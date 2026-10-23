using Microsoft.Data.Sqlite;
using TimeSink.Core.Data;
using TimeSink.Core.Tracking;

namespace TimeSink.Tests.Tracking;

public sealed class DbTrackingSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "timesink-tests-" + Guid.NewGuid().ToString("N"));
    private readonly Db _db;

    public DbTrackingSettingsTests() => _db = Db.Open(Path.Combine(_dir, "timesink.db"));

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void Missing_keys_give_the_spec_defaults()
    {
        var s = new DbTrackingSettings(_db);
        s.PauseOnSleep.Should().BeTrue();
        s.CountWhenLocked.Should().BeTrue();
        s.MinSessionSeconds.Should().Be(15);
    }

    [Fact]
    public void Values_are_read_live_from_the_settings_table()
    {
        var s = new DbTrackingSettings(_db);
        _db.SetSetting("tracking.pauseOnSleep", "false");
        _db.SetSetting("tracking.countWhenLocked", "false");
        _db.SetSetting("tracking.minSessionSeconds", "30");
        s.PauseOnSleep.Should().BeFalse();
        s.CountWhenLocked.Should().BeFalse();
        s.MinSessionSeconds.Should().Be(30);

        _db.SetSetting("tracking.minSessionSeconds", "5");
        s.MinSessionSeconds.Should().Be(5);
    }

    [Fact]
    public void Garbage_values_fall_back_to_defaults()
    {
        var s = new DbTrackingSettings(_db);
        _db.SetSetting("tracking.pauseOnSleep", "maybe");
        _db.SetSetting("tracking.minSessionSeconds", "abc");
        s.PauseOnSleep.Should().BeTrue();
        s.MinSessionSeconds.Should().Be(15);
    }
}
