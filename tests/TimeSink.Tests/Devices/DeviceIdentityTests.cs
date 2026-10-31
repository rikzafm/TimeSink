using Microsoft.Data.Sqlite;
using TimeSink.Core.Data;
using TimeSink.Core.Devices;
using TimeSink.Core.Settings;

namespace TimeSink.Tests.Devices;

public sealed class DeviceIdentityTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "timesink-device-tests-" + Guid.NewGuid().ToString("N"));

    public DeviceIdentityTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }

    private string NewDbPath() => Path.Combine(_dir, $"test-{Guid.NewGuid():N}.db");

    [Fact]
    public void First_run_generates_device_uuid_and_persists_settings_and_devices_row()
    {
        var path = NewDbPath();
        using var db = Db.Open(path);

        var device = DeviceIdentity.GetLocalDevice(db);

        device.DeviceId.Should().NotBeNullOrWhiteSpace();
        Guid.TryParse(device.DeviceId, out _).Should().BeTrue();
        device.Name.Should().NotBeNullOrWhiteSpace();
        device.Platform.Should().Match(p => p == "windows" || p == "macos");
        device.IsLocal.Should().BeTrue();
        device.State.Should().Be("active");

        db.GetSetting("device.id").Should().Be(device.DeviceId);
        db.GetSetting("device.name").Should().Be(device.Name);

        // Verify default device settings
        var settings = AppSettings.ForFile(path);
        settings.Get(Setting.SyncEnabled).Should().BeFalse();
        settings.Get(Setting.SyncFolder).Should().BeNull();
        settings.Get(Setting.DeviceScope).Should().Be(DeviceScope.This);
    }

    [Fact]
    public void Restart_reuses_the_same_device_id_and_name()
    {
        var path = NewDbPath();
        string firstId;
        string firstName;

        using (var db = Db.Open(path))
        {
            var device = DeviceIdentity.GetLocalDevice(db);
            firstId = device.DeviceId;
            firstName = device.Name;
        }

        using (var db2 = Db.Open(path))
        {
            var device2 = DeviceIdentity.GetLocalDevice(db2);
            device2.DeviceId.Should().Be(firstId);
            device2.Name.Should().Be(firstName);
        }
    }

    [Fact]
    public void Rename_updates_settings_and_devices_row()
    {
        var path = NewDbPath();
        using var db = Db.Open(path);

        var device = DeviceIdentity.GetLocalDevice(db);
        const string newName = "My Gaming Rig";

        DeviceIdentity.RenameLocalDevice(db, newName);

        db.GetSetting("device.name").Should().Be(newName);
        var updated = DeviceIdentity.GetLocalDevice(db);
        updated.Name.Should().Be(newName);
        updated.DeviceId.Should().Be(device.DeviceId);
    }

    [Fact]
    public void Duplicate_device_id_regenerates_id_and_rebinds_apps()
    {
        var path = NewDbPath();
        using var db = Db.Open(path);

        var originalDevice = DeviceIdentity.GetLocalDevice(db);
        var appId = db.InsertApp(@"c:\apps\test.exe", "Test App", 100);

        // A snapshot arrives carrying the local device's ID from an external cloned install
        var warning = DeviceIdentity.HandleClonedDeviceConflict(db, originalDevice.DeviceId);

        warning.Should().Contain("Cloned device identity detected");

        var newLocalId = db.GetSetting("device.id")!;
        newLocalId.Should().NotBe(originalDevice.DeviceId);

        // Verify local device record was created
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT is_local FROM devices WHERE device_id = $id";
        cmd.Parameters.AddWithValue("$id", newLocalId);
        ((long)cmd.ExecuteScalar()!).Should().Be(1);

        // Verify old device was demoted to non-local
        cmd.Parameters.Clear();
        cmd.CommandText = "SELECT is_local FROM devices WHERE device_id = $id";
        cmd.Parameters.AddWithValue("$id", originalDevice.DeviceId);
        ((long)cmd.ExecuteScalar()!).Should().Be(0);

        // Verify apps were rebound to new local device
        cmd.Parameters.Clear();
        cmd.CommandText = "SELECT device_id FROM apps WHERE id = $appId";
        cmd.Parameters.AddWithValue("$appId", appId);
        ((string)cmd.ExecuteScalar()!).Should().Be(newLocalId);
    }
}
