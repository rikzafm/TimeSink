using FluentAssertions;
using Microsoft.Data.Sqlite;
using TimeSink.Core.Data;
using TimeSink.Core.Devices;
using TimeSink.Core.Settings;
using Xunit;

namespace TimeSink.Tests.Settings;

public class DeviceInfoServiceTests : IDisposable
{
    private readonly string _dbPath;

    public DeviceInfoServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"timesink_device_info_{Guid.NewGuid():N}.db");
        using var db = Db.Open(_dbPath);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    [Fact]
    public void ComputeDisplayNames_SingleWindowsAndSingleMac_ResolvesToPCAndMac()
    {
        var devices = new List<(string Id, string Name, string Platform, bool IsLocal, string FirstSeen)>
        {
            ("dev-win", "DESKTOP-B9N00S8", "windows", true, "2026-01-01T00:00:00Z"),
            ("dev-mac", "Rikza's MacBook Pro", "macos", false, "2026-01-02T00:00:00Z"),
        };

        var names = DeviceInfoService.ComputeDisplayNames(devices, shortNames: true);
        names["dev-win"].Should().Be("PC");
        names["dev-mac"].Should().Be("Mac");
    }

    [Fact]
    public void ComputeDisplayNames_TwoWindowsPCs_ResolvesToNumberedPCs_WithLocalFirst()
    {
        var devices = new List<(string Id, string Name, string Platform, bool IsLocal, string FirstSeen)>
        {
            ("dev-win-remote", "Living Room PC", "windows", false, "2026-01-01T00:00:00Z"),
            ("dev-win-local", "DESKTOP-B9N00S8", "windows", true, "2026-01-02T00:00:00Z"),
        };

        var names = DeviceInfoService.ComputeDisplayNames(devices, shortNames: true);
        names["dev-win-local"].Should().Be("PC 1");
        names["dev-win-remote"].Should().Be("PC 2");
    }

    [Fact]
    public void ComputeDisplayNames_TwoMacs_ResolvesToNumberedMacs_WithLocalFirst()
    {
        var devices = new List<(string Id, string Name, string Platform, bool IsLocal, string FirstSeen)>
        {
            ("dev-mac-remote", "Studio Mac", "macos", false, "2026-01-01T00:00:00Z"),
            ("dev-mac-local", "MacBook Pro", "macos", true, "2026-01-02T00:00:00Z"),
        };

        var names = DeviceInfoService.ComputeDisplayNames(devices, shortNames: true);
        names["dev-mac-local"].Should().Be("Mac 1");
        names["dev-mac-remote"].Should().Be("Mac 2");
    }

    [Fact]
    public void ComputeDisplayNames_MultipleWindowsAndOneMac_ResolvesNumberedPCsAndSingleMac()
    {
        var devices = new List<(string Id, string Name, string Platform, bool IsLocal, string FirstSeen)>
        {
            ("dev-win-local", "Desktop PC", "windows", true, "2026-01-01T00:00:00Z"),
            ("dev-win-laptop", "Laptop PC", "windows", false, "2026-01-02T00:00:00Z"),
            ("dev-mac", "MacBook Pro", "macos", false, "2026-01-03T00:00:00Z"),
        };

        var names = DeviceInfoService.ComputeDisplayNames(devices, shortNames: true);
        names["dev-win-local"].Should().Be("PC 1");
        names["dev-win-laptop"].Should().Be("PC 2");
        names["dev-mac"].Should().Be("Mac");
    }

    [Fact]
    public void ComputeDisplayNames_ShortNamesDisabled_ResolvesToCustomDeviceNames()
    {
        var devices = new List<(string Id, string Name, string Platform, bool IsLocal, string FirstSeen)>
        {
            ("dev-win", "DESKTOP-B9N00S8", "windows", true, "2026-01-01T00:00:00Z"),
            ("dev-mac", "Rikza's MacBook Pro", "macos", false, "2026-01-02T00:00:00Z"),
        };

        var names = DeviceInfoService.ComputeDisplayNames(devices, shortNames: false);
        names["dev-win"].Should().Be("DESKTOP-B9N00S8");
        names["dev-mac"].Should().Be("Rikza's MacBook Pro");
    }

    [Fact]
    public void Load_ReadsSettingAndDevices_FromDatabase()
    {
        using (var conn = Db.CreateConnection(_dbPath))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                DELETE FROM devices;
                INSERT INTO devices (device_id, name, platform, is_local, first_seen_utc, state) VALUES ('dev1', 'DESKTOP-B9N00S8', 'windows', 1, '2026-01-01', 'active');
                INSERT INTO devices (device_id, name, platform, is_local, first_seen_utc, state) VALUES ('dev2', 'Rikza’s MacBook Pro', 'macos', 0, '2026-01-02', 'active');
                INSERT INTO settings (key, value) VALUES ('devices.shortNames', 'true') ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            ";
            cmd.ExecuteNonQuery();
        }

        var info = DeviceInfoService.Load(_dbPath);
        info["dev1"].Name.Should().Be("PC");
        info["dev2"].Name.Should().Be("Mac");

        // Now toggle off
        using (var conn = Db.CreateConnection(_dbPath))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO settings (key, value) VALUES ('devices.shortNames', 'false') ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
            cmd.ExecuteNonQuery();
        }

        info = DeviceInfoService.Load(_dbPath);
        info["dev1"].Name.Should().Be("DESKTOP-B9N00S8");
        info["dev2"].Name.Should().Be("Rikza’s MacBook Pro");
    }
}
