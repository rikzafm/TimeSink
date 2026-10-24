using Microsoft.Data.Sqlite;
using TimeSink.Core.Data;
using TimeSink.Core.Usage;
using static TimeSink.Tests.Usage.TodayFixture;

namespace TimeSink.Tests.Usage;

/// <summary>
/// Story 17: Today across a DST change, in America/New_York. Hour columns are wall-clock hours (0-23): the skipped
/// hour of a 23 h day is an empty column, the repeated hour of a 25 h day is one column that holds both passes.
/// 2026-03-08 spring forward (02:00 EST -> 03:00 EDT, 05:00Z to 04:00Z next day = 23 h);
/// 2026-11-01 fall back (02:00 EDT -> 01:00 EST, 04:00Z to 05:00Z next day = 25 h).
/// </summary>
public sealed class TodayDstTests : IDisposable
{
    private static readonly TimeZoneInfo Ny = FindNewYork();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "timesink-dst-" + Guid.NewGuid().ToString("N"));
    private readonly string _db;
    private readonly long _app;

    public TodayDstTests()
    {
        Directory.CreateDirectory(_dir);
        _db = Path.Combine(_dir, "t.db");
        using var db = Db.Open(_db);
        _app = db.InsertApp(@"c:\chrome.exe", "Chrome", U(2026, 1, 1, 0));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static TimeZoneInfo FindNewYork()
    {
        foreach (var id in new[] { "America/New_York", "Eastern Standard Time" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { }
        throw new InvalidOperationException("no New York time zone on this machine");
    }

    private void Session(DateTimeOffset start, DateTimeOffset end)
    {
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _db, Pooling = false }.ToString());
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO sessions(app_id,start_utc,end_utc,last_seen_utc,end_reason) VALUES($a,$s,$e,$e,'exit')";
        cmd.Parameters.AddWithValue("$a", _app);
        cmd.Parameters.AddWithValue("$s", start.ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("$e", end.ToUnixTimeSeconds());
        cmd.ExecuteNonQuery();
    }

    private TodayResult Run(DateTimeOffset now) => TodayQuery.Run(_db, now, Ny, running: Array.Empty<long>());

    [Fact]
    public void Spring_forward_day_is_23_hours_and_the_skipped_hour_column_is_empty()
    {
        var r = Run(Utc(2026, 3, 8, 12)); // 08:00 EDT
        (r.DayEndUtc - r.DayStartUtc).Should().Be(23 * 3600);
        r.DayStartUtc.Should().Be(U(2026, 3, 8, 5));
        r.Hours.Should().HaveCount(24);
        r.Hours.Select(h => h.Hour).Should().Equal(Enumerable.Range(0, 24));
        r.Hours[2].AppSeconds.Should().Be(0);
    }

    [Fact]
    public void Spring_forward_buckets_follow_the_wall_clock_across_the_gap()
    {
        // 01:30 EST -> 04:30 EDT (06:30Z-08:30Z): 1:30-2:00 (1800), nothing at 2, 3:00-4:00 (3600), 4:00-4:30 (1800).
        Session(Utc(2026, 3, 8, 6, 30), Utc(2026, 3, 8, 8, 30));
        var r = Run(Utc(2026, 3, 8, 12));
        r.TotalSeconds.Should().Be(7200);
        var h = r.Hours.Select(x => x.AppSeconds).ToArray();
        h[1].Should().Be(1800);
        h[2].Should().Be(0);
        h[3].Should().Be(3600);
        h[4].Should().Be(1800);
        h.Sum().Should().Be(r.TotalSeconds);
    }

    [Fact]
    public void Spring_forward_vs_yesterday_by_now_compares_the_same_wall_clock_time()
    {
        Session(Utc(2026, 3, 8, 6, 30), Utc(2026, 3, 8, 8, 30));     // today: 7200
        // Yesterday (EST all day), cut-off 08:00 EST = 13:00Z.
        Session(Utc(2026, 3, 7, 5, 30), Utc(2026, 3, 7, 6, 30));     // 3600 before the cut-off
        Session(Utc(2026, 3, 7, 12, 30), Utc(2026, 3, 7, 13, 30));   // 1800 before, 1800 after
        Session(Utc(2026, 3, 7, 14), Utc(2026, 3, 7, 15));           // after
        Run(Utc(2026, 3, 8, 12)).VsYesterdaySeconds.Should().Be(7200 - (3600 + 1800));
    }

    [Fact]
    public void Fall_back_day_is_25_hours_and_the_repeated_hour_column_holds_both_passes()
    {
        // 01:30 EDT -> 02:30 EST (05:30Z-07:30Z): hour 1 = 1800 + 3600, hour 2 = 1800.
        Session(Utc(2026, 11, 1, 5, 30), Utc(2026, 11, 1, 7, 30));
        var r = Run(Utc(2026, 11, 1, 18)); // 13:00 EST
        (r.DayEndUtc - r.DayStartUtc).Should().Be(25 * 3600);
        r.DayStartUtc.Should().Be(U(2026, 11, 1, 4));
        r.TotalSeconds.Should().Be(7200);
        var h = r.Hours.Select(x => x.AppSeconds).ToArray();
        h[0].Should().Be(0);
        h[1].Should().Be(5400);
        h[2].Should().Be(1800);
        h[3].Should().Be(0);
        h.Sum().Should().Be(r.TotalSeconds);
    }

    [Fact]
    public void Fall_back_last_column_is_only_the_11pm_hour()
    {
        Session(Utc(2026, 11, 2, 3, 30), Utc(2026, 11, 2, 4, 30)); // 22:30 EST -> 23:30 EST
        var r = Run(Utc(2026, 11, 2, 4, 45));                        // 23:45 EST
        var h = r.Hours.Select(x => x.AppSeconds).ToArray();
        h[22].Should().Be(1800);
        h[23].Should().Be(1800);
        h.Sum().Should().Be(r.TotalSeconds);
    }

    [Fact]
    public void Fall_back_vs_yesterday_by_now_compares_the_same_wall_clock_time()
    {
        Session(Utc(2026, 11, 1, 5, 30), Utc(2026, 11, 1, 7, 30));   // today: 7200
        // Yesterday (EDT all day), cut-off 13:00 EDT = 17:00Z.
        Session(Utc(2026, 10, 31, 10), Utc(2026, 10, 31, 11));       // 3600 before
        Session(Utc(2026, 10, 31, 16, 30), Utc(2026, 10, 31, 17, 30)); // 1800 before, 1800 after
        Session(Utc(2026, 10, 31, 20), Utc(2026, 10, 31, 21));       // after
        Run(Utc(2026, 11, 1, 18)).VsYesterdaySeconds.Should().Be(7200 - (3600 + 1800));
    }

    [Fact]
    public void Day_after_a_dst_change_starts_at_the_new_offset()
    {
        var r = Run(Utc(2026, 3, 9, 12)); // Mon 08:00 EDT
        r.DayStartUtc.Should().Be(U(2026, 3, 9, 4));
        (r.DayEndUtc - r.DayStartUtc).Should().Be(24 * 3600);
    }
}
