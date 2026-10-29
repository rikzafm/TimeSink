using FluentAssertions;
using Microsoft.Data.Sqlite;
using TimeSink.Core.Apps;
using TimeSink.Core.Data;
using TimeSink.Core.Settings;

namespace TimeSink.Tests.Apps;

public sealed class SteamAutoTrackerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ts-auto-" + Guid.NewGuid().ToString("N"));
    private readonly string _db;

    public SteamAutoTrackerTests()
    {
        Directory.CreateDirectory(_dir);
        _db = Path.Combine(_dir, "t.db");
        using (Db.Open(_db)) { }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string Game(int id, string name)
    {
        var steamapps = Path.Combine(_dir, "steamapps");
        Directory.CreateDirectory(steamapps);
        File.WriteAllText(Path.Combine(steamapps, $"appmanifest_{id}.acf"), $"\"AppState\"\n{{\n\"appid\" \"{id}\"\n\"name\" \"{name}\"\n\"installdir\" \"{name}\"\n}}\n");
        var exe = Path.Combine(steamapps, "common", name, name + ".exe");
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        File.WriteAllBytes(exe, new byte[100]);
        return exe;
    }

    private IReadOnlyList<LibraryApp> Library() { using var l = AppLibrary.Open(_db); return l.Library(); }

    [Fact]
    public void Adds_new_games_once_and_not_again_on_the_next_scan()
    {
        Game(1, "Hades");
        SteamAutoTracker.Run(_db, _dir, 100).Should().Be(1);
        SteamAutoTracker.Run(_db, _dir, 200).Should().Be(0);

        Library().Should().ContainSingle(a => a.DisplayName == "Hades" && a.State == AppState.Tracked);

        Game(2, "Celeste");
        SteamAutoTracker.Run(_db, _dir, 300).Should().Be(1);
        Library().Should().HaveCount(2);
    }

    [Fact]
    public void A_game_the_user_stopped_tracking_is_not_added_back()
    {
        Game(1, "Hades");
        SteamAutoTracker.Run(_db, _dir, 100);
        using (var l = AppLibrary.Open(_db)) l.Retire(l.Library().Single().Id, 150);

        SteamAutoTracker.Run(_db, _dir, 200).Should().Be(0);
        Library().Single().State.Should().Be(AppState.Retired);
    }

    [Fact]
    public void Does_nothing_when_the_setting_is_off()
    {
        Game(1, "Hades");
        AppSettings.ForFile(_db).Set(Setting.SteamAutoTrack, false);

        SteamAutoTracker.Run(_db, _dir, 100).Should().Be(0);
        Library().Should().BeEmpty();
    }

    [Fact]
    public void Runs_when_the_setting_is_unreadable()
    {
        Game(1, "Hades");
        new FileSettingsSource(_db).Set(Setting.SteamAutoTrack.Key, "junk");

        SteamAutoTracker.Run(_db, _dir, 100).Should().Be(1);
    }
}
