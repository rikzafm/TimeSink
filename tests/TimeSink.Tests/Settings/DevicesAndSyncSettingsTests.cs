using FluentAssertions;
using Microsoft.Data.Sqlite;
using TimeSink.Core.Data;
using TimeSink.Core.Devices;
using TimeSink.Core.Settings;
using TimeSink.Core.Sync;
using Xunit;

namespace TimeSink.Tests.Settings;

public sealed class DevicesAndSyncSettingsTests : IDisposable
{
    private readonly string _dir;
    private readonly string _dbPath;
    private const string WinDevId = "dev-win";
    private const string MacDevId = "dev-mac";

    public DevicesAndSyncSettingsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ts-devices-sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "timesink.db");

        using var db = Db.Open(_dbPath);
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();

        // 1. Devices: 1 local PC, 1 remote Mac
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO devices (device_id, name, platform, is_local, first_seen_utc, state) VALUES
                ('dev-win', 'My Windows PC', 'windows', 1, '2026-01-01T00:00:00Z', 'active'),
                ('dev-mac', 'My MacBook', 'macos', 0, '2026-01-01T00:00:00Z', 'active');
                """;
            cmd.ExecuteNonQuery();
        }
        db.SetSetting("device.id", WinDevId);
        db.SetSetting("device.name", "My Windows PC");

        // 2. Apps and sessions on both devices
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO apps (id, device_id, exe_path, bundle_id, display_name, added_utc, state) VALUES
                (1, 'dev-win', 'c:\\tools\\code.exe', NULL, 'VS Code', 1000, 'tracked'),
                (2, 'dev-mac', '/Applications/Visual Studio Code.app', 'com.microsoft.VSCode', 'VS Code', 1000, 'tracked');

                INSERT INTO daily_usage (app_id, day, seconds) VALUES
                (1, '2026-03-10', 10800),
                (2, '2026-03-10', 7200);

                INSERT INTO sessions (app_id, start_utc, end_utc, last_seen_utc, end_reason) VALUES
                (1, 1773136800, 1773147600, 1773147600, 'exit'),
                (2, 1773144000, 1773151200, 1773151200, 'exit');
                """;
            cmd.ExecuteNonQuery();
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void SettingsCatalogue_includes_DevicesAndSync_section_and_rows()
    {
        SettingsCatalogue.Sections.Should().Contain(SettingsCatalogue.DevicesAndSync);

        var rows = SettingsCatalogue.InSection(SettingsCatalogue.DevicesAndSync).ToList();
        rows.Should().Contain(r => r.Id == "devicesSync" && r.Kind == SettingKind.Button);
        rows.Should().Contain(r => r.Id == "deviceName" && r.Kind == SettingKind.Info);
        rows.Should().Contain(r => r.Id == "shortDeviceNames" && r.Kind == SettingKind.Switch);
    }

    [Fact]
    public void DeviceIdentity_RenameDevice_updates_name_correctly()
    {
        using var db = Db.Open(_dbPath);

        // Rename local device
        DeviceIdentity.RenameDevice(db, WinDevId, "Gaming Desktop");
        db.GetSetting("device.name").Should().Be("Gaming Desktop");

        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM devices WHERE device_id = $id";
            cmd.Parameters.AddWithValue("$id", WinDevId);
            cmd.ExecuteScalar().Should().Be("Gaming Desktop");
        }

        // Rename remote device
        DeviceIdentity.RenameDevice(db, MacDevId, "Work MacBook Pro");
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM devices WHERE device_id = $id";
            cmd.Parameters.AddWithValue("$id", MacDevId);
            cmd.ExecuteScalar().Should().Be("Work MacBook Pro");
        }

        // Local device name in settings stays untouched
        db.GetSetting("device.name").Should().Be("Gaming Desktop");
    }

    [Fact]
    public void DeviceIdentity_ForgetDevice_sets_forgotten_state()
    {
        using var db = Db.Open(_dbPath);

        DeviceIdentity.ForgetDevice(db, MacDevId);

        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.CommandText = "SELECT state FROM devices WHERE device_id = $id";
            cmd.Parameters.AddWithValue("$id", MacDevId);
            cmd.ExecuteScalar().Should().Be("forgotten");
        }

        // Existing historical apps and sessions are preserved
        using (var checkCmd = db.Connection.CreateCommand())
        {
            checkCmd.CommandText = "SELECT COUNT(*) FROM apps WHERE device_id = $id";
            checkCmd.Parameters.AddWithValue("$id", MacDevId);
            checkCmd.ExecuteScalar().Should().Be(1L);
        }
    }

    [Fact]
    public void DeviceIdentity_DeleteDeviceHistory_clears_device_data_without_touching_local()
    {
        using var db = Db.Open(_dbPath);

        DeviceIdentity.DeleteDeviceHistory(db, MacDevId);

        // Remote device, its apps, daily usage, and sessions are gone
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM devices WHERE device_id = $id";
            cmd.Parameters.AddWithValue("$id", MacDevId);
            cmd.ExecuteScalar().Should().Be(0L);

            cmd.CommandText = "SELECT COUNT(*) FROM apps WHERE device_id = $id";
            cmd.ExecuteScalar().Should().Be(0L);

            cmd.CommandText = "SELECT COUNT(*) FROM daily_usage WHERE app_id = 2";
            cmd.ExecuteScalar().Should().Be(0L);

            cmd.CommandText = "SELECT COUNT(*) FROM sessions WHERE app_id = 2";
            cmd.ExecuteScalar().Should().Be(0L);
        }

        // Local device, its apps and sessions remain completely intact
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM devices WHERE device_id = $id";
            cmd.Parameters.AddWithValue("$id", WinDevId);
            cmd.ExecuteScalar().Should().Be(1L);

            cmd.CommandText = "SELECT COUNT(*) FROM apps WHERE device_id = $id";
            cmd.ExecuteScalar().Should().Be(1L);

            cmd.CommandText = "SELECT COUNT(*) FROM daily_usage WHERE app_id = 1";
            cmd.ExecuteScalar().Should().Be(1L);

            cmd.CommandText = "SELECT COUNT(*) FROM sessions WHERE app_id = 1";
            cmd.ExecuteScalar().Should().Be(1L);
        }
    }
    [Fact]
    public void DeviceIdentity_MergeMatchingCrossDeviceApps_merges_remote_into_local()
    {
        using var db = Db.Open(_dbPath);

        var mergedCount = DeviceIdentity.MergeMatchingCrossDeviceApps(db);
        mergedCount.Should().Be(1);

        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT canonical_id FROM apps WHERE id = 2";
        cmd.ExecuteScalar().Should().Be(1L);

        cmd.CommandText = "SELECT canonical_id FROM apps WHERE id = 1";
        cmd.ExecuteScalar().Should().Be(DBNull.Value);
    }
}

