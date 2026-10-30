using Microsoft.Data.Sqlite;
using TimeSink.Core.Apps;
using TimeSink.Core.Data;
using TimeSink.Core.Usage;
using static TimeSink.Tests.Usage.TodayFixture;

namespace TimeSink.Tests.Usage;

/// <summary>Decision 147: one Tracking start (the first Local day a non-retired Tracked app existed) for every usage query.</summary>
public sealed class TrackingStartTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "timesink-tstart-" + Guid.NewGuid().ToString("N"));
    private readonly string _db;

    public TrackingStartTests()
    {
        Directory.CreateDirectory(_dir);
        _db = Path.Combine(_dir, "t.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static GroupedApp App(long id, long addedUtc, bool retired = false) =>
        new(id, id, "A" + id, addedUtc, State: retired ? AppState.Retired : AppState.Tracked);

    [Fact]
    public void Nothing_tracked_has_no_start_and_counts_no_day()
    {
        var start = TrackingStart.Of(AppGroups.From([]), TimeZoneInfo.Utc);
        start.FirstDay.Should().BeNull();
        start.Counts(new DateOnly(2026, 3, 1)).Should().BeFalse();
        start.IsPartialMonth(2026, 3).Should().BeFalse();
        start.Since(new DateOnly(2026, 1, 1)).Should().Be(new DateOnly(2026, 1, 1));
    }

    [Fact]
    public void The_start_is_the_earliest_added_day_in_the_zone()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("t+10", TimeSpan.FromHours(10), "t", "t");
        // 2026-03-05 20:00Z is already 2026-03-06 06:00 at +10.
        var start = TrackingStart.Of(AppGroups.From([App(1, U(2026, 3, 5, 20)), App(2, U(2026, 3, 9, 1))]), zone);
        start.FirstDay.Should().Be(new DateOnly(2026, 3, 6));
        start.Counts(new DateOnly(2026, 3, 5)).Should().BeFalse();
        start.Counts(new DateOnly(2026, 3, 6)).Should().BeTrue();
        start.IsPartialMonth(2026, 3).Should().BeTrue();
        start.IsPartialMonth(2026, 4).Should().BeFalse();
        start.Since(new DateOnly(2026, 3, 1)).Should().Be(new DateOnly(2026, 3, 6));
        start.Since(new DateOnly(2026, 3, 20)).Should().Be(new DateOnly(2026, 3, 20));
    }

    [Fact]
    public void A_month_that_begins_on_the_1st_is_a_full_month()
    {
        var start = TrackingStart.Of(AppGroups.From([App(1, U(2026, 3, 1, 0))]), TimeZoneInfo.Utc);
        start.IsPartialMonth(2026, 3).Should().BeFalse();
    }

    [Fact]
    public void Month_average_counts_only_the_days_since_the_start()
    {
        using (var db = Db.Open(_db))
        {
            var app = db.InsertApp(@"c:\chrome.exe", "Chrome", U(2026, 3, 6, 0));
            Session(app, U(2026, 3, 6, 10), U(2026, 3, 6, 11));   // 3600
            Session(app, U(2026, 3, 10, 10), U(2026, 3, 10, 12)); // 7200
        }
        var r = Q.Month(_db, Utc(2026, 3, 11, 14, 20), TimeZoneInfo.Utc, 2026, 3, WeekStart.Mon, Array.Empty<long>());
        r.DaysCounted.Should().Be(11);
        r.TotalSeconds.Should().Be(10800);
        r.DailyAverageSeconds.Should().BeApproximately(10800 / 6.0, 1e-9); // Mar 6..11, not Mar 1..11
        r.FirstTracked.Should().Be(new DateOnly(2026, 3, 6));
    }

    private void Session(long app, long start, long end)
    {
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _db, Pooling = false }.ToString());
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO sessions(app_id,start_utc,end_utc,last_seen_utc,end_reason) VALUES($a,$s,$e,$e,'exit')";
        cmd.Parameters.AddWithValue("$a", app);
        cmd.Parameters.AddWithValue("$s", start);
        cmd.Parameters.AddWithValue("$e", end);
        cmd.ExecuteNonQuery();
    }
}
