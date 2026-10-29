using TimeSink.Core.Usage;
using static TimeSink.Tests.Usage.MonthFixture;

namespace TimeSink.Tests.Usage;

/// <summary>
/// Uses <see cref="MonthFixture"/> (zone UTC, now = 2026-03-11 14:20Z). Hand-computed for Chrome:
///   7 sessions: Feb 2 (14400), Feb 11 (3600), Feb 12 (36000), Feb 28 (7200), Mar 2 (10800), Mar 3 22:00 to Mar 4 02:00 (14400),
///   Mar 11 11:00 to now (12000, running) = 98400 in total, average 14057, longest 36000, first tracked 2026-02-02.
///   Busiest day Feb 12 (36000); the Mar 3 session is one opening but touches two days (7200 on each).
/// </summary>
public sealed class AppDetailQueryTests : IDisposable
{
    private readonly MonthFixture _f = new();
    public void Dispose() => _f.Dispose();

    private AppDetailResult? Run(long app, int year = 2026, IReadOnlyCollection<long>? running = null) =>
        Q.AppDetail(_f.DbPath, Now, TimeZoneInfo.Utc, app, year, running);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(25, 1)]
    [InlineData(26, 2)]
    [InlineData(50, 2)]
    [InlineData(51, 3)]
    [InlineData(75, 3)]
    [InlineData(76, 4)]
    [InlineData(100, 4)]
    public void Level_is_quarters_of_the_busiest_day(long seconds, int level) =>
        AppDetailQuery.Level(seconds, 100).Should().Be(level);

    [Fact]
    public void Level_is_zero_without_a_peak() => AppDetailQuery.Level(50, 0).Should().Be(0);

    [Fact]
    public void Lifetime_stats_count_every_session_once()
    {
        var r = Run(_f.Chrome)!;
        r.Name.Should().Be("Chrome");
        r.ExeName.Should().Be("chrome.exe");
        r.TotalSeconds.Should().Be(98400);
        r.SessionCount.Should().Be(7);
        r.AverageSessionSeconds.Should().Be(14057);
        r.LongestSessionSeconds.Should().Be(36000);
        r.FirstTracked.Should().Be(new DateOnly(2026, 2, 2));
    }

    [Fact]
    public void Last_opened_is_the_latest_session_and_flags_a_running_one()
    {
        var r = Run(_f.Chrome)!;
        r.Last.Should().Be(new AppDetailLast(U(2026, 3, 11, 11), 12000, true));
    }

    [Fact]
    public void A_session_that_is_not_running_ends_at_its_heartbeat()
    {
        var r = Run(_f.Chrome, running: [])!;
        r.Last.Should().Be(new AppDetailLast(U(2026, 3, 11, 11), 11940, false)); // last seen 14:19
        r.TotalSeconds.Should().Be(98400 - 60);
    }

    [Fact]
    public void Year_grid_covers_every_day_with_levels_against_the_peak_day()
    {
        var r = Run(_f.Chrome)!;
        r.Year.Should().Be(2026);
        r.Days.Should().HaveCount(365);
        r.PeakDaySeconds.Should().Be(36000);
        r.YearSeconds.Should().Be(98400);

        AppDetailDay Day(int m, int d) => r.Days.Single(x => x.Date == new DateOnly(2026, m, d));
        (Day(2, 12).Seconds, Day(2, 12).Level).Should().Be((36000, 4));
        (Day(3, 2).Seconds, Day(3, 2).Level).Should().Be((10800, 2));
        (Day(2, 2).Level, Day(2, 11).Level, Day(3, 3).Level).Should().Be((2, 1, 1));
        (Day(3, 3).Seconds, Day(3, 3).Sessions).Should().Be((7200, 1));
        (Day(3, 4).Seconds, Day(3, 4).Sessions).Should().Be((7200, 1)); // the same session carried over midnight
        (Day(2, 3).Kind, Day(2, 3).Level).Should().Be((AppDetailDayKind.Tracked, 0));
    }

    [Fact]
    public void Days_before_the_first_tracked_day_and_after_today_are_outlines()
    {
        var r = Run(_f.Chrome)!;
        r.Days.Single(x => x.Date == new DateOnly(2026, 1, 31)).Kind.Should().Be(AppDetailDayKind.BeforeTracking);
        r.Days.Single(x => x.Date == new DateOnly(2026, 3, 11)).Kind.Should().Be(AppDetailDayKind.Tracked);
        r.Days.Single(x => x.Date == new DateOnly(2026, 3, 12)).Kind.Should().Be(AppDetailDayKind.Future);
        r.Days.Where(x => x.Kind != AppDetailDayKind.Tracked).Should().OnlyContain(x => x.Level == 0 && x.Seconds == 0);
    }

    [Fact]
    public void The_requested_year_is_clamped_to_the_tracked_years()
    {
        var r = Run(_f.Chrome, 2020)!;
        (r.Year, r.FirstYear, r.LastYear).Should().Be((2026, 2026, 2026));
        Run(_f.Chrome, 2031)!.Year.Should().Be(2026);
    }

    [Fact]
    public void Merged_apps_are_one_app_under_the_canonical_id()
    {
        _f.Exec($"UPDATE apps SET canonical_id = {_f.Chrome} WHERE id = {_f.Mus}");
        var r = Run(_f.Chrome)!;
        r.SessionCount.Should().Be(8);
        r.TotalSeconds.Should().Be(98400 + 600);
        Run(_f.Mus).Should().BeNull();
    }

    [Fact]
    public void An_app_with_no_sessions_has_empty_stats()
    {
        var empty = _f.Zed;
        _f.Exec($"DELETE FROM sessions WHERE app_id = {empty}");
        var r = Run(empty)!;
        (r.SessionCount, r.TotalSeconds, r.AverageSessionSeconds, r.Last, r.FirstTracked).Should().Be((0, 0L, 0L, null, null));
        r.Days.Should().OnlyContain(d => d.Kind != AppDetailDayKind.Tracked);
    }

    [Fact]
    public void Retired_and_unknown_apps_have_no_detail()
    {
        Run(9999).Should().BeNull();
        Run(_f.Mus + 1).Should().BeNull(); // the fixture's retired "Old" app is inserted right after Mus
    }
}
