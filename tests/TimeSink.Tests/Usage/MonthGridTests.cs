using TimeSink.Core.Usage;

namespace TimeSink.Tests.Usage;

public sealed class MonthGridTests
{
    // March 2026 starts on a Sunday and has 31 days.
    [Theory]
    [InlineData(DayOfWeek.Monday, 6)]
    [InlineData(DayOfWeek.Sunday, 0)]
    [InlineData(DayOfWeek.Saturday, 1)]
    public void Lead_is_the_number_of_blank_cells_before_the_1st(DayOfWeek weekStart, int lead)
    {
        MonthGrid.Layout(2026, 3, weekStart).Lead.Should().Be(lead);
    }

    [Fact]
    public void Weekdays_run_seven_columns_from_the_week_start()
    {
        MonthGrid.Layout(2026, 3, DayOfWeek.Saturday).Weekdays.Should().Equal(
            DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday,
            DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday);
    }

    [Fact]
    public void Weeks_pad_both_ends_with_null_and_hold_every_date_once()
    {
        var grid = MonthGrid.Layout(2026, 3, DayOfWeek.Monday);

        grid.Weeks.Should().HaveCount(6).And.OnlyContain(w => w.Count == 7);
        grid.Weeks[0].Take(6).Should().OnlyContain(d => d == null);
        grid.Weeks[0][6].Should().Be(new DateOnly(2026, 3, 1));
        grid.Weeks[^1].Take(2).Should().Equal(new DateOnly(2026, 3, 30), new DateOnly(2026, 3, 31));
        grid.Weeks[^1].Skip(2).Should().OnlyContain(d => d == null);
        grid.Weeks.SelectMany(w => w).Where(d => d != null).Select(d => d!.Value.Day).Should().Equal(Enumerable.Range(1, 31));
    }

    [Fact]
    public void A_month_that_starts_on_the_week_start_has_no_lead_and_no_extra_row()
    {
        var grid = MonthGrid.Layout(2026, 2, DayOfWeek.Sunday); // 1 February 2026 is a Sunday, 28 days

        grid.Lead.Should().Be(0);
        grid.Weeks.Should().HaveCount(4);
        grid.Days.Should().Be(28);
    }

    [Fact]
    public void Leap_february_has_29_days()
    {
        MonthGrid.Layout(2024, 2, DayOfWeek.Monday).Days.Should().Be(29);
    }

    [Theory]
    [InlineData("2026-03-11", DayOfWeek.Monday, 2, "2026-03-09")]
    [InlineData("2026-03-11", DayOfWeek.Sunday, 3, "2026-03-08")]
    [InlineData("2026-03-09", DayOfWeek.Monday, 0, "2026-03-09")]
    public void WeekStarts_finds_the_first_day_of_the_week_holding_a_day(string day, DayOfWeek weekStart, int into, string start)
    {
        WeekStarts.DaysIntoWeek(DateOnly.Parse(day), weekStart).Should().Be(into);
        WeekStarts.Containing(DateOnly.Parse(day), weekStart).Should().Be(DateOnly.Parse(start));
    }
}
