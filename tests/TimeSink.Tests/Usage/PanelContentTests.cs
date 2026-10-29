using TimeSink.Core.Usage;
using static TimeSink.Tests.Usage.TodayFixture;

namespace TimeSink.Tests.Usage;

/// <summary>Decision 157: what the Today hour panel and the Week day panel say is decided in Core, by app id.</summary>
public sealed class PanelContentTests : IDisposable
{
    private readonly TodayFixture _f = new();
    public void Dispose() => _f.Dispose();

    private static DayDetail Day(long total, double weekAverage, long lastWeek, long? first = 100, long? last = 900, params BarApp[] apps) =>
        new(new DateOnly(2026, 3, 9), total, first, last, apps, weekAverage, lastWeek);

    [Fact]
    public void A_focus_app_moves_to_the_front_and_is_found_by_id_not_by_name()
    {
        var detail = Day(7200, 3600, 3600, 100, 900, new BarApp(1, "Chrome", 3000), new BarApp(2, "Chrome", 4200), new BarApp(3, "Code", 0));

        var content = PanelContents.ForDay(detail, (2, "Chrome"));

        content.Apps.Select(a => a.AppId).Should().Equal(2, 1, 3);
        content.Focus.Should().Be(new PanelFocus(2, "Chrome", 4200));
    }

    [Fact]
    public void A_focus_app_that_was_not_used_that_day_has_no_time_and_changes_no_order()
    {
        var detail = Day(3000, 3000, 3000, 100, 900, new BarApp(1, "Chrome", 3000));

        var content = PanelContents.ForDay(detail, (9, "Steam"));

        content.Focus.Should().Be(new PanelFocus(9, "Steam", 0));
        content.Apps.Select(a => a.AppId).Should().Equal(1);
    }

    [Fact]
    public void Without_a_focus_the_apps_keep_their_order()
    {
        var content = PanelContents.ForDay(Day(0, 0, 0, null, null, new BarApp(2, "B", 5), new BarApp(1, "A", 4)));

        content.Focus.Should().BeNull();
        content.Apps.Select(a => a.AppId).Should().Equal(2, 1);
    }

    [Fact]
    public void The_day_comparisons_are_against_the_week_average_and_the_same_day_last_week()
    {
        var content = PanelContents.ForDay(Day(7200, 3600.4, 9000));

        content.Comparisons.Should().Equal(
            new PanelComparison(PanelComparisonKind.VsWeekAverage, 3600),
            new PanelComparison(PanelComparisonKind.VsSameDayLastWeek, -1800));
        content.Comparisons.Select(c => c.Good).Should().Equal(true, false);
    }

    [Fact]
    public void A_zero_difference_counts_as_good()
    {
        PanelContents.ForDay(Day(3600, 3600, 3600)).Comparisons.Should().OnlyContain(c => c.Good && c.DeltaSeconds == 0);
    }

    [Theory]
    [InlineData(100L, 900L, true)]
    [InlineData(null, 900L, false)]
    [InlineData(100L, null, false)]
    public void The_active_span_needs_both_ends(long? first, long? last, bool shown)
    {
        var content = PanelContents.ForDay(Day(0, 0, 0, first, last));

        (content.SpanStartUtc is not null && content.SpanEndUtc is not null).Should().Be(shown);
        (content.SpanStartUtc is null).Should().Be(content.SpanEndUtc is null);
    }

    [Fact]
    public void The_hour_panel_carries_the_hours_apps_sessions_and_a_typical_line_only_with_a_baseline()
    {
        var r = Q.Today(_f.DbPath, Now, TimeZoneInfo.Utc);

        var content = PanelContents.ForHour(r, 9);

        var detail = BarDetail.ForHour(r, 9);
        content.TotalSeconds.Should().Be(detail.AppSeconds);
        content.SessionCount.Should().Be(detail.Sessions.Count);
        content.Apps.Should().Equal(detail.Apps);
        content.Sessions.Should().Equal(detail.Sessions);
        content.SpanStartUtc.Should().BeNull();
        content.Comparisons.Should().HaveCount(r.HasTypical ? 1 : 0);
        foreach (var c in content.Comparisons)
        {
            c.Kind.Should().Be(PanelComparisonKind.TypicalForHour);
            c.DeltaSeconds.Should().Be((long)Math.Round(detail.AppSeconds - detail.TypicalAppSeconds));
            c.ReferenceSeconds.Should().Be((long)Math.Round(detail.TypicalAppSeconds));
        }
    }

    [Fact]
    public void Sessions_and_the_first_app_tile_carry_the_app_id()
    {
        var r = Q.Today(_f.DbPath, Now, TimeZoneInfo.Utc);
        var ids = r.Apps.ToDictionary(a => a.Name, a => a.AppId);

        var detail = BarDetail.ForHour(r, 9);

        detail.Sessions.Should().NotBeEmpty().And.OnlyContain(s => ids[s.AppName] == s.AppId);
        r.FirstApp.Should().NotBeNull();
        r.FirstApp!.AppId.Should().Be(ids[r.FirstApp.AppName]);
    }
}
