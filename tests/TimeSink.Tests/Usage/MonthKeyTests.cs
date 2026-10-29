using TimeSink.Core.Usage;

namespace TimeSink.Tests.Usage;

public sealed class MonthKeyTests
{
    [Fact]
    public void A_month_is_taken_from_a_day()
    {
        MonthKey.Of(new DateOnly(2026, 3, 31)).Should().Be(new MonthKey(2026, 3));
        new MonthKey(2026, 3).First.Should().Be(new DateOnly(2026, 3, 1));
    }

    [Theory]
    [InlineData(2026, 1, -1, 2025, 12)]   // January back to December
    [InlineData(2025, 12, 1, 2026, 1)]    // December on to January
    [InlineData(2026, 3, -2, 2026, 1)]
    [InlineData(2026, 3, 0, 2026, 3)]
    [InlineData(2026, 11, 14, 2028, 1)]
    public void Months_add_across_year_ends(int year, int month, int by, int toYear, int toMonth) =>
        new MonthKey(year, month).AddMonths(by).Should().Be(new MonthKey(toYear, toMonth));

    [Fact]
    public void Months_order_by_year_then_month_and_count_the_distance_between()
    {
        (new MonthKey(2025, 12) < new MonthKey(2026, 1)).Should().BeTrue();
        (new MonthKey(2026, 3) >= new MonthKey(2026, 3)).Should().BeTrue();
        new MonthKey(2026, 2).MonthsSince(new MonthKey(2025, 11)).Should().Be(3);
        new MonthKey(2026, 1).Prev.Should().Be(new MonthKey(2025, 12));
    }
}
