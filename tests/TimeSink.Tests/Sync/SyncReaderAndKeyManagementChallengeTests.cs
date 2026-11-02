using System.Collections.Concurrent;
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
public sealed class SyncReaderAndKeyManagementChallengeTests
{
    private sealed class MockKeyManager : IKeyManager
    {
        private byte[] _dbKey = RandomNumberGenerator.GetBytes(32);
        private string? _syncPassphrase;

        public MockKeyManager(string? syncPassphrase = null)
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

    // =========================================================================
    // CHALLENGE 1: Wrong Passphrase Decryption
    // =========================================================================

    [Fact]
    public void WrongPassphrase_ReturnsEncryptionKeyWrong_AndLeavesLocalDatabaseUnaffected()
    {
        using var rig = new BackupTestRig();
        var syncFolder = rig.P("sync");
        var now = DateTimeOffset.Parse("2026-11-02T12:00:00Z");
        const string correctPassphrase = "correct-sync-passphrase-alpha-123!";
        const string wrongPassphrase = "wrong-sync-passphrase-beta-999!";

        // 1. Source database setup
        var srcDb = rig.NewDb("source.db",
            (@"/Applications/Slack.app", 1761980000, 1761983600),
            (@"/Applications/Figma.app", 1761984000, 1761987600),
            (@"/Applications/Terminal.app", 1761988000, 1761991600));
        SetDevice(rig, srcDb, "dev-remote-001", "Remote MacBook", "macos");
        rig.Exec(srcDb, "INSERT OR REPLACE INTO settings (key, value) VALUES ('sync.encrypted', 'true');");

        // 2. Export encrypted snapshot
        var sourceKeyManager = new MockKeyManager(correctPassphrase);
        var snapshotPath = SyncWriter.Write(srcDb, syncFolder, now, "1.2.0", sourceKeyManager);
        File.Exists(snapshotPath).Should().BeTrue();

        // 3. Destination database setup with existing data
        var dstDb = rig.NewDb("dest.db",
            (@"c:\windows\notepad.exe", 1761900000, 1761903600),
            (@"c:\program files\calc.exe", 1761904000, 1761907600));
        SetDevice(rig, dstDb, "dev-local-001", "Local Desktop", "windows");

        // Capture initial fingerprint and row counts
        var fingerprintBefore = rig.Fingerprint(dstDb);
        var appsCountBefore = rig.Scalar<long>(dstDb, "SELECT COUNT(*) FROM apps;");
        var sessionsCountBefore = rig.Scalar<long>(dstDb, "SELECT COUNT(*) FROM sessions;");
        var devicesCountBefore = rig.Scalar<long>(dstDb, "SELECT COUNT(*) FROM devices;");

        // 4. Attempt import with WRONG passphrase
        var wrongKeyManager = new MockKeyManager(wrongPassphrase);
        var result = SyncReader.Import(snapshotPath, dstDb, TimeZoneInfo.Utc, wrongKeyManager);

        // 5. Verify status code and message
        result.Status.Should().Be(SyncStatus.EncryptionKeyWrong, "Import with incorrect passphrase must return EncryptionKeyWrong");
        result.Message.Should().Contain("passphrase", "Error message should mention passphrase");
        result.AppsAdded.Should().Be(0);
        result.SessionsAdded.Should().Be(0);
        result.SessionsUpdated.Should().Be(0);
        result.SessionsSkipped.Should().Be(0);

        // 6. Verify local database was completely unaffected
        var fingerprintAfter = rig.Fingerprint(dstDb);
        fingerprintAfter.Should().Be(fingerprintBefore, "Local database fingerprint must be identical after failed decryption");

        rig.Scalar<long>(dstDb, "SELECT COUNT(*) FROM apps;").Should().Be(appsCountBefore);
        rig.Scalar<long>(dstDb, "SELECT COUNT(*) FROM sessions;").Should().Be(sessionsCountBefore);
        rig.Scalar<long>(dstDb, "SELECT COUNT(*) FROM devices;").Should().Be(devicesCountBefore);

        // Remote device record must NOT be inserted
        rig.Scalar<long>(dstDb, "SELECT COUNT(*) FROM devices WHERE device_id = 'dev-remote-001';").Should().Be(0);

        // 7. Verify destination database remains fully functional and uncorrupted
        rig.Exec(dstDb, "INSERT INTO apps (exe_path, display_name, added_utc, device_id) VALUES ('c:\\test.exe', 'Test', 1761910000, 'dev-local-001');");
        rig.Scalar<long>(dstDb, "SELECT COUNT(*) FROM apps WHERE exe_path = 'c:\\test.exe';").Should().Be(1);
    }

    // =========================================================================
    // CHALLENGE 2: Missing Passphrase
    // =========================================================================

    [Fact]
    public void MissingPassphrase_ReturnsEncryptionKeyMissing_AndLeavesLocalDatabaseUnaffected()
    {
        using var rig = new BackupTestRig();
        var syncFolder = rig.P("sync");
        var now = DateTimeOffset.Parse("2026-11-02T12:00:00Z");
        const string correctPassphrase = "sync-secret-key-configured-on-source";

        // 1. Source database setup and encrypted export
        var srcDb = rig.NewDb("source.db", (@"/Applications/Safari.app", 1761984000, 1761987600));
        SetDevice(rig, srcDb, "dev-remote-002", "Remote Mac", "macos");
        rig.Exec(srcDb, "INSERT OR REPLACE INTO settings (key, value) VALUES ('sync.encrypted', 'true');");

        var sourceKeyManager = new MockKeyManager(correctPassphrase);
        var snapshotPath = SyncWriter.Write(srcDb, syncFolder, now, "1.2.0", sourceKeyManager);

        // 2. Destination database setup
        var dstDb = rig.NewDb("dest.db", (@"c:\windows\explorer.exe", 1761900000, 1761903600));
        SetDevice(rig, dstDb, "dev-local-002", "Local PC", "windows");

        var fingerprintBefore = rig.Fingerprint(dstDb);
        var appsCountBefore = rig.Scalar<long>(dstDb, "SELECT COUNT(*) FROM apps;");
        var sessionsCountBefore = rig.Scalar<long>(dstDb, "SELECT COUNT(*) FROM sessions;");

        // 3. KeyManager on destination has NO passphrase configured (null)
        var missingKeyManager = new MockKeyManager(syncPassphrase: null);
        missingKeyManager.HasSyncPassphrase().Should().BeFalse();

        // 4. Attempt import
        var result = SyncReader.Import(snapshotPath, dstDb, TimeZoneInfo.Utc, missingKeyManager);

        // 5. Verify status and database integrity
        result.Status.Should().Be(SyncStatus.EncryptionKeyMissing, "Encrypted archive without configured passphrase must return EncryptionKeyMissing");
        result.AppsAdded.Should().Be(0);
        result.SessionsAdded.Should().Be(0);

        var fingerprintAfter = rig.Fingerprint(dstDb);
        fingerprintAfter.Should().Be(fingerprintBefore, "Destination database must not be modified when passphrase is missing");
        rig.Scalar<long>(dstDb, "SELECT COUNT(*) FROM apps;").Should().Be(appsCountBefore);
        rig.Scalar<long>(dstDb, "SELECT COUNT(*) FROM sessions;").Should().Be(sessionsCountBefore);
        rig.Scalar<long>(dstDb, "SELECT COUNT(*) FROM devices WHERE device_id = 'dev-remote-002';").Should().Be(0);
    }

    [Fact]
    public void EmptyStringPassphrase_IsTreatedAsMissingPassphrase()
    {
        using var rig = new BackupTestRig();
        var syncFolder = rig.P("sync");
        var now = DateTimeOffset.Parse("2026-11-02T12:00:00Z");

        var srcDb = rig.NewDb("source.db", (@"/Applications/Safari.app", 1761984000, 1761987600));
        SetDevice(rig, srcDb, "dev-remote-003", "Remote Mac", "macos");
        rig.Exec(srcDb, "INSERT OR REPLACE INTO settings (key, value) VALUES ('sync.encrypted', 'true');");

        var sourceKeyManager = new MockKeyManager("valid-passphrase");
        var snapshotPath = SyncWriter.Write(srcDb, syncFolder, now, "1.2.0", sourceKeyManager);

        var dstDb = rig.NewDb("dest.db");
        SetDevice(rig, dstDb, "dev-local-003", "Local PC", "windows");

        // Empty string passphrase
        var emptyKeyManager = new MockKeyManager(syncPassphrase: "");
        var result = SyncReader.Import(snapshotPath, dstDb, TimeZoneInfo.Utc, emptyKeyManager);

        result.Status.Should().Be(SyncStatus.EncryptionKeyMissing);
    }

    // =========================================================================
    // CHALLENGE 3: Corrupted Ciphertext & Tampering
    // =========================================================================

    [Fact]
    public void CorruptedCiphertext_WithoutManifestHashUpdate_ReturnsBadHash_AndLeavesDbUnaffected()
    {
        using var rig = new BackupTestRig();
        var syncFolder = rig.P("sync");
        var now = DateTimeOffset.Parse("2026-11-02T12:00:00Z");
        const string passphrase = "tamper-test-passphrase";

        var srcDb = rig.NewDb("source.db", (@"/Applications/Xcode.app", 1761984000, 1761987600));
        SetDevice(rig, srcDb, "dev-mac-tamper1", "Mac 1", "macos");
        rig.Exec(srcDb, "INSERT OR REPLACE INTO settings (key, value) VALUES ('sync.encrypted', 'true');");

        var keyManager = new MockKeyManager(passphrase);
        var snapshotPath = SyncWriter.Write(srcDb, syncFolder, now, "1.2.0", keyManager);

        // Tamper with bytes inside snapshot.db in the zip archive
        var tamperedArchive = rig.P("tampered_hash_mismatch.tssync");
        File.Copy(snapshotPath, tamperedArchive);

        using (var zip = ZipFile.Open(tamperedArchive, ZipArchiveMode.Update))
        {
            var dbEntry = zip.GetEntry(SyncManifest.DbEntryName)!;
            byte[] bytes;
            using (var ms = new MemoryStream())
            {
                using (var s = dbEntry.Open()) s.CopyTo(ms);
                bytes = ms.ToArray();
            }
            dbEntry.Delete();

            // Corrupt middle bytes
            for (int i = 500; i < 520; i++) bytes[i] ^= 0xAA;

            var newEntry = zip.CreateEntry(SyncManifest.DbEntryName, CompressionLevel.Optimal);
            using var sOut = newEntry.Open();
            sOut.Write(bytes);
        }

        var dstDb = rig.NewDb("dest.db", (@"c:\app.exe", 1761900000, 1761903600));
        SetDevice(rig, dstDb, "dev-local-tamper1", "Local PC", "windows");
        var fingerprintBefore = rig.Fingerprint(dstDb);

        var result = SyncReader.Import(tamperedArchive, dstDb, TimeZoneInfo.Utc, keyManager);

        result.Status.Should().Be(SyncStatus.BadHash, "Mismatched hash on tampered ciphertext must return BadHash");
        rig.Fingerprint(dstDb).Should().Be(fingerprintBefore);
    }

    [Fact]
    public void CorruptedCiphertext_WithMatchingManifestHash_ReturnsEncryptionKeyWrong_WithoutUnhandledException()
    {
        using var rig = new BackupTestRig();
        var syncFolder = rig.P("sync");
        var now = DateTimeOffset.Parse("2026-11-02T12:00:00Z");
        const string passphrase = "tamper-matching-hash-passphrase";

        var srcDb = rig.NewDb("source.db", (@"/Applications/Xcode.app", 1761984000, 1761987600));
        SetDevice(rig, srcDb, "dev-mac-tamper2", "Mac 2", "macos");
        rig.Exec(srcDb, "INSERT OR REPLACE INTO settings (key, value) VALUES ('sync.encrypted', 'true');");

        var keyManager = new MockKeyManager(passphrase);
        var snapshotPath = SyncWriter.Write(srcDb, syncFolder, now, "1.2.0", keyManager);

        // Tamper with bytes inside snapshot.db AND update manifest.Hash to match the corrupted file!
        var tamperedArchive = rig.P("tampered_matching_hash.tssync");
        File.Copy(snapshotPath, tamperedArchive);

        using (var zip = ZipFile.Open(tamperedArchive, ZipArchiveMode.Update))
        {
            var dbEntry = zip.GetEntry(SyncManifest.DbEntryName)!;
            byte[] bytes;
            using (var ms = new MemoryStream())
            {
                using (var s = dbEntry.Open()) s.CopyTo(ms);
                bytes = ms.ToArray();
            }
            dbEntry.Delete();

            // Corrupt ciphertext pages (tamper with page 1 salt/header and page 2 data)
            for (int i = 32; i < 64; i++) bytes[i] ^= 0x55;
            for (int i = 4096; i < 4120; i++) bytes[i] ^= 0xFF;

            // Recompute SHA-256 over tampered bytes
            string newHash;
            using (var sha = SHA256.Create())
            {
                newHash = Convert.ToHexStringLower(sha.ComputeHash(bytes));
            }

            // Write corrupted snapshot.db back into zip
            var newDbEntry = zip.CreateEntry(SyncManifest.DbEntryName, CompressionLevel.Optimal);
            using (var sOut = newDbEntry.Open()) sOut.Write(bytes);

            // Update manifest.json with new matching hash
            var mEntry = zip.GetEntry(SyncManifest.EntryName)!;
            SyncManifest oldManifest;
            using (var r = new StreamReader(mEntry.Open(), Encoding.UTF8))
            {
                oldManifest = SyncManifest.Parse(r.ReadToEnd());
            }
            mEntry.Delete();

            var newManifest = oldManifest with { Hash = newHash };
            var newMEntry = zip.CreateEntry(SyncManifest.EntryName);
            using (var w = new StreamWriter(newMEntry.Open(), new UTF8Encoding(false)))
            {
                w.Write(newManifest.ToJson());
            }
        }

        var dstDb = rig.NewDb("dest.db", (@"c:\app.exe", 1761900000, 1761903600));
        SetDevice(rig, dstDb, "dev-local-tamper2", "Local PC", "windows");
        var fingerprintBefore = rig.Fingerprint(dstDb);

        // Act: Import should pass hash check, attempt SQLCipher decryption, fail integrity/open, and return EncryptionKeyWrong
        var result = SyncReader.Import(tamperedArchive, dstDb, TimeZoneInfo.Utc, keyManager);

        result.Status.Should().Be(SyncStatus.EncryptionKeyWrong, "Decryption of corrupted ciphertext must return EncryptionKeyWrong");
        rig.Fingerprint(dstDb).Should().Be(fingerprintBefore, "Local database must remain untouched after decryption failure");
    }

    [Fact]
    public void TruncatedSnapshot_WithMatchingManifestHash_FailsDecryptionGracefully()
    {
        using var rig = new BackupTestRig();
        var syncFolder = rig.P("sync");
        var now = DateTimeOffset.Parse("2026-11-02T12:00:00Z");
        const string passphrase = "truncated-test-passphrase";

        var srcDb = rig.NewDb("source.db", (@"/Applications/Xcode.app", 1761984000, 1761987600));
        SetDevice(rig, srcDb, "dev-mac-trunc", "Mac Trunc", "macos");
        rig.Exec(srcDb, "INSERT OR REPLACE INTO settings (key, value) VALUES ('sync.encrypted', 'true');");

        var keyManager = new MockKeyManager(passphrase);
        var snapshotPath = SyncWriter.Write(srcDb, syncFolder, now, "1.2.0", keyManager);

        var truncatedArchive = rig.P("truncated.tssync");
        File.Copy(snapshotPath, truncatedArchive);

        using (var zip = ZipFile.Open(truncatedArchive, ZipArchiveMode.Update))
        {
            var dbEntry = zip.GetEntry(SyncManifest.DbEntryName)!;
            dbEntry.Delete();

            // Truncate to just 64 random bytes (invalid database file)
            var bytes = RandomNumberGenerator.GetBytes(64);
            string newHash;
            using (var sha = SHA256.Create()) newHash = Convert.ToHexStringLower(sha.ComputeHash(bytes));

            var newDbEntry = zip.CreateEntry(SyncManifest.DbEntryName);
            using (var s = newDbEntry.Open()) s.Write(bytes);

            var mEntry = zip.GetEntry(SyncManifest.EntryName)!;
            SyncManifest oldManifest;
            using (var r = new StreamReader(mEntry.Open(), Encoding.UTF8)) oldManifest = SyncManifest.Parse(r.ReadToEnd());
            mEntry.Delete();

            var newMEntry = zip.CreateEntry(SyncManifest.EntryName);
            using (var w = new StreamWriter(newMEntry.Open(), new UTF8Encoding(false)))
                w.Write((oldManifest with { Hash = newHash }).ToJson());
        }

        var dstDb = rig.NewDb("dest.db");
        SetDevice(rig, dstDb, "dev-local-trunc", "Local PC", "windows");

        var result = SyncReader.Import(truncatedArchive, dstDb, TimeZoneInfo.Utc, keyManager);
        result.Status.Should().Be(SyncStatus.EncryptionKeyWrong);
    }

    // =========================================================================
    // CHALLENGE 4: DPAPI Storage Idempotency, Clearing, and Invalidation
    // =========================================================================

    [Fact]
    public void Dpapi_SetPassphrase_PersistsViaDpapi_AndRejectsEmptyOrWhitespace()
    {
        using var rig = new BackupTestRig();
        var keyFile = rig.P("test.dbkey");
        var syncKeyFile = rig.P("test.synckey");

        var manager = new DpapiKeyManager(keyFile, syncKeyFile);

        // Rejection of null, empty, whitespace
        var actNull = () => manager.SetSyncPassphrase(null!);
        actNull.Should().Throw<ArgumentException>();

        var actEmpty = () => manager.SetSyncPassphrase("");
        actEmpty.Should().Throw<ArgumentException>();

        var actWhitespace = () => manager.SetSyncPassphrase("   \t\r\n  ");
        actWhitespace.Should().Throw<ArgumentException>();

        // Set valid complex passphrase
        const string passphrase = "P@sswørd 🔑 世界 1234 'quotes' \"double\" \\slashes/";
        manager.SetSyncPassphrase(passphrase);

        manager.HasSyncPassphrase().Should().BeTrue();
        manager.GetSyncPassphrase().Should().Be(passphrase);

        // Verify DPAPI encryption on disk
        File.Exists(syncKeyFile).Should().BeTrue();
        var rawBytes = File.ReadAllBytes(syncKeyFile);
        var plainBytes = Encoding.UTF8.GetBytes(passphrase);

        rawBytes.Should().NotEqual(plainBytes, "Raw bytes on disk must be encrypted by DPAPI, not plaintext");

        var decryptedBytes = ProtectedData.Unprotect(rawBytes, null, DataProtectionScope.CurrentUser);
        Encoding.UTF8.GetString(decryptedBytes).Should().Be(passphrase);
    }

    [Fact]
    public void Dpapi_ClearPassphrase_DeletesFile_AndClearsCache()
    {
        using var rig = new BackupTestRig();
        var keyFile = rig.P("test.dbkey");
        var syncKeyFile = rig.P("test.synckey");

        var manager = new DpapiKeyManager(keyFile, syncKeyFile);

        manager.SetSyncPassphrase("temp-passphrase-to-clear");
        manager.HasSyncPassphrase().Should().BeTrue();
        File.Exists(syncKeyFile).Should().BeTrue();

        manager.ClearSyncPassphrase();

        manager.HasSyncPassphrase().Should().BeFalse();
        manager.GetSyncPassphrase().Should().BeNull();
        File.Exists(syncKeyFile).Should().BeFalse();

        // Clearing an already cleared passphrase should be a no-op without error
        var act = () => manager.ClearSyncPassphrase();
        act.Should().NotThrow();
        manager.HasSyncPassphrase().Should().BeFalse();
        manager.GetSyncPassphrase().Should().BeNull();
    }

    [Fact]
    public void Dpapi_MultiThreadedStress_IsIdempotentAndSafe()
    {
        using var rig = new BackupTestRig();
        var keyFile = rig.P("concurrent.dbkey");
        var syncKeyFile = rig.P("concurrent.synckey");

        const int threadCount = 32;
        const string targetPassphrase = "concurrent-stress-passphrase-32threads!";
        var results = new ConcurrentBag<string?>();
        var exceptions = new ConcurrentBag<Exception>();

        // Concurrent writers and readers
        Parallel.For(0, threadCount, new ParallelOptions { MaxDegreeOfParallelism = threadCount }, i =>
        {
            try
            {
                var mgr = new DpapiKeyManager(keyFile, syncKeyFile);
                if (i % 2 == 0)
                {
                    mgr.SetSyncPassphrase(targetPassphrase);
                }
                var retrieved = mgr.GetSyncPassphrase();
                results.Add(retrieved);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        exceptions.Should().BeEmpty("Concurrent access to DpapiKeyManager must be synchronized without exceptions");

        var finalManager = new DpapiKeyManager(keyFile, syncKeyFile);
        finalManager.HasSyncPassphrase().Should().BeTrue();
        finalManager.GetSyncPassphrase().Should().Be(targetPassphrase);
    }

    [Fact]
    public void Dpapi_CrossInstanceCacheInvalidation_WhenPassphraseUpdatedOrCleared()
    {
        using var rig = new BackupTestRig();
        var keyFile = rig.P("test.dbkey");
        var syncKeyFile = rig.P("test.synckey");

        var managerA = new DpapiKeyManager(keyFile, syncKeyFile);
        var managerB = new DpapiKeyManager(keyFile, syncKeyFile);

        // 1. Manager A sets passphrase 1
        managerA.SetSyncPassphrase("passphrase-one");

        // 2. Manager B reads passphrase 1
        managerB.GetSyncPassphrase().Should().Be("passphrase-one");

        // 3. Manager A updates passphrase to 2
        managerA.SetSyncPassphrase("passphrase-two");

        // 4. Cross-instance cache invalidation:
        // Manager B had cached "passphrase-one". Because Manager A updated the passphrase file on disk,
        // Manager B detects the updated file timestamp and returns the fresh passphrase "passphrase-two".
        var updatedValue = managerB.GetSyncPassphrase();
        updatedValue.Should().Be("passphrase-two", "Cross-instance cache invalidation must return the updated passphrase from disk");

        // Fresh instance reading from disk gets the updated passphrase
        var managerC = new DpapiKeyManager(keyFile, syncKeyFile);
        managerC.GetSyncPassphrase().Should().Be("passphrase-two", "A fresh instance or process re-reads the updated ciphertext from disk");

        // 5. Manager A clears passphrase
        managerA.ClearSyncPassphrase();
        File.Exists(syncKeyFile).Should().BeFalse();

        // Direct call to GetSyncPassphrase() detects file absence and invalidates Manager B's cache
        managerB.GetSyncPassphrase().Should().BeNull("Direct call to GetSyncPassphrase() after external file deletion must detect absence and return null");
        managerB.HasSyncPassphrase().Should().BeFalse();
        managerB.GetSyncPassphrase().Should().BeNull();

        // Fresh instance should see null
        var managerD = new DpapiKeyManager(keyFile, syncKeyFile);
        managerD.HasSyncPassphrase().Should().BeFalse();
        managerD.GetSyncPassphrase().Should().BeNull();
    }
}
