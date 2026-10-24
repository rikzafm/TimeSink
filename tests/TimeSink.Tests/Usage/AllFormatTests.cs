using TimeSink.Core.Usage;

namespace TimeSink.Tests.Usage;

public class AllFormatTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(600, "0.2")]       // 10 min
    [InlineData(5400, "1.5")]
    [InlineData(35999, "10")]      // 9.9997 h rounds to 10, shown whole
    [InlineData(36000, "10")]
    [InlineData(98400, "27")]      // 27.33 h
    [InlineData(2196000, "610")]
    public void Hours_show_one_decimal_under_ten_hours_and_whole_hours_above(long seconds, string expected) =>
        AllFormat.Hours(seconds).Should().Be(expected);

    [Theory]
    [InlineData(0, "0.0")]
    [InlineData(5400, "1.5")]
    [InlineData(13846.15, "3.8")]
    [InlineData(82800, "23.0")]
    public void Average_per_day_is_hours_with_one_decimal(double seconds, string expected) =>
        AllFormat.AveragePerDay(seconds).Should().Be(expected);

    [Theory]
    [InlineData(0.0, "0%")]
    [InlineData(0.003333, "<1%")]
    [InlineData(0.01, "1%")]
    [InlineData(0.54666, "55%")]
    [InlineData(1.0, "100%")]
    public void Share_is_a_whole_percent_with_a_floor_for_tiny_shares(double share, string expected) =>
        AllFormat.Share(share).Should().Be(expected);
}
