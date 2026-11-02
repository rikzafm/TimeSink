using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;
using TimeSink.Core.Backup;
using TimeSink.Core.Data;

namespace TimeSink.Tests.Backup;

public sealed class EncryptedBackupTests : IDisposable
{
    private readonly BackupTestRig _rig = new();
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    public void Dispose() => _rig.Dispose();

    [Fact]
    public void Backup_preserves_encryption_in_archive()
    {
        var dbPath = _rig.NewDb("live_enc.db", (@"c:\test\app.exe", 1_700_000_000, 1_700_003_600));
        var backupZip = _rig.P("enc.timesink");

        BackupService.Create(dbPath, backupZip, Now, "1.0.0");

        File.Exists(backupZip).Should().BeTrue();

        using (var archive = ZipFile.OpenRead(backupZip))
        {
            var dbEntry = archive.GetEntry(BackupManifest.DbEntryName);
            dbEntry.Should().NotBeNull();

            using var s = dbEntry!.Open();
            var header = new byte[16];
            s.ReadExactly(header);

            // Entry in archive must be encrypted SQLCipher format, not plaintext SQLite
            var headerStr = Encoding.ASCII.GetString(header);
            headerStr.Should().NotStartWith("SQLite format 3");
        }

        // Inspection should succeed
        var inspected = RestoreService.Inspect(backupZip);
        inspected.Apps.Should().Be(1);
        inspected.Sessions.Should().Be(1);
    }

    [Fact]
    public void Restore_reencrypts_legacy_unencrypted_backup()
    {
        // 1. Create a legacy unencrypted SQLite database
        var plainDbPath = _rig.P("legacy_plain.db");
        using (var conn = new SqliteConnection($"Data Source={plainDbPath};Pooling=false"))
        {
            conn.Open();
            using (var tx = conn.BeginTransaction())
            {
                TimeSink.Core.Data.Migrations.M001_Initial.Apply(conn);
                TimeSink.Core.Data.Migrations.M002_BackgroundApps.Apply(conn);
                TimeSink.Core.Data.Migrations.M003_AppKind.Apply(conn);
                TimeSink.Core.Data.Migrations.M004_Devices.Apply(conn);

                using var vCmd = conn.CreateCommand();
                vCmd.CommandText = "PRAGMA user_version = 4;";
                vCmd.ExecuteNonQuery();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO devices (device_id, name, platform, is_local, first_seen_utc, state)
                    VALUES ('dev-1', 'Test Device', 'windows', 1, '2026-11-02T00:00:00Z', 'active');

                    INSERT INTO apps (id, exe_path, display_name, category, added_utc, device_id)
                    VALUES (1, 'c:\legacy\app.exe', 'Legacy App', 'Utilities', 1700000000, 'dev-1');

                    INSERT INTO sessions (id, app_id, start_utc, end_utc, last_seen_utc, end_reason)
                    VALUES (1, 1, 1700000000, 1700001000, 1700001000, 'exit');
                    """;
                cmd.ExecuteNonQuery();
                tx.Commit();
            }
        }

        // Verify it was created unencrypted
        var header = new byte[16];
        using (var fs = File.OpenRead(plainDbPath))
        {
            fs.ReadExactly(header);
        }
        Encoding.ASCII.GetString(header).Should().StartWith("SQLite format 3");

        // 2. Package into a legacy .timesink archive
        var legacyZipPath = _rig.P("legacy.timesink");
        using (var archive = ZipFile.Open(legacyZipPath, ZipArchiveMode.Create))
        {
            var manifest = new BackupManifest(1, 4, "0.9.0", Now);
            var mEntry = archive.CreateEntry(BackupManifest.EntryName);
            using (var mw = new StreamWriter(mEntry.Open(), Encoding.UTF8))
            {
                mw.Write(manifest.ToJson());
            }

            archive.CreateEntryFromFile(plainDbPath, BackupManifest.DbEntryName);
        }

        // 3. Inspect works on legacy unencrypted archive
        var inspected = RestoreService.Inspect(legacyZipPath);
        inspected.Apps.Should().Be(1);
        inspected.Sessions.Should().Be(1);

        // 4. Restore replacing target database
        var targetDbPath = _rig.P("target_live.db");
        // Initialize target database with Db.Open so it has local key and schema
        using (var db = Db.Open(targetDbPath)) { }

        RestoreService.Replace(legacyZipPath, targetDbPath, _rig.P("backups"), Now, TimeZoneInfo.Utc);

        // 5. Verify target database is now encrypted
        using (var fs = File.OpenRead(targetDbPath))
        {
            fs.ReadExactly(header);
        }
        Encoding.ASCII.GetString(header).Should().NotStartWith("SQLite format 3");

        // 6. Verify data was successfully copied and is accessible with local key
        _rig.Scalar<long>(targetDbPath, "SELECT COUNT(*) FROM apps").Should().Be(1);
        _rig.Scalar<long>(targetDbPath, "SELECT COUNT(*) FROM sessions").Should().Be(1);
        _rig.Scalar<string>(targetDbPath, "SELECT display_name FROM apps WHERE id = 1").Should().Be("Legacy App");
    }
}
