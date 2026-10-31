using FluentAssertions;
using TimeSink.Core.Data;
using TimeSink.Core.Settings;
using TimeSink.Core.Sync;
using TimeSink.Tests.Backup;
using Xunit;

namespace TimeSink.Tests.Sync;

public sealed class SyncHostTests
{
    [Fact]
    public void Disabled_sync_or_missing_folder_does_nothing()
    {
        using var rig = new BackupTestRig();
        var dbPath = rig.NewDb("test.db", (@"c:\test.exe", 1761984000, 1761987600));

        var host = new SyncHost(dbPath);
        host.Check();

        // Sync settings were disabled by default, no files written
        AppSettings.ForFile(dbPath).Get(Setting.SyncEnabled).Should().BeFalse();
        AppSettings.ForFile(dbPath).Get(Setting.SyncLastExportUtc).Should().Be(0);
    }

    [Fact]
    public void Enabled_sync_writes_snapshot_and_updates_last_export_utc()
    {
        using var rig = new BackupTestRig();
        var syncFolder = rig.P("sync");
        Directory.CreateDirectory(syncFolder);

        var dbPath = rig.NewDb("test.db", (@"c:\test.exe", 1761984000, 1761987600));
        rig.Exec(dbPath, "PRAGMA foreign_keys = OFF; " +
                         "INSERT OR REPLACE INTO settings (key, value) VALUES " +
                         "('device.id', 'dev-win-001'), ('device.name', 'Windows PC'), " +
                         "('sync.enabled', 'true'), ('sync.folder', '" + syncFolder.Replace("'", "''") + "'); " +
                         "UPDATE devices SET device_id = 'dev-win-001', name = 'Windows PC', platform = 'windows' WHERE is_local = 1; " +
                         "UPDATE apps SET device_id = 'dev-win-001'; " +
                         "PRAGMA foreign_keys = ON;");

        var now = DateTimeOffset.Parse("2026-11-01T12:00:00Z");
        using var host = new SyncHost(dbPath, TimeZoneInfo.Utc, () => now);

        host.Check();

        File.Exists(Path.Combine(syncFolder, "dev-win-001.tssync")).Should().BeTrue();
        AppSettings.ForFile(dbPath).Get(Setting.SyncLastExportUtc).Should().Be(now.ToUnixTimeSeconds());
    }

    [Fact]
    public void SyncNow_forces_immediate_export()
    {
        using var rig = new BackupTestRig();
        var syncFolder = rig.P("sync");
        Directory.CreateDirectory(syncFolder);

        var dbPath = rig.NewDb("test.db", (@"c:\test.exe", 1761984000, 1761987600));
        rig.Exec(dbPath, "PRAGMA foreign_keys = OFF; " +
                         "INSERT OR REPLACE INTO settings (key, value) VALUES " +
                         "('device.id', 'dev-win-001'), ('device.name', 'Windows PC'), " +
                         "('sync.enabled', 'true'), ('sync.folder', '" + syncFolder.Replace("'", "''") + "'); " +
                         "UPDATE devices SET device_id = 'dev-win-001', name = 'Windows PC', platform = 'windows' WHERE is_local = 1; " +
                         "UPDATE apps SET device_id = 'dev-win-001'; " +
                         "PRAGMA foreign_keys = ON;");

        var time = DateTimeOffset.Parse("2026-11-01T12:00:00Z");
        using var host = new SyncHost(dbPath, TimeZoneInfo.Utc, () => time);

        host.Check();
        var firstExport = AppSettings.ForFile(dbPath).Get(Setting.SyncLastExportUtc);

        // Advance 10 seconds (well within 5 minute interval)
        time = time.AddSeconds(10);
        host.SyncNow();

        AppSettings.ForFile(dbPath).Get(Setting.SyncLastExportUtc).Should().Be(time.ToUnixTimeSeconds());
    }

    [Fact]
    public void Two_installs_exchange_data_through_sync_folder()
    {
        using var rig = new BackupTestRig();
        var syncFolder = rig.P("shared_cloud");
        Directory.CreateDirectory(syncFolder);

        // Machine 1 (Mac)
        var macDb = rig.NewDb("mac.db", (@"/Applications/Safari.app", 1761984000, 1761987600));
        rig.Exec(macDb, "PRAGMA foreign_keys = OFF; " +
                        "INSERT OR REPLACE INTO settings (key, value) VALUES " +
                        "('device.id', 'dev-mac-001'), ('device.name', 'MacBook'), " +
                        "('sync.enabled', 'true'), ('sync.folder', '" + syncFolder.Replace("'", "''") + "'); " +
                        "UPDATE devices SET device_id = 'dev-mac-001', name = 'MacBook', platform = 'macos' WHERE is_local = 1; " +
                        "UPDATE apps SET device_id = 'dev-mac-001', bundle_id = 'com.apple.Safari'; " +
                        "PRAGMA foreign_keys = ON;");

        // Machine 2 (Windows)
        var winDb = rig.NewDb("win.db", (@"c:\windows\notepad.exe", 1761980000, 1761983600));
        rig.Exec(winDb, "PRAGMA foreign_keys = OFF; " +
                        "INSERT OR REPLACE INTO settings (key, value) VALUES " +
                        "('device.id', 'dev-win-001'), ('device.name', 'Desktop PC'), " +
                        "('sync.enabled', 'true'), ('sync.folder', '" + syncFolder.Replace("'", "''") + "'); " +
                        "UPDATE devices SET device_id = 'dev-win-001', name = 'Desktop PC', platform = 'windows' WHERE is_local = 1; " +
                        "UPDATE apps SET device_id = 'dev-win-001'; " +
                        "PRAGMA foreign_keys = ON;");

        var now = DateTimeOffset.Parse("2026-11-01T12:00:00Z");

        using var macHost = new SyncHost(macDb, TimeZoneInfo.Utc, () => now);
        using var winHost = new SyncHost(winDb, TimeZoneInfo.Utc, () => now);

        // Mac exports
        macHost.Check();
        File.Exists(Path.Combine(syncFolder, "dev-mac-001.tssync")).Should().BeTrue();

        // Windows exports and imports Mac
        winHost.Check();
        File.Exists(Path.Combine(syncFolder, "dev-win-001.tssync")).Should().BeTrue();

        // Mac imports Windows
        macHost.Check();

        // Verify Windows DB has Mac's app and session
        rig.Scalar<long>(winDb, "SELECT COUNT(*) FROM apps WHERE device_id = 'dev-mac-001'").Should().Be(1);
        rig.Scalar<long>(winDb, "SELECT COUNT(*) FROM sessions WHERE app_id IN (SELECT id FROM apps WHERE device_id = 'dev-mac-001')").Should().Be(1);
        AppSettings.ForFile(winDb).Get(Setting.SyncState).Should().Be("synced");

        // Verify Mac DB has Windows app and session
        rig.Scalar<long>(macDb, "SELECT COUNT(*) FROM apps WHERE device_id = 'dev-win-001'").Should().Be(1);
        rig.Scalar<long>(macDb, "SELECT COUNT(*) FROM sessions WHERE app_id IN (SELECT id FROM apps WHERE device_id = 'dev-win-001')").Should().Be(1);
        AppSettings.ForFile(macDb).Get(Setting.SyncState).Should().Be("synced");
    }

    [Fact]
    public void Discovery_window_transitions_to_no_other_device_after_10_minutes()
    {
        using var rig = new BackupTestRig();
        var syncFolder = rig.P("sync");
        Directory.CreateDirectory(syncFolder);

        var dbPath = rig.NewDb("test.db");
        rig.Exec(dbPath, "PRAGMA foreign_keys = OFF; " +
                         "INSERT OR REPLACE INTO settings (key, value) VALUES " +
                         "('device.id', 'dev-win-001'), ('device.name', 'Windows PC'), " +
                         "('sync.enabled', 'true'), ('sync.folder', '" + syncFolder.Replace("'", "''") + "'); " +
                         "UPDATE devices SET device_id = 'dev-win-001', name = 'Windows PC', platform = 'windows' WHERE is_local = 1; " +
                         "PRAGMA foreign_keys = ON;");

        var startTime = DateTimeOffset.Parse("2026-11-01T12:00:00Z");
        var currentTime = startTime;

        using var host = new SyncHost(dbPath, TimeZoneInfo.Utc, () => currentTime);

        // Initially waiting for device
        host.Check();
        AppSettings.ForFile(dbPath).Get(Setting.SyncState).Should().Be("waiting_for_device");

        // Advance 5 minutes - still waiting
        currentTime = startTime.AddMinutes(5);
        host.Check();
        AppSettings.ForFile(dbPath).Get(Setting.SyncState).Should().Be("waiting_for_device");

        // Advance 10 minutes - no other device found
        currentTime = startTime.AddMinutes(10);
        host.Check();
        AppSettings.ForFile(dbPath).Get(Setting.SyncState).Should().Be("no_other_device");
    }

    [Fact]
    public void Clean_snapshot_is_written_on_dispose()
    {
        using var rig = new BackupTestRig();
        var syncFolder = rig.P("sync");
        Directory.CreateDirectory(syncFolder);

        var dbPath = rig.NewDb("test.db", (@"c:\test.exe", 1761984000, 1761987600));
        rig.Exec(dbPath, "PRAGMA foreign_keys = OFF; " +
                         "INSERT OR REPLACE INTO settings (key, value) VALUES " +
                         "('device.id', 'dev-win-001'), ('device.name', 'Windows PC'), " +
                         "('sync.enabled', 'true'), ('sync.folder', '" + syncFolder.Replace("'", "''") + "'); " +
                         "UPDATE devices SET device_id = 'dev-win-001', name = 'Windows PC', platform = 'windows' WHERE is_local = 1; " +
                         "UPDATE apps SET device_id = 'dev-win-001'; " +
                         "PRAGMA foreign_keys = ON;");

        var now = DateTimeOffset.Parse("2026-11-01T12:00:00Z");
        var host = new SyncHost(dbPath, TimeZoneInfo.Utc, () => now);

        // Dispose without calling Check
        host.Dispose();

        File.Exists(Path.Combine(syncFolder, "dev-win-001.tssync")).Should().BeTrue();
    }

    [Fact]
    public void Unreadable_or_missing_folder_does_not_crash_and_backs_off()
    {
        using var rig = new BackupTestRig();
        var nonExistentFolder = rig.P("missing_folder_does_not_exist");

        var dbPath = rig.NewDb("test.db");
        rig.Exec(dbPath, "PRAGMA foreign_keys = OFF; " +
                         "INSERT OR REPLACE INTO settings (key, value) VALUES " +
                         "('device.id', 'dev-win-001'), ('device.name', 'Windows PC'), " +
                         "('sync.enabled', 'true'), ('sync.folder', '" + nonExistentFolder.Replace("'", "''") + "'); " +
                         "PRAGMA foreign_keys = ON;");

        var loggedErrors = new List<string>();
        using var host = new SyncHost(dbPath, TimeZoneInfo.Utc, null, loggedErrors.Add);

        // Calling check on missing folder must not throw
        var act = () => host.Check();
        act.Should().NotThrow();
    }
}
