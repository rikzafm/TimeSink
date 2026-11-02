using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using TimeSink.Core.Backup;
using TimeSink.Core.Data;
using TimeSink.Core.Data.Migrations;
using TimeSink.Core.Tracking;
using TimeSink.Core.Usage;

namespace TimeSink.Tests.Stress;

public sealed class EncryptionStressTests : IDisposable
{
    private readonly string _testDir;
    private readonly byte[] _key;

    public EncryptionStressTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "timesink-stress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _key = RandomNumberGenerator.GetBytes(32);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public void InterruptionRecovery_LeftoverMigratingFile_RecoversWithoutDataLoss()
    {
        var dbPath = Path.Combine(_testDir, "interrupted_migrating.db");
        var migratingPath = dbPath + ".migrating";

        // 1. Create a populated plaintext database with multiple tables
        using (var plainConn = new SqliteConnection($"Data Source={dbPath};Pooling=false"))
        {
            plainConn.Open();
            M001_Initial.Apply(plainConn);
            M002_BackgroundApps.Apply(plainConn);
            M003_AppKind.Apply(plainConn);
            M004_Devices.Apply(plainConn);

            using var cmdVer = plainConn.CreateCommand();
            cmdVer.CommandText = "PRAGMA user_version = 4;";
            cmdVer.ExecuteNonQuery();

            using var cmd = plainConn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO devices (device_id, name, platform, is_local, first_seen_utc, state)
                VALUES ('stress-dev-1', 'Stress Rig', 'windows', 1, '2026-11-02T00:00:00Z', 'active');

                INSERT INTO apps (id, exe_path, display_name, category, added_utc, device_id)
                VALUES (1, 'c:\stress\app1.exe', 'Stress App 1', 'Work', 1700000000, 'stress-dev-1'),
                       (2, 'c:\stress\app2.exe', 'Stress App 2', 'Game', 1700000100, 'stress-dev-1');

                INSERT INTO sessions (id, app_id, start_utc, end_utc, last_seen_utc, end_reason)
                VALUES (1, 1, 1700000000, 1700001000, 1700001000, 'exit'),
                       (2, 2, 1700000100, 1700002000, 1700002000, 'exit');

                INSERT INTO settings (key, value)
                VALUES ('theme', 'dark'), ('retention_days', '90');
                """;
            cmd.ExecuteNonQuery();
        }

        // Verify it was plaintext
        EncryptionMigrator.IsPlaintextDatabase(dbPath).Should().BeTrue();

        // 2. Simulate interrupted migration by writing dummy/corrupted .migrating file
        File.WriteAllBytes(migratingPath, [0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03]);
        File.Exists(migratingPath).Should().BeTrue();

        // 3. Open database via Db.Open (which cleans stale files and migrates)
        using (var db = Db.Open(dbPath, _key))
        {
            db.SchemaVersion.Should().Be(4);
            var apps = db.GetApps();
            apps.Should().HaveCount(2);
            apps.Select(a => a.DisplayName).Should().Contain(["Stress App 1", "Stress App 2"]);
            db.GetSetting("theme").Should().Be("dark");
            db.GetSetting("retention_days").Should().Be("90");
        }

        // 4. Verify post-conditions
        File.Exists(migratingPath).Should().BeFalse();
        EncryptionMigrator.IsPlaintextDatabase(dbPath).Should().BeFalse();

        // Verify database is encrypted
        var header = new byte[16];
        using (var fs = File.OpenRead(dbPath)) fs.ReadExactly(header);
        Encoding.ASCII.GetString(header).Should().NotStartWith("SQLite format 3");
    }

    [Fact]
    public void InterruptionRecovery_ProcessDiedAfterMoveToBak_RecoversFromBak()
    {
        var dbPath = Path.Combine(_testDir, "crashed_bak.db");
        var bakPath = dbPath + ".bak";
        var migratingPath = dbPath + ".migrating";

        // 1. Create a populated plaintext database
        using (var plainConn = new SqliteConnection($"Data Source={dbPath};Pooling=false"))
        {
            plainConn.Open();
            M001_Initial.Apply(plainConn);
            M002_BackgroundApps.Apply(plainConn);
            M003_AppKind.Apply(plainConn);
            M004_Devices.Apply(plainConn);

            using var cmdVer = plainConn.CreateCommand();
            cmdVer.CommandText = "PRAGMA user_version = 4;";
            cmdVer.ExecuteNonQuery();

            using var cmd = plainConn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO devices (device_id, name, platform, is_local, first_seen_utc, state)
                VALUES ('bak-dev-1', 'Bak Rig', 'windows', 1, '2026-11-02T00:00:00Z', 'active');

                INSERT INTO apps (id, exe_path, display_name, category, added_utc, device_id)
                VALUES (1, 'c:\bak\app.exe', 'Bak App', 'Productivity', 1700000000, 'bak-dev-1');
                """;
            cmd.ExecuteNonQuery();
        }

        // Simulate crash after moving db to .bak, and writing an incomplete .migrating file
        File.Move(dbPath, bakPath);
        File.WriteAllText(migratingPath, "corrupt partial db");
        File.Exists(dbPath).Should().BeFalse();
        File.Exists(bakPath).Should().BeTrue();
        File.Exists(migratingPath).Should().BeTrue();

        // 2. Open database via Db.Open
        using (var db = Db.Open(dbPath, _key))
        {
            db.SchemaVersion.Should().Be(4);
            var apps = db.GetApps();
            apps.Should().HaveCount(1);
            apps[0].DisplayName.Should().Be("Bak App");
        }

        // 3. Verify clean state
        File.Exists(dbPath).Should().BeTrue();
        File.Exists(bakPath).Should().BeFalse();
        File.Exists(migratingPath).Should().BeFalse();
        EncryptionMigrator.IsPlaintextDatabase(dbPath).Should().BeFalse();
    }

    [Fact]
    public void InterruptionRecovery_ProcessDiedAfterReplaceBeforeBakDelete_RetainsEncrypted()
    {
        var dbPath = Path.Combine(_testDir, "replaced_with_bak.db");
        var bakPath = dbPath + ".bak";

        // 1. Create a valid encrypted database as primary
        using (var db = Db.Open(dbPath, _key))
        {
            db.InsertApp(@"c:\enc\app.exe", "Encrypted App", 1700000000);
        }

        // Verify primary is encrypted
        EncryptionMigrator.IsPlaintextDatabase(dbPath).Should().BeFalse();

        // 2. Simulate leftover .bak file
        File.WriteAllText(bakPath, "old plaintext backup left after replace");
        File.Exists(bakPath).Should().BeTrue();

        // 3. Open database
        using (var db = Db.Open(dbPath, _key))
        {
            var apps = db.GetApps();
            apps.Should().HaveCount(1);
            apps[0].DisplayName.Should().Be("Encrypted App");
        }

        // 4. Verify leftover .bak was removed and encrypted db is intact
        File.Exists(bakPath).Should().BeFalse();
        EncryptionMigrator.IsPlaintextDatabase(dbPath).Should().BeFalse();
    }

    [Fact]
    public void InterruptionRecovery_UncheckpointedWal_MigratesCleanlyWithoutDataLoss()
    {
        var dbPath = Path.Combine(_testDir, "wal_migration.db");
        var walPath = dbPath + "-wal";

        // 1. Create plaintext database in WAL mode and insert records without truncate checkpoint
        using (var plainConn = new SqliteConnection($"Data Source={dbPath};Pooling=false"))
        {
            plainConn.Open();
            using (var cmd = plainConn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA journal_mode=WAL;";
                cmd.ExecuteNonQuery();
            }

            M001_Initial.Apply(plainConn);
            M002_BackgroundApps.Apply(plainConn);
            M003_AppKind.Apply(plainConn);
            M004_Devices.Apply(plainConn);

            using (var cmd = plainConn.CreateCommand())
            {
                cmd.CommandText = """
                    PRAGMA user_version = 4;
                    INSERT INTO devices (device_id, name, platform, is_local, first_seen_utc, state)
                    VALUES ('wal-dev', 'Wal Rig', 'windows', 1, '2026-11-02T00:00:00Z', 'active');

                    INSERT INTO apps (id, exe_path, display_name, category, added_utc, device_id)
                    VALUES (1, 'c:\wal\app1.exe', 'Wal App 1', 'Dev', 1700000000, 'wal-dev'),
                           (2, 'c:\wal\app2.exe', 'Wal App 2', 'Dev', 1700000100, 'wal-dev');
                    """;
                cmd.ExecuteNonQuery();
            }
        }

        // Verify it was plaintext
        EncryptionMigrator.IsPlaintextDatabase(dbPath).Should().BeTrue();

        // 2. Open via Db.Open
        using (var db = Db.Open(dbPath, _key))
        {
            var apps = db.GetApps();
            apps.Should().HaveCount(2);
            apps.Select(a => a.DisplayName).Should().Contain(["Wal App 1", "Wal App 2"]);
        }

        // 3. Verify it is now encrypted
        EncryptionMigrator.IsPlaintextDatabase(dbPath).Should().BeFalse();
    }

    [Fact]
    public void EncryptedBackup_ArchiveSnapshotCannotBeOpenedWithoutSqlcipherKey()
    {
        var dbPath = Path.Combine(_testDir, "backup_source.db");
        var backupZip = Path.Combine(_testDir, "backup_enc.timesink");

        // 1. Create live encrypted database with data
        using (var db = Db.Open(dbPath, _key))
        {
            db.InsertApp(@"c:\backup\app.exe", "Backup App", 1700000000);
            db.SetSetting("backup_flag", "true");
        }

        // 2. Create backup archive
        var now = DateTimeOffset.UtcNow;
        BackupService.Create(dbPath, backupZip, now, "1.0.0", _key);
        File.Exists(backupZip).Should().BeTrue();

        // 3. Inspect zip contents directly
        var extractDir = Path.Combine(_testDir, "extracted");
        Directory.CreateDirectory(extractDir);
        ZipFile.ExtractToDirectory(backupZip, extractDir);

        var extractedDb = Path.Combine(extractDir, "timesink.db");
        File.Exists(extractedDb).Should().BeTrue();

        // 4. Inspect magic bytes - must be encrypted
        var header = new byte[16];
        using (var fs = File.OpenRead(extractedDb)) fs.ReadExactly(header);
        Encoding.ASCII.GetString(header).Should().NotStartWith("SQLite format 3");

        // 5. Raw connection without key must fail
        using (var rawConn = new SqliteConnection($"Data Source={extractedDb};Pooling=false"))
        {
            rawConn.Open();
            using var cmd = rawConn.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM sqlite_master;";
            var act = () => cmd.ExecuteScalar();
            act.Should().Throw<SqliteException>();
        }

        // 6. SQLCipher connection with WRONG key must fail
        var wrongKey = RandomNumberGenerator.GetBytes(32);
        using (var wrongConn = Db.CreateConnection(extractedDb, SqliteOpenMode.ReadWrite, wrongKey))
        {
            using var cmd = wrongConn.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM sqlite_master;";
            var act = () => cmd.ExecuteScalar();
            act.Should().Throw<SqliteException>();
        }

        // 7. SQLCipher connection with CORRECT key must succeed and contain data
        using (var correctConn = Db.CreateConnection(extractedDb, SqliteOpenMode.ReadWrite, _key))
        {
            using var cmd = correctConn.CreateCommand();
            cmd.CommandText = "SELECT display_name FROM apps WHERE id = 1;";
            var appName = cmd.ExecuteScalar() as string;
            appName.Should().Be("Backup App");
        }
    }

    [Fact]
    public void RestoreService_Replace_RestoresAndReencryptsTargetDatabase()
    {
        var srcDbPath = Path.Combine(_testDir, "src_replace.db");
        var targetDbPath = Path.Combine(_testDir, "target_replace.db");
        var backupZip = Path.Combine(_testDir, "replace.timesink");
        var safetyDir = Path.Combine(_testDir, "safety");

        // 1. Create source encrypted database and archive it
        using (var db = Db.Open(srcDbPath, _key))
        {
            db.InsertApp(@"c:\replace\app1.exe", "Source App 1", 1700000000);
            db.InsertApp(@"c:\replace\app2.exe", "Source App 2", 1700000100);
        }
        var now = DateTimeOffset.UtcNow;
        BackupService.Create(srcDbPath, backupZip, now, "1.0.0", _key);

        // 2. Create target encrypted database with different content
        using (var db = Db.Open(targetDbPath, _key))
        {
            db.InsertApp(@"c:\target\old.exe", "Old Target App", 1600000000);
        }

        // 3. Execute RestoreService.Replace
        var result = RestoreService.Replace(backupZip, targetDbPath, safetyDir, now, TimeZoneInfo.Utc, _key);

        // 4. Verify safety copy created and is encrypted
        result.SafetyCopy.Should().NotBeNull();
        File.Exists(result.SafetyCopy).Should().BeTrue();
        using (var archive = ZipFile.OpenRead(result.SafetyCopy!))
        {
            var dbEntry = archive.GetEntry("timesink.db")!;
            using var s = dbEntry.Open();
            var h = new byte[16];
            s.ReadExactly(h);
            Encoding.ASCII.GetString(h).Should().NotStartWith("SQLite format 3");
        }

        // 5. Verify target database now contains restored apps
        using (var db = Db.Open(targetDbPath, _key))
        {
            var apps = db.GetApps();
            apps.Should().HaveCount(2);
            apps.Select(a => a.DisplayName).Should().Contain(["Source App 1", "Source App 2"]);
            apps.Select(a => a.DisplayName).Should().NotContain("Old Target App");
        }

        // Verify target database is encrypted
        var header = new byte[16];
        using (var fs = File.OpenRead(targetDbPath)) fs.ReadExactly(header);
        Encoding.ASCII.GetString(header).Should().NotStartWith("SQLite format 3");
    }

    [Fact]
    public void RestoreService_Merge_RestoresDataAndPreservesEncryption()
    {
        var srcDbPath = Path.Combine(_testDir, "src_merge.db");
        var targetDbPath = Path.Combine(_testDir, "target_merge.db");
        var backupZip = Path.Combine(_testDir, "merge.timesink");

        // 1. Create target encrypted database
        using (var db = Db.Open(targetDbPath, _key))
        {
            db.InsertApp(@"c:\shared\app.exe", "Shared App", 1700000000);
            db.InsertApp(@"c:\target\only.exe", "Target Only App", 1700000100);
        }

        // 2. Create source database with shared app and new app, and backup it
        using (var db = Db.Open(srcDbPath, _key))
        {
            db.InsertApp(@"c:\shared\app.exe", "Shared App", 1700000000);
            db.InsertApp(@"c:\source\only.exe", "Source Only App", 1700000200);
        }
        var now = DateTimeOffset.UtcNow;
        BackupService.Create(srcDbPath, backupZip, now, "1.0.0", _key);

        // 3. Merge backup into target
        var mergeResult = RestoreService.Merge(backupZip, targetDbPath, TimeZoneInfo.Utc, _key);
        mergeResult.AppsAdded.Should().Be(1); // "Source Only App" added

        // 4. Verify target has all 3 apps and remains encrypted
        using (var db = Db.Open(targetDbPath, _key))
        {
            var apps = db.GetApps();
            apps.Should().HaveCount(3);
            apps.Select(a => a.DisplayName).Should().Contain(["Shared App", "Target Only App", "Source Only App"]);
        }

        var header = new byte[16];
        using (var fs = File.OpenRead(targetDbPath)) fs.ReadExactly(header);
        Encoding.ASCII.GetString(header).Should().NotStartWith("SQLite format 3");
    }

    [Fact]
    public void RestoreService_CorruptedArchive_ThrowsAndLeavesTargetIntact()
    {
        var targetDbPath = Path.Combine(_testDir, "target_protected.db");
        var corruptedZip = Path.Combine(_testDir, "corrupted.timesink");

        // 1. Create target database
        using (var db = Db.Open(targetDbPath, _key))
        {
            db.InsertApp(@"c:\safe\app.exe", "Safe App", 1700000000);
        }

        // 2. Create corrupted zip archive
        using (var zip = ZipFile.Open(corruptedZip, ZipArchiveMode.Create))
        {
            var manifestEntry = zip.CreateEntry(BackupManifest.EntryName);
            using (var mw = new StreamWriter(manifestEntry.Open(), Encoding.UTF8))
            {
                mw.Write(new BackupManifest(1, 4, "1.0.0", DateTimeOffset.UtcNow).ToJson());
            }

            var dbEntry = zip.CreateEntry(BackupManifest.DbEntryName);
            using (var dw = new StreamWriter(dbEntry.Open(), Encoding.UTF8))
            {
                dw.Write("This is completely corrupt database garbage data!");
            }
        }

        // 3. Inspect should fail
        var inspectAct = () => RestoreService.Inspect(corruptedZip, _key);
        inspectAct.Should().Throw<BackupException>();

        // 4. Replace should fail
        var replaceAct = () => RestoreService.Replace(corruptedZip, targetDbPath, Path.Combine(_testDir, "safety"), DateTimeOffset.UtcNow, TimeZoneInfo.Utc, _key);
        replaceAct.Should().Throw<BackupException>();

        // 5. Target database must remain untouched and fully readable
        using (var db = Db.Open(targetDbPath, _key))
        {
            var apps = db.GetApps();
            apps.Should().HaveCount(1);
            apps[0].DisplayName.Should().Be("Safe App");
        }
    }

    [Fact]
    public void LargeDataset_PlainToEncryptedMigration_PreservesThousandsOfRowsAndIndices()
    {
        var dbPath = Path.Combine(_testDir, "large_plain.db");
        const int appCount = 500;
        const int sessionCount = 1000;

        using (var plainConn = new SqliteConnection($"Data Source={dbPath};Pooling=false"))
        {
            plainConn.Open();
            M001_Initial.Apply(plainConn);
            M002_BackgroundApps.Apply(plainConn);
            M003_AppKind.Apply(plainConn);
            M004_Devices.Apply(plainConn);

            using var cmdVer = plainConn.CreateCommand();
            cmdVer.CommandText = "PRAGMA user_version = 4;";
            cmdVer.ExecuteNonQuery();

            using var tx = plainConn.BeginTransaction();

            using (var cmdDev = plainConn.CreateCommand())
            {
                cmdDev.CommandText = "INSERT INTO devices (device_id, name, platform, is_local, first_seen_utc, state) VALUES ('dev-large', 'Large Rig', 'windows', 1, '2026-11-02T00:00:00Z', 'active');";
                cmdDev.ExecuteNonQuery();
            }

            for (var i = 1; i <= appCount; i++)
            {
                using var cmdApp = plainConn.CreateCommand();
                cmdApp.CommandText = $"INSERT INTO apps (id, exe_path, display_name, category, added_utc, device_id) VALUES ({i}, 'c:\\apps\\app{i}.exe', 'App {i}', 'Work', 1700000000 + {i}, 'dev-large');";
                cmdApp.ExecuteNonQuery();
            }

            for (var j = 1; j <= sessionCount; j++)
            {
                var appId = (j % appCount) + 1;
                using var cmdSess = plainConn.CreateCommand();
                cmdSess.CommandText = $"INSERT INTO sessions (id, app_id, start_utc, end_utc, last_seen_utc, end_reason) VALUES ({j}, {appId}, 1700000000 + {j}, 1700000100 + {j}, 1700000100 + {j}, 'exit');";
                cmdSess.ExecuteNonQuery();
            }

            tx.Commit();
        }

        // Verify plaintext
        EncryptionMigrator.IsPlaintextDatabase(dbPath).Should().BeTrue();

        // Migrate via Db.Open
        using (var db = Db.Open(dbPath, _key))
        {
            db.SchemaVersion.Should().Be(4);
            using var cmdCount = db.Connection.CreateCommand();
            cmdCount.CommandText = "SELECT COUNT(*) FROM apps;";
            Convert.ToInt32(cmdCount.ExecuteScalar()).Should().Be(appCount);

            cmdCount.CommandText = "SELECT COUNT(*) FROM sessions;";
            Convert.ToInt32(cmdCount.ExecuteScalar()).Should().Be(sessionCount);
        }

        // Verify encryption
        EncryptionMigrator.IsPlaintextDatabase(dbPath).Should().BeFalse();
    }

    [Fact]
    public void ReadOnlyConnection_CanReadEncryptedDatabase_AndRejectsWrites()
    {
        var dbPath = Path.Combine(_testDir, "readonly.db");

        // 1. Create and populate encrypted db
        using (var db = Db.Open(dbPath, _key))
        {
            db.InsertApp(@"c:\readonly\app.exe", "Read Only App", 1700000000);
        }

        // 2. Open read-only connection
        using (var roConn = Db.CreateConnection(dbPath, SqliteOpenMode.ReadOnly, _key))
        {
            // Reading must succeed
            using var readCmd = roConn.CreateCommand();
            readCmd.CommandText = "SELECT display_name FROM apps WHERE id = 1;";
            (readCmd.ExecuteScalar() as string).Should().Be("Read Only App");

            // Writing must throw SqliteException (attempt to write a readonly database)
            using var writeCmd = roConn.CreateCommand();
            writeCmd.CommandText = "INSERT INTO settings (key, value) VALUES ('k', 'v');";
            var act = () => writeCmd.ExecuteNonQuery();
            act.Should().Throw<SqliteException>();
        }
    }

    [Fact]
    public void RestoreService_Replace_ClosesActiveUnfinishedSessions()
    {
        var srcDbPath = Path.Combine(_testDir, "src_active.db");
        var targetDbPath = Path.Combine(_testDir, "target_active.db");
        var backupZip = Path.Combine(_testDir, "active_sessions.timesink");
        var safetyDir = Path.Combine(_testDir, "safety_active");

        // 1. Create source db with an active open session (end_utc is null)
        using (var db = Db.Open(srcDbPath, _key))
        {
            var appId = db.InsertApp(@"c:\active\app.exe", "Active App", 1700000000);
            using var cmd = db.Connection.CreateCommand();
            cmd.CommandText = $"INSERT INTO sessions (app_id, start_utc, end_utc, last_seen_utc, end_reason) VALUES ({appId}, 1700000000, NULL, 1700000500, NULL);";
            cmd.ExecuteNonQuery();
        }

        var now = DateTimeOffset.UtcNow;
        BackupService.Create(srcDbPath, backupZip, now, "1.0.0", _key);

        // 2. Target db
        using (var db = Db.Open(targetDbPath, _key)) { }

        // 3. Restore Replace
        RestoreService.Replace(backupZip, targetDbPath, safetyDir, now, TimeZoneInfo.Utc, _key);

        // 4. Verify session is closed with end_reason = 'restore' and end_utc = last_seen_utc
        using (var db = Db.Open(targetDbPath, _key))
        {
            using var cmd = db.Connection.CreateCommand();
            cmd.CommandText = "SELECT end_utc, last_seen_utc, end_reason FROM sessions WHERE app_id = 1;";
            using var reader = cmd.ExecuteReader();
            reader.Read().Should().BeTrue();
            reader.GetInt64(0).Should().Be(1700000500); // end_utc set to last_seen_utc
            reader.GetString(2).Should().Be("restore");
        }
    }

    [Fact]
    public void NativeAot_PublishedAgent_RunsAndCreatesValidEncryptedDatabase()
    {
        // Locate published TimeSink.Agent.exe
        var currentDir = new DirectoryInfo(Directory.GetCurrentDirectory());
        string? repoRoot = null;
        for (var dir = currentDir; dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "timesink.sln")))
            {
                repoRoot = dir.FullName;
                break;
            }
        }

        repoRoot.Should().NotBeNull("Solution root directory must be locatable");
        var agentExe = Path.Combine(repoRoot!, "src", "TimeSink.Agent", "bin", "Release", "net10.0-windows", "win-x64", "publish", "TimeSink.Agent.exe");
        File.Exists(agentExe).Should().BeTrue($"Published agent must exist at {agentExe}");

        var testDb = Path.Combine(_testDir, "agent_smoke.db");
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = agentExe,
            Arguments = $"--smoke \"{testDb}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.WaitForExit(10000).Should().BeTrue();

        File.Exists(testDb).Should().BeTrue();

        // 1. Verify encrypted header
        var header = new byte[16];
        using (var fs = File.OpenRead(testDb)) fs.ReadExactly(header);
        Encoding.ASCII.GetString(header).Should().NotStartWith("SQLite format 3");

        // 2. Open with Db.Open using default DPAPI key
        using var db = Db.Open(testDb);
        db.SchemaVersion.Should().Be(4);
        var apps = db.GetApps();
        apps.Should().HaveCount(1);
        apps[0].ExePath.Should().Be(@"c:\smoke\test.exe");
        apps[0].DisplayName.Should().Be("Smoke");
    }
}
