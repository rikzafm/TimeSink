using TimeSink.Core.Settings;
using TimeSink.Core.Theming;
using TimeSink.Core.Usage;

namespace TimeSink.Tests.Usage;

public sealed class YearQueryTests : IDisposable
{
    private const long H = 3600;
    private readonly YearFixture _f = new();
    public void Dispose() => _f.Dispose();

    private YearResult Run() => Q.Year(_f.DbPath, YearFixture.Now, TimeZoneInfo.Utc);

    [Fact]
    public void Twelve_columns_with_tracked_hours_and_months_before_tracking_empty()
    {
        var r = Run();
        r.Year.Should().Be(2026);
        r.Months.Should().HaveCount(12);
        r.Months[0].Kind.Should().Be(YearMonthKind.BeforeTracking);
        r.Months[0].Seconds.Should().Be(0);
        r.Months.Skip(1).Take(4).Select(m => m.Seconds).Should().Equal(10 * H, 30 * H, 36 * H, 20 * H); // Feb..May; Old excluded
        r.Months.Skip(1).Take(3).Select(m => m.Kind).Should().OnlyContain(k => k == YearMonthKind.Tracked);
        r.Months[4].Kind.Should().Be(YearMonthKind.Current);
        r.TotalSeconds.Should().Be(96 * H);
        r.TrackingStarted.Should().Be(new DateOnly(2026, 2, 15));
    }

    [Fact]
    public void Current_month_projects_from_days_elapsed_and_future_months_ghost_at_the_last_three_months_average()
    {
        var r = Run();
        r.Months[4].ProjectedSeconds.Should().Be(62 * H); // 20 h / 10 days * 31
        // Mar 30 + Apr 36 + May projected 62 = 128 h / 3
        r.RecentMonthCount.Should().Be(3);
        foreach (var m in r.Months.Skip(5))
        {
            m.Kind.Should().Be(YearMonthKind.Future);
            m.Seconds.Should().Be(0);
            m.ProjectedSeconds.Should().Be(128 * H / 3);
        }
    }

    [Fact]
    public void Average_line_uses_full_tracked_months_only_and_banner_inputs_follow()
    {
        var r = Run();
        r.AverageMonthSeconds.Should().Be(33 * H); // Mar, Apr (Feb began mid-month; May is current)
        r.BestMonth.Should().Be(4);
        r.BestMonthSeconds.Should().Be(36 * H);
        // 96 so far + (62 - 20) rest of May + 7 ghosts of 128/3 h = 436.667 h
        r.PaceYearSeconds.Should().Be(96 * H + 42 * H + 7 * (128 * H / 3));
    }

    [Fact]
    public void Month_panel_has_top_apps_and_delta_versus_previous_month()
    {
        var r = Run();
        var mar = r.Months[2];
        mar.TopApps.Select(a => (a.Name, a.Seconds)).Should().Equal(("Chrome", 20 * H), ("Code", 10 * H));
        mar.DeltaPercent.Should().BeApproximately(200, 0.001); // vs Feb 10 h
        r.Months[3].DeltaPercent.Should().BeApproximately(20, 0.001);
        r.Months[3].TopApps.Select(a => a.Name).Should().Equal("Chrome", "Code");
        r.Months[1].DeltaPercent.Should().BeNull(); // January was before tracking
        r.Months[4].DeltaPercent.Should().BeApproximately(100.0 * 62 / 36 - 100, 0.001); // projected vs Apr
        r.Months[8].TopApps.Should().BeEmpty();
    }

    [Fact]
    public void No_tracked_apps_leaves_every_month_empty_with_no_pace()
    {
        using var empty = new YearFixture(seed: false);
        var r = Q.Year(empty.DbPath, YearFixture.Now, TimeZoneInfo.Utc);
        r.Months.Should().OnlyContain(m => m.Kind == YearMonthKind.BeforeTracking);
        r.PaceYearSeconds.Should().BeNull();
        r.AverageMonthSeconds.Should().BeNull();
        r.TrackingStarted.Should().BeNull();
    }

    [Fact]
    public void Tracking_that_began_this_month_paces_from_the_current_month_alone()
    {
        using var f = new YearFixture(seed: false);
        var app = f.AddApp(@"c:\x.exe", "X", YearFixture.U(5, 6, 0));
        f.Session(app, YearFixture.U(5, 6, 0), YearFixture.U(5, 8, 0));
        var r = Q.Year(f.DbPath, YearFixture.Now, TimeZoneInfo.Utc);
        r.RecentMonthCount.Should().Be(1);
        r.AverageMonthSeconds.Should().BeNull(); // no full tracked month yet
        // 48 h over the 5 days since tracking began (May 6..10) -> 9.6 h/day * 31
        r.Months[4].ProjectedSeconds.Should().Be((long)(48 * H / 5.0 * 31));
        r.Months[5].ProjectedSeconds.Should().Be(r.Months[4].ProjectedSeconds);
    }

    [Fact]
    public void Sub_view_setting_defaults_to_pace_and_parses_stored_values()
    {
        YearSubViews.Parse(null).Should().Be(YearSubView.Pace);
        YearSubViews.Parse("garbage").Should().Be(YearSubView.Pace);
        YearSubViews.Parse("Matrix").Should().Be(YearSubView.Matrix);
        Setting.LastYearView.Key.Should().Be("ui.lastYearView");
    }
}
