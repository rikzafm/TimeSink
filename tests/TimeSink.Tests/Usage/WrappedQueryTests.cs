using TimeSink.Core.Usage;
using static TimeSink.Tests.Usage.WrappedFixture;

namespace TimeSink.Tests.Usage;

public sealed class WrappedQueryTests : IDisposable
{
    private readonly WrappedFixture _f = new();
    public void Dispose() => _f.Dispose();

    private WrappedResult Run(IReadOnlyCollection<long>? running = null) =>
        WrappedQuery.Run(_f.DbPath, Now, Zone, running);

    [Fact]
    public void Total_and_the_days_and_hours_equivalent_match_the_fixture()
    {
        var r = Run();
        r.Year.Should().Be(2026);
        r.AppSeconds.Should().Be(78000);            // retired Old excluded, open Alpha counted to now
        r.TotalSeconds.Should().Be(117000);         // PC uptime: first to last session per day, overlaps counted once
        r.EquivalentDays.Should().Be(1);
        r.EquivalentHours.Should().Be(8);           // 117000 s = 32 h 30 m
        r.AppsTracked.Should().Be(4);
        r.TrackingStarted.Should().Be(new DateOnly(2026, 3, 3));
        r.DaysTracked.Should().Be(100);             // Mar 3 .. Jun 10 inclusive: 29 + 30 + 31 + 10
        r.DaysWithActivity.Should().Be(9);
    }

    [Fact]
    public void Equivalent_splits_whole_days_from_the_remaining_hours()
    {
        _f.Session(_f.Beta, L(2026, 6, 1, 0).ToUnixTimeSeconds(), L(2026, 6, 4, 6).ToUnixTimeSeconds()); // 78 h more
        var r = Run();
        r.AppSeconds.Should().Be(78000 + 78 * 3600);
        r.TotalSeconds.Should().Be(397800);
        r.EquivalentDays.Should().Be(4);            // 397800 s = 110 h 30 m = 4 d 14 h
        r.EquivalentHours.Should().Be(14);
    }

    [Fact]
    public void Podium_is_the_top_five_apps_and_the_rest_follow()
    {
        var r = Run();
        r.Podium.Select(a => (a.Name, a.Seconds)).Should().Equal(("Alpha", 64800L), ("Beta", 10800L), ("Gamma", 1800L), ("Delta", 600L));
        r.Others.Should().BeEmpty();
    }

    [Fact]
    public void Longest_streak_uses_local_days_and_reports_its_span()
    {
        var r = Run();
        r.LongestStreakDays.Should().Be(4);         // Mar 3-6; Mar 5 23:00 local is Mar 5 17:30 UTC, the next local day is Mar 6
        r.StreakStart.Should().Be(new DateOnly(2026, 3, 3));
        r.StreakEnd.Should().Be(new DateOnly(2026, 3, 6));
    }

    [Fact]
    public void Busiest_month_with_its_delta_against_the_month_before()
    {
        var r = Run();
        r.BusiestMonth.Should().Be(6);
        r.BusiestMonthSeconds.Should().Be(27600);   // Alpha 27000 + Delta 600
        r.BusiestMonthDeltaPercent.Should().BeApproximately(666.67, 0.01); // vs May 3600
    }

    [Fact]
    public void Busiest_month_has_no_delta_when_the_previous_month_was_empty()
    {
        _f.Session(_f.Beta, L(2026, 3, 10, 0).ToUnixTimeSeconds(), L(2026, 3, 12, 0).ToUnixTimeSeconds()); // March busiest; Feb empty
        var r = Run();
        r.BusiestMonth.Should().Be(3);
        r.BusiestMonthDeltaPercent.Should().BeNull();
    }

    [Fact]
    public void Peak_hour_is_the_local_hour_with_the_most_app_time()
    {
        var r = Run();
        r.PeakHour.Should().Be(14);                 // 16200 s (in UTC the same time would be hour 8)
        r.PeakHourSeconds.Should().Be(16200);
    }

    [Fact]
    public void Share_before_noon_is_a_rounded_percentage_of_local_time()
    {
        Run().BeforeNoonPercent.Should().Be(47);    // 36600 / 78000 = 46.9 %
    }

    [Fact]
    public void An_open_session_of_an_app_the_agent_does_not_report_stops_at_its_last_heartbeat()
    {
        var r = Run(running: new long[] { _f.Beta });
        r.AppSeconds.Should().Be(78000 - 300);    // last seen 15:25 not 15:30
    }

    [Fact]
    public void Nothing_tracked_gives_an_empty_result()
    {
        var dir = Path.Combine(Path.GetTempPath(), "timesink-wrapped-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var db = Path.Combine(dir, "e.db");
        try
        {
            TimeSink.Core.Data.Db.Open(db).Dispose();
            var r = WrappedQuery.Run(db, Now, Zone, null);
            r.TotalSeconds.Should().Be(0);
            r.Podium.Should().BeEmpty();
            r.TrackingStarted.Should().BeNull();
            r.DaysTracked.Should().Be(0);
            r.PeakHour.Should().BeNull();
            r.BusiestMonth.Should().BeNull();
            r.LongestStreakDays.Should().Be(0);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}
