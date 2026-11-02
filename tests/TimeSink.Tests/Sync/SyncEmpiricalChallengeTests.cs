using System.IO.Compression;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using TimeSink.Core.Data;
using TimeSink.Core.Devices;
using TimeSink.Core.Security;
using TimeSink.Core.Settings;
using TimeSink.Core.Sync;
using TimeSink.Tests.Backup;
using Xunit;

namespace TimeSink.Tests.Sync;

[SupportedOSPlatform("windows")]
public sealed class SyncEmpiricalChallengeTests
{
    private sealed class TestKeyManager : IKeyManager
    {
        private byte[] _dbKey = RandomNumberGenerator.GetBytes(32);
        private string? _syncPassphrase;

        public TestKeyManager(string? syncPassphrase = null)
        {
            _syncPassphrase = syncPassphrase;
        }

        public byte[] GetOrCreateDatabaseKey() => (byte[])_dbKey.Clone();
        public void DeleteDatabaseKey() => _dbKey = RandomNumberGenerator.GetBytes(32);
        public string? GetSyncPassphrase() => _syncPassphrase;
        public void SetSyncPassphrase(string passphrase) => _syncPassphrase = passphrase;
        public bool HasSyncPassphrase() => !string.IsNullOrEmpty(_syncPassphrase);
        public void ClearSyncPassphrase() => _syncPassphrase = null;
    }

    private static void SetDevice(BackupTestRig rig, string dbPath, string deviceId, string deviceName, string platform)
    {
        rig.Exec(dbPath, "PRAGMA foreign_keys = OFF; " +
                         $"INSERT OR REPLACE INTO settings (key, value) VALUES ('device.id', '{deviceId}'), ('device.name', '{deviceName}'); " +
                         $"UPDATE devices SET device_id = '{deviceId}', name = '{deviceName}', platform = '{platform}' WHERE is_local = 1; " +
                         $"UPDATE apps SET device_id = '{deviceId}'; " +
                         "PRAGMA foreign_keys = ON;");
    }

    [Fact]
    public void Challenge1_EncryptedExport_SnapshotCannotBeReadWithoutKey_OrWithWrongKey()
    {
        using var rig = new BackupTestRig();
        var syncFolder = rig.P("sync");
        var now = DateTimeOffset.Parse("2026-11-02T12:00:00Z");
        const string passphrase = "empirical-test-passphrase-2026!";

        var testKeyManager = new TestKeyManager(passphrase);

        // 1. Setup local source DB with sessions and apps
        var srcDb = rig.NewDb("source.db",
            (@"/Applications/Xcode.app", 1761984000, 1761987600),
            (@"/Applications/Safari.app", 1761988000, 1761989800));
        SetDevice(rig, srcDb, "dev-mac-001", "MacBook Pro", "macos");

        // Enable sync encryption
        rig.Exec(srcDb, "INSERT OR REPLACE INTO settings (key, value) VALUES ('sync.encrypted', 'true');");

        // 2. Export encrypted snapshot
        var snapshotPath = SyncWriter.Write(srcDb, syncFolder, now, "1.2.0", testKeyManager);
        File.Exists(snapshotPath).Should().BeTrue();

        // 3. Inspect archive entries
        string extractedDb = rig.P("extracted_snapshot.db");
        SyncManifest manifest;
        using (var zip = ZipFile.OpenRead(snapshotPath))
        {
            var manifestEntry = zip.GetEntry(SyncManifest.EntryName);
            manifestEntry.Should().NotBeNull();
            using (var reader = new StreamReader(manifestEntry!.Open(), Encoding.UTF8))
            {
                manifest = SyncManifest.Parse(reader.ReadToEnd());
            }

            var dbEntry = zip.GetEntry(SyncManifest.DbEntryName);
            dbEntry.Should().NotBeNull();
            dbEntry!.ExtractToFile(extractedDb);
        }

        // Verify manifest properties
        manifest.Encrypted.Should().BeTrue("Manifest must signal that snapshot.db is encrypted");
        manifest.SchemaVersion.Should().BeGreaterOrEqualTo(5, "Encrypted sync archives must have schema version >= 5");
        manifest.SessionCount.Should().Be(2);

        // 4. Verify physical file is encrypted (no plain SQLite magic header)
        var rawBytes = File.ReadAllBytes(extractedDb);
        var header = Encoding.ASCII.GetString(rawBytes, 0, Math.Min(16, rawBytes.Length));
        header.Should().NotStartWith("SQLite format 3", "Encrypted SQLCipher database must not have plaintext SQLite header");

        // 5. Verify standard unkeyed SQLite connection FAILS to read snapshot.db
        using (var unkeyedConn = new SqliteConnection(new SqliteConnectionStringBuilder
               { DataSource = extractedDb, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        {
            unkeyedConn.Open();
            using var cmd = unkeyedConn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sessions;";
            var act = () => cmd.ExecuteScalar();
            act.Should().Throw<SqliteException>("Unkeyed SQLite connection must fail to read encrypted database");
        }

        // 6. Verify SQLite connection with WRONG key fails
        using (var wrongKeyConn = new SqliteConnection(new SqliteConnectionStringBuilder
               { DataSource = extractedDb, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        {
            wrongKeyConn.Open();
            using var pragma = wrongKeyConn.CreateCommand();
            pragma.CommandText = "PRAGMA key = 'wrong-passphrase-totally-incorrect';";
            pragma.ExecuteNonQuery();

            using var cmd = wrongKeyConn.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check;";
            var act = () =>
            {
                var result = cmd.ExecuteScalar() as string;
                if (result != "ok")
                {
                    throw new SqliteException("Integrity check failed with wrong key", 26 /* SQLITE_NOTADB */);
                }
            };
            act.Should().Throw<SqliteException>("Connection with incorrect key must fail integrity check or query");
        }

        // 7. Verify SQLite connection with CORRECT key succeeds
        using (var correctKeyConn = new SqliteConnection(new SqliteConnectionStringBuilder
               { DataSource = extractedDb, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        {
            correctKeyConn.Open();
            using var pragma = correctKeyConn.CreateCommand();
            pragma.CommandText = $"PRAGMA key = '{passphrase.Replace("'", "''")}';";
            pragma.ExecuteNonQuery();

            using var checkCmd = correctKeyConn.CreateCommand();
            checkCmd.CommandText = "PRAGMA integrity_check;";
            var checkResult = checkCmd.ExecuteScalar() as string;
            checkResult.Should().Be("ok", "Opening with correct key must pass integrity check");

            using var countCmd = correctKeyConn.CreateCommand();
            countCmd.CommandText = "SELECT COUNT(*) FROM sessions;";
            var count = Convert.ToInt32(countCmd.ExecuteScalar());
            count.Should().Be(2, "Sessions count must match exported data");
        }
    }

    [Fact]
    public void Challenge1b_EncryptedExport_SupportsPassphraseWithQuotesAndSpecialChars()
    {
        using var rig = new BackupTestRig();
        var syncFolder = rig.P("sync");
        var now = DateTimeOffset.Parse("2026-11-02T12:00:00Z");
        const string specialPassphrase = "Complex'Pass;phrase\"With-Symbols!@#$%^&*()_+";

        var testKeyManager = new TestKeyManager(specialPassphrase);

        var srcDb = rig.NewDb("source.db", (@"/usr/local/bin/node", 1761984000, 1761987600));
        SetDevice(rig, srcDb, "dev-linux-001", "Linux Server", "macos");
        rig.Exec(srcDb, "INSERT OR REPLACE INTO settings (key, value) VALUES ('sync.encrypted', 'true');");

        var snapshotPath = SyncWriter.Write(srcDb, syncFolder, now, "1.2.0", testKeyManager);

        var dstDb = rig.NewDb("dest.db");
        SetDevice(rig, dstDb, "dev-win-001", "Windows PC", "windows");

        var result = SyncReader.Import(snapshotPath, dstDb, TimeZoneInfo.Utc, testKeyManager);
        result.Status.Should().Be(SyncStatus.Success, "Passphrase with quotes and special characters should round-trip cleanly");
        result.SessionsAdded.Should().Be(1);
    }

    [Fact]
    public void Challenge2_CiphertextHashCoverage_ManifestHashMatchesRawEncryptedSnapshotBytes()
    {
        using var rig = new BackupTestRig();
        var syncFolder = rig.P("sync");
        var now = DateTimeOffset.Parse("2026-11-02T12:00:00Z");
        const string passphrase = "hash-coverage-verification-key";

        var testKeyManager = new TestKeyManager(passphrase);

        var srcDb = rig.NewDb("source.db",
            (@"c:\games\game.exe", 1761984000, 1761987600),
            (@"c:\work\app.exe", 1761988000, 1761991600));
        SetDevice(rig, srcDb, "dev-win-001", "Gaming PC", "windows");
        rig.Exec(srcDb, "INSERT OR REPLACE INTO settings (key, value) VALUES ('sync.encrypted', 'true');");

        var snapshotPath = SyncWriter.Write(srcDb, syncFolder, now, "1.2.0", testKeyManager);

        // 1. Extract raw snapshot.db and manifest
        string extractedDb = rig.P("cipher_snapshot.db");
        SyncManifest manifest;
        using (var zip = ZipFile.OpenRead(snapshotPath))
        {
            var mEntry = zip.GetEntry(SyncManifest.EntryName)!;
            using var reader = new StreamReader(mEntry.Open(), Encoding.UTF8);
            manifest = SyncManifest.Parse(reader.ReadToEnd());

            var dbEntry = zip.GetEntry(SyncManifest.DbEntryName)!;
            dbEntry.ExtractToFile(extractedDb);
        }

        // 2. Compute SHA-256 directly over raw extracted bytes
        string actualCiphertextHash;
        using (var sha256 = SHA256.Create())
        using (var fs = File.OpenRead(extractedDb))
        {
            actualCiphertextHash = Convert.ToHexStringLower(sha256.ComputeHash(fs));
        }

        // Verify manifest hash covers the encrypted bytes
        manifest.Hash.Should().Be(actualCiphertextHash, "manifest.Hash must exactly match the SHA-256 of the raw encrypted snapshot.db bytes");

        // 3. Tamper with 1 byte in the encrypted snapshot.db and verify BadHash rejection
        var tamperedZip = rig.P("tampered.tssync");
        File.Copy(snapshotPath, tamperedZip);

        using (var zip = ZipFile.Open(tamperedZip, ZipArchiveMode.Update))
        {
            var dbEntry = zip.GetEntry(SyncManifest.DbEntryName)!;
            byte[] dbBytes;
            using (var stream = dbEntry.Open())
            using (var ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                dbBytes = ms.ToArray();
            }
            dbEntry.Delete();

            // Flip one byte in ciphertext
            dbBytes[100] ^= 0xFF;

            var newEntry = zip.CreateEntry(SyncManifest.DbEntryName, CompressionLevel.Optimal);
            using (var stream = newEntry.Open())
            {
                stream.Write(dbBytes);
            }
        }

        var dstDb = rig.NewDb("dest.db");
        SetDevice(rig, dstDb, "dev-mac-001", "MacBook", "macos");

        var tamperResult = SyncReader.Import(tamperedZip, dstDb, TimeZoneInfo.Utc, testKeyManager);
        tamperResult.Status.Should().Be(SyncStatus.BadHash, "Tampered ciphertext must fail SHA-256 verification before decryption");
    }

    [Fact]
    public void Challenge3_SyncHost_EncryptionKeyMissing_PausesRetryCycle_AndSyncNowUnpauses()
    {
        using var rig = new BackupTestRig();
        var syncFolder = rig.P("sync");
        Directory.CreateDirectory(syncFolder);

        var remoteNow = DateTimeOffset.Parse("2026-11-02T12:00:00Z");
        const string passphrase = "remote-configured-secret";

        // Remote device creates encrypted snapshot
        var remoteDb = rig.NewDb("remote.db", (@"/Applications/Safari.app", 1761984000, 1761987600));
        SetDevice(rig, remoteDb, "dev-mac-001", "Remote Mac", "macos");
        rig.Exec(remoteDb, "INSERT OR REPLACE INTO settings (key, value) VALUES ('sync.encrypted', 'true');");
        SyncWriter.Write(remoteDb, syncFolder, remoteNow, "1.2.0", new TestKeyManager(passphrase));

        // Local device setup with NO passphrase configured
        var localDb = rig.NewDb("local.db");
        SetDevice(rig, localDb, "dev-win-001", "Local PC", "windows");
        rig.Exec(localDb, "PRAGMA foreign_keys = OFF; " +
                          "INSERT OR REPLACE INTO settings (key, value) VALUES " +
                          "('sync.enabled', 'true'), ('sync.folder', '" + syncFolder.Replace("'", "''") + "'); " +
                          "PRAGMA foreign_keys = ON;");

        var localKeyManager = new TestKeyManager(syncPassphrase: null);
        var clockTime = remoteNow.AddMinutes(1);
        using var host = new SyncHost(localDb, TimeZoneInfo.Utc, () => clockTime, keyManager: localKeyManager);

        // 1. Initial check detects encrypted file without key
        host.Check();

        AppSettings.ForFile(localDb).Get(Setting.SyncState).Should().Be("encryption_key_missing");

        // 2. Advance time by normal check interval (15s, 1min, 5min)
        clockTime = clockTime.AddMinutes(5);
        // host.Check() should NOT re-run because it is paused in backoff (_backoffUntil == DateTimeOffset.MaxValue)
        host.Check();
        AppSettings.ForFile(localDb).Get(Setting.SyncState).Should().Be("encryption_key_missing");

        // 3. User configures passphrase and calls SyncNow()
        localKeyManager.SetSyncPassphrase(passphrase);
        host.SyncNow();

        // Verify unpaused immediately and imported successfully
        AppSettings.ForFile(localDb).Get(Setting.SyncState).Should().Be("synced");
        rig.Scalar<long>(localDb, "SELECT COUNT(*) FROM sessions WHERE app_id IN (SELECT id FROM apps WHERE device_id = 'dev-mac-001')").Should().Be(1);
    }

    [Fact]
    public void Challenge3b_SyncHost_EncryptionKeyWrong_PausesRetryCycle_AndSyncNowUnpauses()
    {
        using var rig = new BackupTestRig();
        var syncFolder = rig.P("sync");
        Directory.CreateDirectory(syncFolder);

        var remoteNow = DateTimeOffset.Parse("2026-11-02T12:00:00Z");
        const string correctPassphrase = "correct-remote-secret";
        const string wrongPassphrase = "wrong-local-secret";

        // Remote device creates encrypted snapshot
        var remoteDb = rig.NewDb("remote.db", (@"/Applications/Safari.app", 1761984000, 1761987600));
        SetDevice(rig, remoteDb, "dev-mac-001", "Remote Mac", "macos");
        rig.Exec(remoteDb, "INSERT OR REPLACE INTO settings (key, value) VALUES ('sync.encrypted', 'true');");
        SyncWriter.Write(remoteDb, syncFolder, remoteNow, "1.2.0", new TestKeyManager(correctPassphrase));

        // Local device setup with WRONG passphrase
        var localDb = rig.NewDb("local.db");
        SetDevice(rig, localDb, "dev-win-001", "Local PC", "windows");
        rig.Exec(localDb, "PRAGMA foreign_keys = OFF; " +
                          "INSERT OR REPLACE INTO settings (key, value) VALUES " +
                          "('sync.enabled', 'true'), ('sync.folder', '" + syncFolder.Replace("'", "''") + "'); " +
                          "PRAGMA foreign_keys = ON;");

        var localKeyManager = new TestKeyManager(syncPassphrase: wrongPassphrase);
        var clockTime = remoteNow.AddMinutes(1);
        using var host = new SyncHost(localDb, TimeZoneInfo.Utc, () => clockTime, keyManager: localKeyManager);

        // 1. Initial check fails decryption with wrong key
        host.Check();

        AppSettings.ForFile(localDb).Get(Setting.SyncState).Should().Be("encryption_key_wrong");

        // 2. Normal periodic check remains paused in backoff
        clockTime = clockTime.AddMinutes(5);
        host.Check();
        AppSettings.ForFile(localDb).Get(Setting.SyncState).Should().Be("encryption_key_wrong");

        // 3. User corrects passphrase and calls SyncNow()
        localKeyManager.SetSyncPassphrase(correctPassphrase);
        host.SyncNow();

        // Verify unpaused and imported
        AppSettings.ForFile(localDb).Get(Setting.SyncState).Should().Be("synced");
        rig.Scalar<long>(localDb, "SELECT COUNT(*) FROM apps WHERE device_id = 'dev-mac-001'").Should().Be(1);
    }

    [Fact]
    public void Challenge3c_SyncHost_RemoteNotEncrypted_PausesRetryCycle_AndSyncNowUnpauses()
    {
        using var rig = new BackupTestRig();
        var syncFolder = rig.P("sync");
        Directory.CreateDirectory(syncFolder);

        var remoteNow = DateTimeOffset.Parse("2026-11-02T12:00:00Z");

        // Remote device creates UNENCRYPTED snapshot
        var remoteDb = rig.NewDb("remote.db", (@"/Applications/Safari.app", 1761984000, 1761987600));
        SetDevice(rig, remoteDb, "dev-mac-001", "Remote Mac", "macos");
        SyncWriter.Write(remoteDb, syncFolder, remoteNow, "1.0.0");

        // Local device expects ENCRYPTION
        var localDb = rig.NewDb("local.db");
        SetDevice(rig, localDb, "dev-win-001", "Local PC", "windows");
        rig.Exec(localDb, "PRAGMA foreign_keys = OFF; " +
                          "INSERT OR REPLACE INTO settings (key, value) VALUES " +
                          "('sync.enabled', 'true'), ('sync.folder', '" + syncFolder.Replace("'", "''") + "'), " +
                          "('sync.encrypted', 'true'); " +
                          "PRAGMA foreign_keys = ON;");

        var localKeyManager = new TestKeyManager("local-secret");
        var clockTime = remoteNow.AddMinutes(1);
        using var host = new SyncHost(localDb, TimeZoneInfo.Utc, () => clockTime, keyManager: localKeyManager);

        // 1. Initial check flags remote_not_encrypted
        host.Check();

        AppSettings.ForFile(localDb).Get(Setting.SyncState).Should().Be("remote_not_encrypted");

        // 2. Normal periodic check remains paused in backoff
        clockTime = clockTime.AddMinutes(5);
        host.Check();
        AppSettings.ForFile(localDb).Get(Setting.SyncState).Should().Be("remote_not_encrypted");

        // 3. Remote updates to encrypted snapshot
        rig.Exec(remoteDb, "INSERT OR REPLACE INTO settings (key, value) VALUES ('sync.encrypted', 'true');");
        SyncWriter.Write(remoteDb, syncFolder, clockTime, "1.2.0", new TestKeyManager("local-secret"));

        // 4. Local device calls SyncNow()
        host.SyncNow();

        AppSettings.ForFile(localDb).Get(Setting.SyncState).Should().Be("synced");
    }

    [Fact]
    public void Challenge4_BackwardCompatibility_UnencryptedSchema4Archive_ReadWhenEncryptionDisabled()
    {
        using var rig = new BackupTestRig();
        var syncFolder = rig.P("sync");
        Directory.CreateDirectory(syncFolder);

        // 1. Construct genuine schema 4 unencrypted snapshot
        var rawDb = rig.P("legacy_snap.db");
        using (var snapConn = new SqliteConnection($"Data Source={rawDb};Pooling=false"))
        {
            snapConn.Open();
            using var cmd = snapConn.CreateCommand();
            cmd.CommandText = """
                PRAGMA user_version = 4;
                CREATE TABLE apps (
                    id INTEGER PRIMARY KEY,
                    canonical_id INTEGER NULL,
                    exe_path TEXT NOT NULL,
                    bundle_id TEXT NULL,
                    device_id TEXT NOT NULL,
                    package_family TEXT NULL,
                    display_name TEXT NOT NULL,
                    category TEXT NOT NULL DEFAULT 'Other',
                    state TEXT NOT NULL DEFAULT 'tracked',
                    added_utc INTEGER NOT NULL,
                    retired_utc INTEGER NULL,
                    is_background INTEGER NOT NULL DEFAULT 0,
                    kind INTEGER NULL
                );
                CREATE TABLE sessions (
                    id INTEGER PRIMARY KEY,
                    app_id INTEGER NOT NULL REFERENCES apps(id),
                    start_utc INTEGER NOT NULL,
                    end_utc INTEGER NULL,
                    last_seen_utc INTEGER NOT NULL,
                    pid INTEGER NULL,
                    proc_start_utc INTEGER NULL,
                    end_reason TEXT NULL
                );
                CREATE TABLE icons (
                    app_id INTEGER PRIMARY KEY,
                    png BLOB NOT NULL,
                    hash TEXT NOT NULL
                );
                CREATE TABLE cross_device_merges (
                    source_device_id TEXT NOT NULL,
                    source_exe_path TEXT NOT NULL DEFAULT '',
                    source_bundle_id TEXT NOT NULL DEFAULT '',
                    target_device_id TEXT NOT NULL,
                    target_exe_path TEXT NOT NULL DEFAULT '',
                    target_bundle_id TEXT NOT NULL DEFAULT ''
                );
                INSERT INTO apps (id, exe_path, bundle_id, device_id, display_name, category, added_utc)
                VALUES (1, '/Applications/Notes.app', 'com.apple.Notes', 'dev-mac-legacy', 'Notes', 'Productivity', 1760000000);
                INSERT INTO sessions (id, app_id, start_utc, end_utc, last_seen_utc, end_reason)
                VALUES (1, 1, 1761984000, 1761987600, 1761987600, 'exit');
                """;
            cmd.ExecuteNonQuery();
        }

        string dbHash;
        using (var sha256 = SHA256.Create())
        using (var fs = File.OpenRead(rawDb))
        {
            dbHash = Convert.ToHexStringLower(sha256.ComputeHash(fs));
        }

        // Write schema 4 manifest JSON WITHOUT "encrypted" key
        var schema4Json = $$"""
        {
          "format": 1,
          "schemaVersion": 4,
          "appVersion": "1.0.0",
          "deviceId": "dev-mac-legacy",
          "deviceName": "Legacy MacBook",
          "platform": "macos",
          "createdUtc": "2026-11-01T12:00:00Z",
          "sessionCount": 1,
          "hash": "{{dbHash}}"
        }
        """;

        var legacyArchive = rig.P("dev-mac-legacy.tssync");
        using (var zip = ZipFile.Open(legacyArchive, ZipArchiveMode.Create))
        {
            var mEntry = zip.CreateEntry(SyncManifest.EntryName);
            using (var writer = new StreamWriter(mEntry.Open(), new UTF8Encoding(false)))
            {
                writer.Write(schema4Json);
            }
            zip.CreateEntryFromFile(rawDb, SyncManifest.DbEntryName);
        }

        // 2. Local device with encryption DISABLED imports schema 4 archive
        var localDb = rig.NewDb("dest_unencrypted.db");
        SetDevice(rig, localDb, "dev-win-001", "Windows PC", "windows");
        // sync.encrypted is false by default

        var result = SyncReader.Import(legacyArchive, localDb, TimeZoneInfo.Utc);
        result.Status.Should().Be(SyncStatus.Success, "Schema 4 archive must be supported when local encryption is false");
        result.AppsAdded.Should().Be(1);
        result.SessionsAdded.Should().Be(1);

        rig.Scalar<long>(localDb, "SELECT COUNT(*) FROM apps WHERE device_id = 'dev-mac-legacy'").Should().Be(1);
        rig.Scalar<string>(localDb, "SELECT display_name FROM apps WHERE device_id = 'dev-mac-legacy'").Should().Be("Notes");

        // 3. Local device with encryption ENABLED rejects schema 4 archive with RemoteNotEncrypted
        var localEncDb = rig.NewDb("dest_encrypted.db");
        SetDevice(rig, localEncDb, "dev-win-001", "Windows PC", "windows");
        rig.Exec(localEncDb, "INSERT OR REPLACE INTO settings (key, value) VALUES ('sync.encrypted', 'true');");

        var encResult = SyncReader.Import(legacyArchive, localEncDb, TimeZoneInfo.Utc);
        encResult.Status.Should().Be(SyncStatus.RemoteNotEncrypted, "Schema 4 unencrypted archive must be rejected when local encryption is enabled");
    }

    [Fact]
    public void Challenge5_KeyManager_DpapiKeyManager_SyncPassphraseRoundTrip()
    {
        using var rig = new BackupTestRig();
        var keyFile = rig.P("test.dbkey");
        var syncKeyFile = rig.P("test.synckey");

        var keyManager = new DpapiKeyManager(keyFile, syncKeyFile);

        // Initially no passphrase configured
        keyManager.HasSyncPassphrase().Should().BeFalse();
        keyManager.GetSyncPassphrase().Should().BeNull();

        // Set passphrase
        const string passphrase = "dpapi-protected-sync-key-2026";
        keyManager.SetSyncPassphrase(passphrase);

        keyManager.HasSyncPassphrase().Should().BeTrue();
        keyManager.GetSyncPassphrase().Should().Be(passphrase);

        // New instance targeting the same file should retrieve the passphrase via DPAPI
        var secondManager = new DpapiKeyManager(keyFile, syncKeyFile);
        secondManager.HasSyncPassphrase().Should().BeTrue();
        secondManager.GetSyncPassphrase().Should().Be(passphrase);

        // Clear passphrase
        keyManager.ClearSyncPassphrase();
        keyManager.HasSyncPassphrase().Should().BeFalse();
        keyManager.GetSyncPassphrase().Should().BeNull();
        secondManager.HasSyncPassphrase().Should().BeFalse();
    }

    [Fact]
    public void Challenge6_SqlInjectionPayloadInPassphrase_DoesNotCorruptOrInjectSql()
    {
        using var rig = new BackupTestRig();
        var syncFolder = rig.P("sync");
        var now = DateTimeOffset.Parse("2026-11-02T12:00:00Z");
        const string injectionPassphrase = "admin'; DROP TABLE apps; DROP TABLE sessions; --";

        var testKeyManager = new TestKeyManager(injectionPassphrase);

        var srcDb = rig.NewDb("source_injection.db", (@"c:\apps\test.exe", 1761984000, 1761987600));
        SetDevice(rig, srcDb, "dev-injection-001", "Security PC", "windows");
        rig.Exec(srcDb, "INSERT OR REPLACE INTO settings (key, value) VALUES ('sync.encrypted', 'true');");

        // Export with malicious payload as passphrase
        var snapshotPath = SyncWriter.Write(srcDb, syncFolder, now, "1.2.0", testKeyManager);
        File.Exists(snapshotPath).Should().BeTrue();

        // Destination import
        var dstDb = rig.NewDb("dest_injection.db");
        SetDevice(rig, dstDb, "dev-dest-001", "Dest PC", "windows");

        var result = SyncReader.Import(snapshotPath, dstDb, TimeZoneInfo.Utc, testKeyManager);
        result.Status.Should().Be(SyncStatus.Success, "Sanitized passphrase literal must protect against SQL injection");
        result.SessionsAdded.Should().Be(1);

        // Verify tables still exist and data is intact
        rig.Scalar<long>(dstDb, "SELECT COUNT(*) FROM apps WHERE device_id = 'dev-injection-001'").Should().Be(1);
        rig.Scalar<long>(dstDb, "SELECT COUNT(*) FROM sessions WHERE app_id IN (SELECT id FROM apps WHERE device_id = 'dev-injection-001')").Should().Be(1);
    }

    [Fact]
    public void Challenge7_SyncWriter_ThrowsWhenSyncEncrypted_ButNoPassphraseConfigured()
    {
        using var rig = new BackupTestRig();
        var syncFolder = rig.P("sync");
        var now = DateTimeOffset.Parse("2026-11-02T12:00:00Z");

        var srcDb = rig.NewDb("source_nopass.db", (@"c:\apps\app.exe", 1761984000, 1761987600));
        SetDevice(rig, srcDb, "dev-nopass-001", "NoPass PC", "windows");
        rig.Exec(srcDb, "INSERT OR REPLACE INTO settings (key, value) VALUES ('sync.encrypted', 'true');");

        var emptyKeyManager = new TestKeyManager(syncPassphrase: null);

        var act = () => SyncWriter.Write(srcDb, syncFolder, now, "1.2.0", emptyKeyManager);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no passphrase is configured*");
    }

    [Fact]
    public void Challenge8_SyncReader_NewerSchemaVersion_ReturnsNewerFormat()
    {
        using var rig = new BackupTestRig();
        var syncFolder = rig.P("sync");
        var now = DateTimeOffset.Parse("2026-11-02T12:00:00Z");
        const string passphrase = "test-schema-version-check";

        var testKeyManager = new TestKeyManager(passphrase);

        var srcDb = rig.NewDb("source_v99.db", (@"c:\apps\app.exe", 1761984000, 1761987600));
        SetDevice(rig, srcDb, "dev-v99-001", "V99 PC", "windows");
        rig.Exec(srcDb, "INSERT OR REPLACE INTO settings (key, value) VALUES ('sync.encrypted', 'true');");

        var snapshotPath = SyncWriter.Write(srcDb, syncFolder, now, "1.2.0", testKeyManager);

        // Tamper manifest schemaVersion to 99
        var newerZip = rig.P("newer_v99.tssync");
        File.Copy(snapshotPath, newerZip);
        using (var zip = ZipFile.Open(newerZip, ZipArchiveMode.Update))
        {
            var mEntry = zip.GetEntry(SyncManifest.EntryName)!;
            SyncManifest manifest;
            using (var reader = new StreamReader(mEntry.Open(), Encoding.UTF8))
            {
                manifest = SyncManifest.Parse(reader.ReadToEnd());
            }
            mEntry.Delete();

            var newEntry = zip.CreateEntry(SyncManifest.EntryName);
            using var writer = new StreamWriter(newEntry.Open(), new UTF8Encoding(false));
            writer.Write((manifest with { SchemaVersion = 99 }).ToJson());
        }

        var dstDb = rig.NewDb("dest_v99.db");
        SetDevice(rig, dstDb, "dev-dest-001", "Dest PC", "windows");

        var result = SyncReader.Import(newerZip, dstDb, TimeZoneInfo.Utc, testKeyManager);
        result.Status.Should().Be(SyncStatus.NewerFormat);
        result.Message.Should().Contain("Update required");
    }
}

