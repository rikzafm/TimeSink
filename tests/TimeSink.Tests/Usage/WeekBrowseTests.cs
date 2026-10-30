using TimeSink.Core.Usage;

namespace TimeSink.Tests.Usage;

/// <summary>Decision 165: where the Week view can go from the week it shows is Core's, so a click needs no arithmetic in the view.</summary>
public sealed class WeekBrowseTests
{
    private static readonly DateOnly Today = new(2026, 3, 11);        // a Wednesday
    private static readonly DateOnly ThisMonday = new(2026, 3, 9);
    private static readonly DateOnly First = new(2025, 11, 20);       // its week starts on Monday 17 November: 16 weeks back

    private static WeekBrowse Showing(int weeksBack, DateOnly? first = null, DayOfWeek weekStart = DayOfWeek.Monday, bool noFirst = false)
    {
        var thisWeek = ThisMonday.AddDays(weekStart == DayOfWeek.Sunday ? -1 : 0);
        var shown = thisWeek.AddDays(-7 * weeksBack);
        var days = Enumerable.Range(0, 7).Select(i => new WeekDay(shown.AddDays(i), 0, false, shown.AddDays(i) > Today)).ToList();
        var totals = Enumerable.Repeat(0L, 20).ToList();
        return new WeekBrowse(new WeekResult(Today, shown, 0, 0, days, [], 0, [], totals, 0, noFirst ? null : first ?? First));
    }

    [Fact]
    public void The_current_week_can_only_go_earlier()
    {
        var b = Showing(0);

        b.WeeksBack.Should().Be(0);
        b.Later().Should().BeNull();
        b.Earlier().Should().Be(1);
    }

    [Fact]
    public void A_week_in_between_steps_both_ways()
    {
        var b = Showing(2);

        b.Earlier().Should().Be(3);
        b.Later().Should().Be(1);
    }

    [Fact]
    public void The_week_of_the_first_tracked_day_cannot_go_earlier()
    {
        var b = Showing(16);

        b.WeeksBack.Should().Be(16);
        b.Earlier().Should().BeNull();
        b.Later().Should().Be(15);
    }

    [Fact]
    public void Without_a_first_tracked_day_there_is_nowhere_earlier()
    {
        Showing(0, noFirst: true).Earlier().Should().BeNull();
    }

    [Fact]
    public void The_weeks_follow_the_Week_start_of_the_week_shown()
    {
        var b = Showing(0, weekStart: DayOfWeek.Sunday);

        b.Earlier().Should().Be(1);
        b.Shows(new DateOnly(2026, 3, 8)).Should().BeTrue();
        b.Shows(new DateOnly(2026, 3, 7)).Should().BeFalse();
    }

    [Fact]
    public void A_trend_bar_is_a_week_counted_back_from_the_current_one_oldest_first()
    {
        var b = Showing(0);

        b.TrendBarStart(19).Should().Be(ThisMonday);
        b.TrendBarStart(18).Should().Be(ThisMonday.AddDays(-7));
        b.TrendBarStart(0).Should().Be(ThisMonday.AddDays(-7 * 19));
        b.TrendBar(10).Should().Be(9);
    }

    [Fact]
    public void Clicking_the_week_already_shown_goes_nowhere_and_a_bar_before_the_first_tracked_week_stops_at_it()
    {
        Showing(3).TrendBar(16).Should().BeNull();   // bar 16 is 3 weeks back
        Showing(0).TrendBar(0).Should().Be(16);      // 19 weeks back lies before the first tracked week
    }

    [Fact]
    public void A_day_in_the_shown_week_opens_at_once()
    {
        var click = Showing(2).OnDayClicked(ThisMonday.AddDays(-14).AddDays(3));

        click.OpenNow.Should().BeTrue();
        click.LoadWeeksBack.Should().BeNull();
    }

    [Fact]
    public void A_day_in_another_week_asks_for_that_week_first()
    {
        var click = Showing(0).OnDayClicked(new DateOnly(2026, 2, 25));   // a Wednesday two weeks back

        click.OpenNow.Should().BeFalse();
        click.LoadWeeksBack.Should().Be(2);
    }

    [Fact]
    public void A_day_before_the_first_tracked_week_asks_for_the_first_week()
    {
        Showing(0).OnDayClicked(new DateOnly(2025, 1, 1)).LoadWeeksBack.Should().Be(16);
    }
}
