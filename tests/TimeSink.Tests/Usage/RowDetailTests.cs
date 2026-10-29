using TimeSink.Core.Usage;

namespace TimeSink.Tests.Usage;

/// <summary>Decision 149: the expanded Today row's blip rule, stats and live seconds come from Core.</summary>
public sealed class RowDetailTests
{
    private static TodaySession S(long start, long len, bool open = false) => new(start, start + len, open);

    private static TodayAppRow Row(long seconds, double? avg, bool running, params TodaySession[] sessions) =>
        new(1, "Chrome", seconds, running, running ? 0 : null, null, sessions, avg, avg is null ? 0 : 7);

    [Fact]
    public void Sessions_under_a_minute_are_blips_that_are_only_counted()
    {
        var row = Row(3719, 1800, false, S(0, 59), S(100, 60), S(500, 3600));
        var d = RowDetails.Of(row);
        d.Shown.Select(s => s.Seconds).Should().Equal(60, 3600);
        d.Blips.Should().Be(1);
        d.LongestSeconds.Should().Be(3600);
    }

    [Fact]
    public void A_row_of_only_blips_has_no_longest_session()
    {
        var d = RowDetails.Of(Row(30, null, false, S(0, 30)));
        d.Shown.Should().BeEmpty();
        d.Blips.Should().Be(1);
        d.LongestSeconds.Should().Be(0);
    }

    [Fact]
    public void Time_against_the_seven_day_average_is_a_ratio_and_the_dial_fractions_are_of_24_hours()
    {
        var d = RowDetails.Of(Row(5400, 3600, false, S(0, 5400)));
        d.AverageSeconds.Should().Be(3600);
        d.VsAverage!.Value.Percent.Should().BeApproximately(50, 1e-9);
        d.TotalFraction.Should().BeApproximately(5400 / 86400.0, 1e-9);
        d.AverageFraction.Should().BeApproximately(3600 / 86400.0, 1e-9);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    public void No_average_means_no_comparison(double? avg)
    {
        var d = RowDetails.Of(Row(600, avg, false, S(0, 600)));
        d.AverageSeconds.Should().BeNull();
        d.VsAverage.Should().BeNull();
        d.AverageFraction.Should().BeNull();
    }

    [Fact]
    public void Live_seconds_grow_for_running_rows_and_the_combined_time_for_every_open_Session()
    {
        var running = Row(1000, null, true, S(0, 1000, open: true));
        var closed = Row(500, null, false, S(2000, 500));
        var r = new TodayResult(new DateOnly(2026, 3, 11), 0, 86400, 5000, 1500, 2, 2, [], false, [], null, null, 0, [running, closed], 4000, 0, TimeZoneInfo.Utc);

        r.ElapsedAt(5010).Should().Be(10);
        r.ElapsedAt(4000).Should().Be(0);                  // never negative
        r.RowSecondsAt(running, 5010).Should().Be(1010);
        r.RowSecondsAt(closed, 5010).Should().Be(500);
        r.CombinedSecondsAt(5010).Should().Be(1500 + 10 * 2);
        r.UptimeAt(5010, live: true).Should().Be(4010);
        r.UptimeAt(5010, live: false).Should().Be(4000);
    }
}
