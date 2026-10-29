using TimeSink.Core.Usage;

namespace TimeSink.Tests.Usage;

public sealed class PeriodNavigatorTests
{
    private static readonly DateOnly First = new(2025, 11, 20);
    private static readonly DateOnly Today = new(2026, 3, 11); // a Wednesday

    private static PeriodNavigator Nav(PeriodUnit unit, DayOfWeek weekStart = DayOfWeek.Monday) => new(unit, First, Today, weekStart);

    [Theory]
    [InlineData("2025-01-01", "2025-11-20")]
    [InlineData("2025-11-19", "2025-11-20")]
    [InlineData("2025-11-20", "2025-11-20")]
    [InlineData("2026-01-15", "2026-01-15")]
    [InlineData("2026-03-11", "2026-03-11")]
    [InlineData("2026-03-12", "2026-03-11")]
    [InlineData("2030-01-01", "2026-03-11")]
    public void Clamp_keeps_a_day_between_the_first_tracked_day_and_today(string day, string expected)
    {
        Nav(PeriodUnit.Day).Clamp(DateOnly.Parse(day)).Should().Be(DateOnly.Parse(expected));
    }

    [Fact]
    public void Contains_is_true_from_the_first_tracked_day_to_today_inclusive()
    {
        var nav = Nav(PeriodUnit.Day);

        nav.Contains(First).Should().BeTrue();
        nav.Contains(Today).Should().BeTrue();
        nav.Contains(First.AddDays(-1)).Should().BeFalse();
        nav.Contains(Today.AddDays(1)).Should().BeFalse();
    }

    [Fact]
    public void A_first_day_after_today_counts_as_today()
    {
        var nav = new PeriodNavigator(PeriodUnit.Day, Today.AddDays(5), Today);

        nav.First.Should().Be(Today);
        nav.CanPrev(Today).Should().BeFalse();
        nav.CanNext(Today).Should().BeFalse();
    }

    [Fact]
    public void A_first_day_equal_to_today_leaves_nowhere_to_go()
    {
        var nav = new PeriodNavigator(PeriodUnit.Month, Today, Today);

        nav.CanPrev(Today).Should().BeFalse();
        nav.CanNext(Today).Should().BeFalse();
    }

    [Fact]
    public void Day_steps_stop_at_the_first_tracked_day_and_at_today()
    {
        var nav = Nav(PeriodUnit.Day);

        nav.CanPrev(First).Should().BeFalse();
        nav.CanPrev(First.AddDays(1)).Should().BeTrue();
        nav.Step(First.AddDays(1), -1).Should().Be(First);
        nav.CanNext(Today).Should().BeFalse();
        nav.CanNext(Today.AddDays(-1)).Should().BeTrue();
        nav.Step(Today.AddDays(-1), 1).Should().Be(Today);
    }

    [Fact]
    public void Month_steps_stop_at_the_months_of_the_first_tracked_day_and_of_today()
    {
        var nav = Nav(PeriodUnit.Month);

        nav.CanPrev(new DateOnly(2025, 11, 1)).Should().BeFalse();
        nav.CanPrev(new DateOnly(2025, 12, 1)).Should().BeTrue();
        nav.CanNext(new DateOnly(2026, 3, 1)).Should().BeFalse();
        nav.CanNext(new DateOnly(2026, 2, 1)).Should().BeTrue();
        nav.Step(new DateOnly(2026, 1, 31), 1).Should().Be(new DateOnly(2026, 2, 28));
        nav.Step(new DateOnly(2026, 1, 15), -1).Should().Be(new DateOnly(2025, 12, 15));
    }

    [Fact]
    public void Year_steps_stop_at_the_years_of_the_first_tracked_day_and_of_today()
    {
        var nav = Nav(PeriodUnit.Year);

        nav.CanPrev(new DateOnly(2025, 6, 1)).Should().BeFalse();
        nav.CanPrev(new DateOnly(2026, 1, 1)).Should().BeTrue();
        nav.CanNext(new DateOnly(2026, 6, 1)).Should().BeFalse();
        nav.CanNext(new DateOnly(2025, 6, 1)).Should().BeTrue();
        nav.Step(new DateOnly(2026, 2, 28), -1).Should().Be(new DateOnly(2025, 2, 28));
    }

    [Theory]
    [InlineData(DayOfWeek.Monday, "2026-03-09")]
    [InlineData(DayOfWeek.Sunday, "2026-03-08")]
    [InlineData(DayOfWeek.Saturday, "2026-03-07")]
    public void A_week_starts_on_the_chosen_first_day_of_the_week(DayOfWeek weekStart, string expected)
    {
        Nav(PeriodUnit.Week, weekStart).StartOf(Today).Should().Be(DateOnly.Parse(expected));
    }

    [Fact]
    public void Week_steps_move_seven_days_and_stop_at_the_weeks_of_the_first_tracked_day_and_of_today()
    {
        var nav = Nav(PeriodUnit.Week); // First is a Thursday: its week starts Mon 2025-11-17

        nav.CanPrev(new DateOnly(2025, 11, 17)).Should().BeFalse();
        nav.CanPrev(new DateOnly(2025, 11, 24)).Should().BeTrue();
        nav.CanNext(Today).Should().BeFalse();
        nav.CanNext(Today.AddDays(-7)).Should().BeTrue();
        nav.Step(Today, -1).Should().Be(new DateOnly(2026, 3, 4));
    }
}
