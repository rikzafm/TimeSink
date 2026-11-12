using System.Runtime.Versioning;
using System.Security.Cryptography;
using FluentAssertions;
using TimeSink.Core.Data;
using TimeSink.Core.Security;
using TimeSink.Core.Settings;
using TimeSink.Core.Sync;
using TimeSink.Tests.Backup;
using Xunit;

namespace TimeSink.Tests.Sync;

/// <summary>Ticket 06: encryption errors skip only the failing file and recover without a restart or "Sync now".</summary>
[SupportedOSPlatform("windows")]
public sealed class SyncEncryptionRecoveryTests
{
    private sealed class TestKeyManager(string? passphrase = null) : IKeyManager
    {
        private string? _passphrase = passphrase;
        public byte[] GetOrCreateDatabaseKey() => RandomNumberGenerator.GetBytes(32);
        public void DeleteDatabaseKey() { }
        public string? GetSyncPassphrase() => _passphrase;
        public void SetSyncPassphrase(string p) => _passphrase = p;
        public bool HasSyncPassphrase() => !string.IsNullOrEmpty(_passphrase);
        public void ClearSyncPassphrase() => _passphrase = null;
    }

    private static void SetDevice(BackupTestRig rig, string dbPath, string id, string name, string platform) =>
        rig.Exec(dbPath, "PRAGMA foreign_keys = OFF; " +
                         $"INSERT OR REPLACE INTO settings (key, value) VALUES ('device.id', '{id}'), ('device.name', '{name}'); " +
                         $"UPDATE devices SET device_id = '{id}', name = '{name}', platform = '{platform}' WHERE is_local = 1; " +
                         $"UPDATE apps SET device_id = '{id}'; PRAGMA foreign_keys = ON;");

    private static string MakeLocal(BackupTestRig rig, string folder, bool encrypted)
    {
        var db = rig.NewDb("local.db", (@"c:\windows\notepad.exe", 1761980000, 1761983600));
        SetDevice(rig, db, "dev-win-001", "Local PC", "windows");
        rig.Exec(db, "INSERT OR REPLACE INTO settings (key, value) VALUES ('sync.enabled', 'true'), " +
                     $"('sync.folder', '{folder.Replace("'", "''")}'), ('sync.encrypted', '{(encrypted ? "true" : "false")}');");
        return db;
    }

    private static string MakeRemote(BackupTestRig rig, string folder, DateTimeOffset at, string? passphrase)
    {
        var db = rig.NewDb("remote.db", (@"/Applications/Safari.app", 1761984000, 1761987600));
        SetDevice(rig, db, "dev-mac-001", "Remote Mac", "macos");
        if (passphrase is not null)
            rig.Exec(db, "INSERT OR REPLACE INTO settings (key, value) VALUES ('sync.encrypted', 'true');");
        SyncWriter.Write(db, folder, at, "1.2.0", new TestKeyManager(passphrase));
        return db;
    }

    private static long ImportFingerprints(BackupTestRig rig, string db) =>
        rig.Scalar<long>(db, "SELECT COUNT(*) FROM settings WHERE key LIKE 'sync.imported.%'");

    [Fact]
    public void Wrong_passphrase_still_exports_and_fixing_it_imports_without_sync_now()
    {
        using var rig = new BackupTestRig();
        var folder = rig.P("sync");
        Directory.CreateDirectory(folder);
        var t0 = DateTimeOffset.Parse("2026-11-02T12:00:00Z");
        MakeRemote(rig, folder, t0, "right");
        var local = MakeLocal(rig, folder, encrypted: true);
        var keys = new TestKeyManager("wrong");
        var now = t0.AddMinutes(1);
        var log = new List<string>();
        using var host = new SyncHost(local, TimeZoneInfo.Utc, () => now, log.Add, keyManager: keys);

        host.Check();

        AppSettings.ForFile(local).Get(Setting.SyncState).Should().Be("encryption_key_wrong");
        File.Exists(Path.Combine(folder, "dev-win-001.tssync")).Should().BeTrue("export must not wait on the import error");
        AppSettings.ForFile(local).Get(Setting.SyncLastError).Should().NotBeNullOrEmpty();
        ImportFingerprints(rig, local).Should().Be(0, "a file that failed decryption was not imported, so it must not be fingerprinted");

        // Idle checks neither re-fail loudly nor change state.
        for (var i = 0; i < 4; i++) { now = now.AddSeconds(15); host.Check(); }
        log.Count(l => l.Contains("passphrase failed decryption")).Should().Be(1);
        AppSettings.ForFile(local).Get(Setting.SyncState).Should().Be("encryption_key_wrong");

        // Fix the passphrase: plain Check(), no SyncNow, no restart.
        keys.SetSyncPassphrase("right");
        now = now.AddSeconds(15);
        host.Check();

        AppSettings.ForFile(local).Get(Setting.SyncState).Should().Be("synced", string.Join(" | ", log));
        AppSettings.ForFile(local).Get(Setting.SyncLastError).Should().BeNullOrEmpty();
        rig.Scalar<long>(local, "SELECT COUNT(*) FROM apps WHERE device_id = 'dev-mac-001'").Should().Be(1);
        ImportFingerprints(rig, local).Should().Be(1);
    }

    [Fact]
    public void Replacing_the_remote_file_retries_a_skipped_file()
    {
        using var rig = new BackupTestRig();
        var folder = rig.P("sync");
        Directory.CreateDirectory(folder);
        var t0 = DateTimeOffset.Parse("2026-11-02T12:00:00Z");
        var remote = MakeRemote(rig, folder, t0, passphrase: null); // plain file
        var local = MakeLocal(rig, folder, encrypted: true);
        var now = t0.AddMinutes(1);
        using var host = new SyncHost(local, TimeZoneInfo.Utc, () => now, keyManager: new TestKeyManager("secret"));

        host.Check();
        AppSettings.ForFile(local).Get(Setting.SyncState).Should().Be("remote_not_encrypted");
        File.Exists(Path.Combine(folder, "dev-win-001.tssync")).Should().BeTrue();

        rig.Exec(remote, "INSERT OR REPLACE INTO settings (key, value) VALUES ('sync.encrypted', 'true');");
        now = now.AddMinutes(1);
        SyncWriter.Write(remote, folder, now, "1.2.0", new TestKeyManager("secret"));
        now = now.AddSeconds(15);
        host.Check();

        AppSettings.ForFile(local).Get(Setting.SyncState).Should().Be("synced");
    }

    [Fact]
    public void Encryption_error_on_one_file_does_not_stop_other_files()
    {
        using var rig = new BackupTestRig();
        var folder = rig.P("sync");
        Directory.CreateDirectory(folder);
        var t0 = DateTimeOffset.Parse("2026-11-02T12:00:00Z");
        MakeRemote(rig, folder, t0, "right");
        File.Copy(Path.Combine(folder, "dev-mac-001.tssync"), Path.Combine(folder, "aaa-other.tssync"));
        var local = MakeLocal(rig, folder, encrypted: true);
        var log = new List<string>();
        using var host = new SyncHost(local, TimeZoneInfo.Utc, () => t0.AddMinutes(1), log.Add, keyManager: new TestKeyManager("wrong"));

        host.Check();

        log.Count(l => l.Contains("passphrase failed decryption")).Should().Be(2, "both files were examined");
    }

    [Fact]
    public void Failed_export_does_not_skip_the_import_pass()
    {
        using var rig = new BackupTestRig();
        var folder = rig.P("sync");
        Directory.CreateDirectory(folder);
        var t0 = DateTimeOffset.Parse("2026-11-02T12:00:00Z");
        MakeRemote(rig, folder, t0, passphrase: null);
        var local = MakeLocal(rig, folder, encrypted: false);
        Directory.CreateDirectory(Path.Combine(folder, "dev-win-001.tssync")); // a directory where our file belongs: export throws
        using var host = new SyncHost(local, TimeZoneInfo.Utc, () => t0.AddMinutes(1));

        host.Check();

        rig.Scalar<long>(local, "SELECT COUNT(*) FROM apps WHERE device_id = 'dev-mac-001'").Should().Be(1);
        AppSettings.ForFile(local).Get(Setting.SyncState).Should().Be("export_failed");
        AppSettings.ForFile(local).Get(Setting.SyncLastError).Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Non_success_statuses_are_logged_once_per_change()
    {
        using var rig = new BackupTestRig();
        var folder = rig.P("sync");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "dev-bad-001.tssync"), "not a zip");
        var local = MakeLocal(rig, folder, encrypted: false);
        var now = DateTimeOffset.Parse("2026-11-02T12:00:00Z");
        var log = new List<string>();
        using var host = new SyncHost(local, TimeZoneInfo.Utc, () => now, log.Add);

        for (var i = 0; i < 5; i++) { now = now.AddSeconds(15); host.Check(); }

        log.Count(l => l.Contains("dev-bad-001.tssync") && l.Contains("BadHash")).Should().Be(1);
    }

    [Fact]
    public void Documents_current_mismatch_pc_ignores_a_stored_passphrase_when_sync_encrypted_is_off()
    {
        // Bug 6 (separate ticket): the Mac encrypts when `sync.encrypted` is true OR a passphrase exists in the keychain;
        // the PC encrypts only when `sync.encrypted` is true. This pins the PC half so the follow-up ticket flips it.
        using var rig = new BackupTestRig();
        var folder = rig.P("sync");
        Directory.CreateDirectory(folder);
        var local = MakeLocal(rig, folder, encrypted: false);

        var path = SyncWriter.Write(local, folder, DateTimeOffset.Parse("2026-11-02T12:00:00Z"), "1.2.0", new TestKeyManager("has-passphrase"));

        using var zip = System.IO.Compression.ZipFile.OpenRead(path);
        using var s = new StreamReader(zip.GetEntry(SyncManifest.EntryName)!.Open());
        SyncManifest.Parse(s.ReadToEnd()).Encrypted.Should().BeFalse("current PC rule; the Mac would write true here");
    }
}
