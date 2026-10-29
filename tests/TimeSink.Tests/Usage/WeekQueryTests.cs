using TimeSink.Core.Usage;
using static TimeSink.Tests.Usage.WeekFixture;

namespace TimeSink.Tests.Usage;

public sealed class WeekQueryTests : IDisposable
{
    private readonly WeekFixture _f = Standard(out _, out _);
    public void Dispose() => _f.Dispose();

    private WeekResult Run(WeekStart ws = WeekStart.Mon, IReadOnlyCollection<long>? running = null) =>
        WeekQuery.Run(_f.DbPath, Now, TimeZoneInfo.Utc, ws, running);

    [Fact]
    public void Monday_week_has_seven_columns_with_hand_computed_seconds()
    {
        var r = Run();
        r.WeekStartDate.Should().Be(new DateOnly(2026, 3, 9));
        r.Days.Select(d => d.Date.Day).Should().Equal(9, 10, 11, 12, 13, 14, 15);
        // PC uptime: Mon 09:00-14:00; Tue 23:00-24:00 (retired Old ignored); Wed 00:00 (carry-over) to now 14:20.
        r.Days.Select(d => d.Seconds).Should().Equal(18000, 3600, 51600, 0, 0, 0, 0);
        r.TotalSeconds.Should().Be(73200);
        r.Days.Select(d => d.IsToday).Should().Equal(false, false, true, false, false, false, false);
        r.Days.Select(d => d.IsFuture).Should().Equal(false, false, false, true, true, true, true);
    }

    [Fact]
    public void An_open_session_the_agent_does_not_report_counts_only_to_its_heartbeat()
    {
        var r = Run(running: Array.Empty<long>());
        // Uptime of the live day runs to now whatever the Agent reports; only app time stops at the heartbeat.
        r.Days[2].Seconds.Should().Be(51600);
        r.Apps.Single(a => a.Name == "Chrome").Seconds.Should().Be(7200 + 3600 + 1800 + (Unix(2026, 3, 11, 14, 19) - Unix(2026, 3, 11, 11)));
    }

    [Fact]
    public void The_result_carries_the_local_day_the_first_app_was_added()
    {
        Run().FirstTracked.Should().Be(new DateOnly(2025, 1, 1));
    }

    [Fact]
    public void SparkDay_maps_each_sparkline_point_to_its_day_ending_on_today()
    {
        var today = new DateOnly(2026, 3, 11);

        WeekQuery.SparkDay(today, WeekQuery.SparkDays - 1).Should().Be(today);
        WeekQuery.SparkDay(today, 0).Should().Be(today.AddDays(-(WeekQuery.SparkDays - 1)));
        WeekQuery.SparkDay(today, 1, count: 3).Should().Be(today.AddDays(-1));
    }

    [Fact]
    public void Sunday_start_shifts_the_week_and_the_columns()
    {
        var r = Run(WeekStart.Sun);
        r.WeekStartDate.Should().Be(new DateOnly(2026, 3, 8));
        r.Days.Select(d => d.Seconds).Should().Equal(1800, 18000, 3600, 51600, 0, 0, 0);
        r.TotalSeconds.Should().Be(75000);
    }

    [Fact]
    public void Saturday_start_shifts_the_week_and_the_columns()
    {
        var r = Run(WeekStart.Sat);
        r.WeekStartDate.Should().Be(new DateOnly(2026, 3, 7));
        r.Days.Select(d => d.Seconds).Should().Equal(3600, 1800, 18000, 3600, 51600, 0, 0);
        r.TotalSeconds.Should().Be(78600);
    }

    [Fact]
    public void Heat_grid_is_twenty_weeks_week_major_ending_with_the_current_week()
    {
        var h = Run().Heat;
        h.Should().HaveCount(140);
        h[0].Date.Should().Be(new DateOnly(2025, 10, 27)); // 19 weeks (133 days) before Mon 2026-03-09
        h[133].Date.Should().Be(new DateOnly(2026, 3, 9));
        h[139].Date.Should().Be(new DateOnly(2026, 3, 15));
        h.Skip(133).Select(c => c.Seconds).Should().Equal(18000, 3600, 51600, 0, 0, 0, 0);
        h.Skip(133).Select(c => c.Level).Should().Equal(3, 1, 4, 0, 0, 0, 0); // 5 h, 1 h, 14.33 h
        h.Skip(133).Select(c => c.IsFuture).Should().Equal(false, false, false, true, true, true, true);
        h[132].Seconds.Should().Be(1800); // Sun 03-08
        h[131].Seconds.Should().Be(3600); // Sat 03-07 (Code)
        h[128].Seconds.Should().Be(7200); // Wed 03-04
    }

    [Fact]
    public void Week_totals_end_with_this_week_and_last_week_counts_only_the_weekdays_started()
    {
        var r = Run();
        r.WeekTotals.Should().HaveCount(20);
        r.WeekTotals[^1].Should().Be(r.TotalSeconds);
        // Today is Wed, so only last Mon-Wed (heat cells 126-128) count against this week's Mon-Wed.
        r.PrevWeekSeconds.Should().Be(r.Heat.Skip(126).Take(3).Sum(c => c.Seconds)).And.BeGreaterThan(0);
    }

    [Fact]
    public void Streak_counts_back_from_today_until_the_first_empty_day()
    {
        Run().StreakDays.Should().Be(5); // Sat 03-07 .. Wed 03-11; Fri 03-06 is empty
    }

    [Fact]
    public void An_empty_today_does_not_break_the_streak_until_it_ends()
    {
        var f = new WeekFixture();
        using var _ = f;
        var a = f.AddApp("A");
        f.Session(a, Unix(2026, 3, 9, 9), Unix(2026, 3, 9, 10));
        f.Session(a, Unix(2026, 3, 10, 9), Unix(2026, 3, 10, 10));
        var early = WeekQuery.Run(f.DbPath, U(2026, 3, 11, 0, 10), TimeZoneInfo.Utc, WeekStart.Mon);
        early.StreakDays.Should().Be(2); // Wed is still empty but not over
        f.Session(a, Unix(2026, 3, 11, 0, 5), Unix(2026, 3, 11, 0, 8));
        WeekQuery.Run(f.DbPath, U(2026, 3, 11, 0, 10), TimeZoneInfo.Utc, WeekStart.Mon).StreakDays.Should().Be(3);
        // Thursday ends with nothing: the streak is gone.
        WeekQuery.Run(f.DbPath, U(2026, 3, 13, 0, 10), TimeZoneInfo.Utc, WeekStart.Mon).StreakDays.Should().Be(0);
    }

    [Fact]
    public void Streak_ignores_retired_apps()
    {
        var f = new WeekFixture();
        using var _ = f;
        var gone = f.AddApp("Gone");
        f.Session(gone, Unix(2026, 3, 11, 9), Unix(2026, 3, 11, 10));
        f.Retire(gone);
        WeekQuery.Run(f.DbPath, Now, TimeZoneInfo.Utc, WeekStart.Mon).StreakDays.Should().Be(0);
    }

    [Fact]
    public void App_rows_are_this_weeks_totals_sorted_with_ten_day_sparklines()
    {
        var r = Run();
        r.Apps.Select(a => (a.Name, a.Seconds)).Should().Equal(("Chrome", 24600), ("Code", 5400));
        // 03-02 .. 03-11
        r.Apps[0].Spark.Should().Equal(3600, 0, 7200, 0, 0, 0, 1800, 7200, 3600, 13800);
        r.Apps[1].Spark.Should().Equal(0, 0, 0, 0, 0, 3600, 0, 3600, 0, 1800);
    }

    [Fact]
    public void Merged_apps_are_one_row_under_the_canonical_app()
    {
        var f = new WeekFixture();
        using var _ = f;
        var main = f.AddApp("Main");
        var alias = f.AddApp("Alias");
        f.Session(main, Unix(2026, 3, 11, 9), Unix(2026, 3, 11, 10));
        f.Session(alias, Unix(2026, 3, 11, 11), Unix(2026, 3, 11, 11, 30));
        f.Exec($"UPDATE apps SET canonical_id = {main} WHERE id = {alias}");
        var r = WeekQuery.Run(f.DbPath, Now, TimeZoneInfo.Utc, WeekStart.Mon);
        r.Apps.Should().ContainSingle().Which.Should().Match<WeekAppRow>(a => a.Name == "Main" && a.Seconds == 5400);
    }

    private static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    [Fact]
    public void A_session_across_local_midnight_on_spring_forward_day_splits_at_local_midnight()
    {
        var f = new WeekFixture();
        using var _ = f;
        var a = f.AddApp("A");
        // Sat 03-07 23:00 EST (04:00Z) -> Sun 03-08 04:30 EDT (08:30Z). Local midnight 05:00Z; clocks jump 02:00 -> 03:00 at 07:00Z.
        f.Session(a, Unix(2026, 3, 8, 4), Unix(2026, 3, 8, 8, 30));
        var r = WeekQuery.Run(f.DbPath, U(2026, 3, 9, 16), Eastern, WeekStart.Mon); // Mon 12:00 EDT
        var sat = r.Heat.Single(x => x.Date == new DateOnly(2026, 3, 7));
        var sun = r.Heat.Single(x => x.Date == new DateOnly(2026, 3, 8));
        sat.Seconds.Should().Be(3600);
        sun.Seconds.Should().Be(3 * 3600 + 30 * 60); // 05:00Z -> 08:30Z, not 4.5 h of wall clock
        r.StreakDays.Should().Be(2); // Mon (today, empty) does not break it: Sat + Sun
    }

    [Fact]
    public void The_spring_forward_day_is_23_hours_and_the_fall_back_day_is_25()
    {
        var f = new WeekFixture();
        using var _ = f;
        var a = f.AddApp("A");
        f.Session(a, Unix(2026, 3, 8, 5), Unix(2026, 3, 9, 4)); // Sun 00:00 EST -> Mon 00:00 EDT
        f.Session(a, Unix(2026, 11, 1, 4), Unix(2026, 11, 2, 5)); // Sun 00:00 EDT -> Mon 00:00 EST
        var spring = WeekQuery.Run(f.DbPath, U(2026, 3, 9, 16), Eastern, WeekStart.Mon);
        spring.Heat.Single(x => x.Date == new DateOnly(2026, 3, 8)).Seconds.Should().Be(23 * 3600);
        spring.Heat.Single(x => x.Date == new DateOnly(2026, 3, 9)).Seconds.Should().Be(0);
        var fall = WeekQuery.Run(f.DbPath, U(2026, 11, 2, 17), Eastern, WeekStart.Mon); // Mon 12:00 EST
        fall.Heat.Single(x => x.Date == new DateOnly(2026, 11, 1)).Seconds.Should().Be(25 * 3600);
        fall.Heat.Single(x => x.Date == new DateOnly(2026, 11, 2)).Seconds.Should().Be(0);
    }

    [Fact]
    public void The_week_boundary_follows_local_midnight_not_utc()
    {
        var f = new WeekFixture();
        using var _ = f;
        var a = f.AddApp("A");
        // Sun 03-15 21:00 EDT (01:00Z Mon) -> 22:00 EDT: Sunday locally, Monday in UTC. Now = Mon 03-16 10:00 EDT.
        f.Session(a, Unix(2026, 3, 16, 1), Unix(2026, 3, 16, 2));
        var r = WeekQuery.Run(f.DbPath, U(2026, 3, 16, 14), Eastern, WeekStart.Mon);
        r.WeekStartDate.Should().Be(new DateOnly(2026, 3, 16));
        r.TotalSeconds.Should().Be(0);
        r.Heat.Single(x => x.Date == new DateOnly(2026, 3, 15)).Seconds.Should().Be(3600);
    }

    [Fact]
    public void The_week_start_setting_parses_with_a_Monday_default()
    {
        WeekStarts.Parse("Sat").Should().Be(WeekStart.Sat);
        WeekStarts.Parse("Sun").Should().Be(WeekStart.Sun);
        WeekStarts.Parse(null).Should().Be(WeekStart.Mon);
        WeekStarts.Parse("bogus").Should().Be(WeekStart.Mon);
    }
}
