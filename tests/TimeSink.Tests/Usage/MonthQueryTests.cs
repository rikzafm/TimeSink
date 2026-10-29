using TimeSink.Core.Usage;
using static TimeSink.Tests.Usage.MonthFixture;

namespace TimeSink.Tests.Usage;

public sealed class MonthQueryTests : IDisposable
{
    private readonly MonthFixture _f = new();
    public void Dispose() => _f.Dispose();

    private MonthResult March(DayOfWeek start = DayOfWeek.Monday, TimeZoneInfo? zone = null, IReadOnlyCollection<long>? running = null) =>
        Q.Month(_f.DbPath, Now, zone ?? TimeZoneInfo.Utc, 2026, 3, start, running);

    private static MonthDay Day(MonthResult r, int d) => r.Days.Single(x => x.Date.Day == d);

    [Fact]
    public void Daily_totals_match_the_hand_computed_fixture()
    {
        var r = March();
        Day(r, 1).Seconds.Should().Be(1800);
        Day(r, 2).Seconds.Should().Be(43200);   // uptime 09:00-21:00; retired Old excluded
        Day(r, 3).Seconds.Should().Be(54000);   // 09:00 to midnight; the carry-over is split between the 3rd and 4th
        Day(r, 4).Seconds.Should().Be(7200);
        Day(r, 5).Seconds.Should().Be(1800);
        Day(r, 6).Seconds.Should().Be(0);
        Day(r, 10).Seconds.Should().Be(36000);
        Day(r, 11).Seconds.Should().Be(22800);  // 08:00 to now (the live day)
        Day(r, 11).IsToday.Should().BeTrue();
        Day(r, 12).IsFuture.Should().BeTrue();
        Day(r, 12).Seconds.Should().Be(0);
        r.Days.Should().HaveCount(31);
    }

    [Fact]
    public void Heat_levels_follow_the_daily_totals()
    {
        var r = March();
        Day(r, 1).Heat.Should().Be(0);   // 30 m
        Day(r, 2).Heat.Should().Be(4);   // 12 h
        Day(r, 3).Heat.Should().Be(4);   // 15 h
        Day(r, 4).Heat.Should().Be(1);   // 2 h
        Day(r, 11).Heat.Should().Be(3);  // 6.3 h
    }

    [Fact]
    public void Hero_stats_total_average_best_and_delta_over_the_same_number_of_days()
    {
        var r = March();
        r.IsCurrent.Should().BeTrue();
        r.DaysCounted.Should().Be(11);
        r.TotalSeconds.Should().Be(166800);
        r.DailyAverageSeconds.Should().BeApproximately(166800 / 11.0, 1e-9);
        r.Best.Should().Be(new BestDay(new DateOnly(2026, 3, 3), 54000));
        r.PreviousSeconds.Should().Be(32400);   // Feb 1-11 only; Feb 12 and 28 are outside the same-days window
        r.DeltaSeconds.Should().Be(134400);
        r.Delta!.Value.Percent.Should().BeApproximately(134400 / 32400.0 * 100, 1e-9);
    }

    [Fact]
    public void All_apps_ranked_with_deltas_against_the_same_days_of_last_month()
    {
        var r = March();
        r.TopApps.Select(a => a.Name).Take(4).Should().Equal("Code", "Chrome", "Disc", ZedName);
        r.TopApps.Should().HaveCountGreaterThan(4); // no cap: the month tab scrolls
        r.TopApps[0].Should().BeEquivalentTo(new { Seconds = 55800L, PreviousSeconds = 7200L, DeltaSeconds = 48600L });
        r.TopApps[0].Delta!.Value.Percent.Should().BeApproximately(675, 1e-9);
        r.TopApps[1].Should().BeEquivalentTo(new { Seconds = 37200L, PreviousSeconds = 18000L, DeltaSeconds = 19200L });
        r.TopApps[2].Should().BeEquivalentTo(new { Seconds = 9000L, PreviousSeconds = 7200L, DeltaSeconds = 1800L });
        r.TopApps[2].Delta!.Value.Percent.Should().BeApproximately(25, 1e-9);
        r.TopApps[3].Should().BeEquivalentTo(new { Seconds = 1800L, PreviousSeconds = 0L, DeltaSeconds = 1800L });
        r.TopApps[3].Delta.Should().BeNull(); // no baseline: "new", not infinity
    }

    [Fact]
    public void Selected_day_breakdown_lists_apps_by_time()
    {
        var d = Day(March(), 2);
        d.Apps.Select(a => (a.Name, a.Seconds)).Should().Equal(("Chrome", 10800L), ("Code", 7200L), ("Disc", 3600L));
    }

    [Fact]
    public void Default_selected_day_is_today_for_the_current_month_and_the_last_day_otherwise()
    {
        March().DefaultSelected.Should().Be(new DateOnly(2026, 3, 11));
        Q.Month(_f.DbPath, Now, TimeZoneInfo.Utc, 2026, 2, DayOfWeek.Monday).DefaultSelected.Should().Be(new DateOnly(2026, 2, 28));
    }

    [Fact]
    public void Monday_first_grid_pads_leading_blanks_and_totals_each_week()
    {
        var r = March(DayOfWeek.Monday);
        r.Weeks.Should().HaveCount(6);
        r.Weeks[0].Days.Select(d => d?.Date.Day).Should().Equal(null, null, null, null, null, null, 1);
        r.Weeks[0].TotalSeconds.Should().Be(1800);
        r.Weeks[1].Days.Select(d => d!.Date.Day).Should().Equal(2, 3, 4, 5, 6, 7, 8);
        r.Weeks[1].TotalSeconds.Should().Be(43200 + 54000 + 7200 + 1800);
        r.Weeks[2].TotalSeconds.Should().Be(36000 + 22800);
        r.Weeks[5].Days.Select(d => d?.Date.Day).Should().Equal(30, 31, null, null, null, null, null);
        r.Weeks[5].TotalSeconds.Should().Be(0);
    }

    [Fact]
    public void Sunday_first_grid_starts_on_the_first_with_no_blanks()
    {
        var r = March(DayOfWeek.Sunday);
        r.Weeks.Should().HaveCount(5);
        r.Weeks[0].Days.Select(d => d!.Date.Day).Should().Equal(1, 2, 3, 4, 5, 6, 7);
        r.Weeks[0].TotalSeconds.Should().Be(1800 + 43200 + 54000 + 7200 + 1800);
        r.Weeks[1].TotalSeconds.Should().Be(36000 + 22800);
    }

    [Fact]
    public void Week_start_setting_parses_Mon_Sun_Sat_and_defaults_to_Monday()
    {
        MonthQuery.ParseWeekStart("Sun").Should().Be(DayOfWeek.Sunday);
        MonthQuery.ParseWeekStart("Sat").Should().Be(DayOfWeek.Saturday);
        MonthQuery.ParseWeekStart("Mon").Should().Be(DayOfWeek.Monday);
        MonthQuery.ParseWeekStart(null).Should().Be(DayOfWeek.Monday);
        MonthQuery.ParseWeekStart("junk").Should().Be(DayOfWeek.Monday);
    }

    [Fact]
    public void A_completed_month_counts_every_day_and_has_no_baseline_when_last_month_is_empty()
    {
        var r = Q.Month(_f.DbPath, Now, TimeZoneInfo.Utc, 2026, 2, DayOfWeek.Monday);
        r.IsCurrent.Should().BeFalse();
        r.DaysCounted.Should().Be(28);
        r.TotalSeconds.Should().Be(75600);
        r.DailyAverageSeconds.Should().BeApproximately(2700, 1e-9);
        r.Best.Should().Be(new BestDay(new DateOnly(2026, 2, 12), 36000));
        r.PreviousSeconds.Should().Be(0);
        r.Delta.Should().BeNull();
        r.Days.Should().OnlyContain(d => !d.IsFuture && !d.IsToday);
    }

    [Fact]
    public void A_future_month_is_empty()
    {
        var r = Q.Month(_f.DbPath, Now, TimeZoneInfo.Utc, 2026, 4, DayOfWeek.Monday);
        r.DaysCounted.Should().Be(0);
        r.TotalSeconds.Should().Be(0);
        r.DailyAverageSeconds.Should().Be(0);
        r.Best.Should().BeNull();
        r.TopApps.Should().BeEmpty();
        r.Days.Should().OnlyContain(d => d.IsFuture);
    }

    [Fact]
    public void An_open_session_the_agent_does_not_report_counts_only_to_its_heartbeat()
    {
        var r = March(running: Array.Empty<long>());
        Day(r, 11).Seconds.Should().Be(22800); // uptime of the live day runs to now regardless
        Day(r, 11).Apps.Single(a => a.Name == "Chrome").Seconds.Should().Be(12000 - 60); // app time stops at the heartbeat 14:19
    }

    [Fact]
    public void Merged_apps_are_one_row_under_the_canonical_app()
    {
        var alias = _f.Chrome + 100;
        _f.Exec($"INSERT INTO apps(id, exe_path, display_name, added_utc, canonical_id) VALUES({alias}, 'c:\\\\chrome2.exe', 'Chrome2', {U(2026, 1, 1, 0)}, {_f.Chrome})");
        _f.Session(alias, U(2026, 3, 4, 12), U(2026, 3, 4, 13));
        var d = Day(March(), 4);
        d.Seconds.Should().Be(46800); // uptime 00:00 (carry-over) to 13:00
        d.Apps.Should().ContainSingle().Which.Name.Should().Be("Chrome");
    }

    [Fact]
    public void Days_follow_the_local_zone()
    {
        var plus10 = TimeZoneInfo.CreateCustomTimeZone("t+10", TimeSpan.FromHours(10), "t", "t");
        // Local Mar 4 = Mar 3 14:00Z .. Mar 4 14:00Z: uptime from Code 14:00Z to Chrome's end 02:00Z.
        Day(March(zone: plus10), 4).Seconds.Should().Be(12 * 3600);
    }
}
