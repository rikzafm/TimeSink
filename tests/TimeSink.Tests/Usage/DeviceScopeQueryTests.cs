using Microsoft.Data.Sqlite;
using TimeSink.Core.Apps;
using TimeSink.Core.Data;
using TimeSink.Core.Devices;
using TimeSink.Core.Usage;

namespace TimeSink.Tests.Usage;

public sealed class DeviceScopeQueryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "timesink-devicescope-tests-" + Guid.NewGuid().ToString("N"));

    public DeviceScopeQueryTests()
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
    public void DeviceScope_This_filters_to_local_apps_while_All_includes_remote_apps()
    {
        var path = NewDbPath();
        using var db = Db.Open(path);
        var localDevId = db.GetSetting("device.id")!;
        const string remoteDevId = "remote-mac-uuid-999";

        // Register remote device
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO devices (device_id, name, platform, is_local, first_seen_utc) VALUES ($id, 'MacBook', 'macos', 0, '2026-03-10T00:00:00Z')";
            cmd.Parameters.AddWithValue("$id", remoteDevId);
            cmd.ExecuteNonQuery();
        }

        // Insert local apps
        var localOther = db.InsertApp(@"c:\windows\notepad.exe", "Notepad", 1000, localDevId);
        var localGame = db.InsertApp(@"c:\games\doom.exe", "Doom", 1000, localDevId);

        // Insert remote apps
        var remoteOther = db.InsertApp(@"/Applications/Notes.app", "Notes", 1000, remoteDevId);
        var remoteGame = db.InsertApp(@"/Applications/Hades.app", "Hades", 1000, remoteDevId);

        // Mark kind
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.CommandText = "UPDATE apps SET kind = 1 WHERE id IN ($g1, $g2); UPDATE apps SET kind = 0 WHERE id IN ($o1, $o2);";
            cmd.Parameters.AddWithValue("$g1", localGame);
            cmd.Parameters.AddWithValue("$g2", remoteGame);
            cmd.Parameters.AddWithValue("$o1", localOther);
            cmd.Parameters.AddWithValue("$o2", remoteOther);
            cmd.ExecuteNonQuery();
        }

        // Test AppGroups.Load directly
        var thisApps = AppGroups.Load(db.Connection, includeRetired: false, gamesOnly: false, deviceScope: DeviceScope.This);
        thisApps.Count.Should().Be(2);
        thisApps.ContainsKey(localOther).Should().BeTrue();
        thisApps.ContainsKey(localGame).Should().BeTrue();
        thisApps.ContainsKey(remoteOther).Should().BeFalse();
        thisApps.ContainsKey(remoteGame).Should().BeFalse();

        var allApps = AppGroups.Load(db.Connection, includeRetired: false, gamesOnly: false, deviceScope: DeviceScope.All);
        allApps.Count.Should().Be(4);
        allApps.ContainsKey(localOther).Should().BeTrue();
        allApps.ContainsKey(localGame).Should().BeTrue();
        allApps.ContainsKey(remoteOther).Should().BeTrue();
        allApps.ContainsKey(remoteGame).Should().BeTrue();
    }

    [Fact]
    public void GamesFilter_and_DeviceScope_combine_cleanly()
    {
        var path = NewDbPath();
        using var db = Db.Open(path);
        var localDevId = db.GetSetting("device.id")!;
        const string remoteDevId = "remote-mac-uuid-888";

        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO devices (device_id, name, platform, is_local, first_seen_utc) VALUES ($id, 'MacBook Pro', 'macos', 0, '2026-03-10T00:00:00Z')";
            cmd.Parameters.AddWithValue("$id", remoteDevId);
            cmd.ExecuteNonQuery();
        }

        var localOther = db.InsertApp(@"c:\windows\notepad.exe", "Notepad", 1000, localDevId);
        var localGame = db.InsertApp(@"c:\games\doom.exe", "Doom", 1000, localDevId);
        var remoteOther = db.InsertApp(@"/Applications/Notes.app", "Notes", 1000, remoteDevId);
        var remoteGame = db.InsertApp(@"/Applications/Hades.app", "Hades", 1000, remoteDevId);

        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.CommandText = "UPDATE apps SET kind = 1 WHERE id IN ($g1, $g2); UPDATE apps SET kind = 0 WHERE id IN ($o1, $o2);";
            cmd.Parameters.AddWithValue("$g1", localGame);
            cmd.Parameters.AddWithValue("$g2", remoteGame);
            cmd.Parameters.AddWithValue("$o1", localOther);
            cmd.Parameters.AddWithValue("$o2", remoteOther);
            cmd.ExecuteNonQuery();
        }

        // Scope = This + GamesOnly = true: only local game (Doom)
        var thisGames = AppGroups.Load(db.Connection, includeRetired: false, gamesOnly: true, deviceScope: DeviceScope.This);
        thisGames.Count.Should().Be(1);
        thisGames.ContainsKey(localGame).Should().BeTrue();

        // Scope = All + GamesOnly = true: games from both devices (Doom + Hades)
        var allGames = AppGroups.Load(db.Connection, includeRetired: false, gamesOnly: true, deviceScope: DeviceScope.All);
        allGames.Count.Should().Be(2);
        allGames.ContainsKey(localGame).Should().BeTrue();
        allGames.ContainsKey(remoteGame).Should().BeTrue();
    }

    [Fact]
    public void UsageWindow_honors_DeviceScope_from_UsageContext()
    {
        var path = NewDbPath();
        using var db = Db.Open(path);
        var localDevId = db.GetSetting("device.id")!;
        const string remoteDevId = "remote-mac-uuid-777";

        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO devices (device_id, name, platform, is_local, first_seen_utc) VALUES ($id, 'MacBook', 'macos', 0, '2026-03-10T00:00:00Z')";
            cmd.Parameters.AddWithValue("$id", remoteDevId);
            cmd.ExecuteNonQuery();
        }

        var localApp = db.InsertApp(@"c:\windows\notepad.exe", "Notepad", 1000, localDevId);
        var remoteApp = db.InsertApp(@"/Applications/Notes.app", "Notes", 1000, remoteDevId);

        var now = DateTimeOffset.UtcNow;
        var ctxThis = new UsageContext(now, TimeZoneInfo.Utc, null, () => null, () => WeekStart.Mon, gamesOnly: false, dbPath: path, deviceScope: DeviceScope.This);
        var ctxAll = new UsageContext(now, TimeZoneInfo.Utc, null, () => null, () => WeekStart.Mon, gamesOnly: false, dbPath: path, deviceScope: DeviceScope.All);

        UsageWindow.Over(ctxThis, w =>
        {
            w.Apps.Count.Should().Be(1);
            w.Apps.ContainsKey(localApp).Should().BeTrue();
            w.Apps.ContainsKey(remoteApp).Should().BeFalse();
            return true;
        });

        UsageWindow.Over(ctxAll, w =>
        {
            w.Apps.Count.Should().Be(2);
            w.Apps.ContainsKey(localApp).Should().BeTrue();
            w.Apps.ContainsKey(remoteApp).Should().BeTrue();
            return true;
        });
    }
}

