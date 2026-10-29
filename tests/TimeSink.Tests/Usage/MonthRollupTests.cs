using TimeSink.Core.Usage;

namespace TimeSink.Tests.Usage;

public sealed class MonthRollupTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static long At(int y, int m, int d, int h) => new DateTimeOffset(y, m, d, h, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    private static UsageSeg Seg(long group, long start, long end) => new(group, group, start, end, false, false);

    [Fact]
    public void A_session_over_a_month_end_counts_on_each_side()
    {
        var rollup = new MonthRollup([Seg(1, At(2026, 3, 31, 23), At(2026, 4, 1, 2))], Utc, At(2026, 4, 5, 0), null, false);

        rollup.AppSeconds(new MonthKey(2026, 3)).Should().Be(3600);
        rollup.AppSeconds(new MonthKey(2026, 4)).Should().Be(7200);
        rollup.Groups(new MonthKey(2026, 4)).Should().Equal(new Dictionary<long, long> { [1] = 7200 });
    }

    [Fact]
    public void Apps_are_summed_per_group_and_a_month_without_time_is_empty()
    {
        var rollup = new MonthRollup([Seg(1, At(2026, 3, 2, 9), At(2026, 3, 2, 10)), Seg(2, At(2026, 3, 3, 9), At(2026, 3, 3, 11)),
            Seg(1, At(2026, 3, 9, 9), At(2026, 3, 9, 10))], Utc, At(2026, 4, 5, 0), null, false);

        rollup.Groups(new MonthKey(2026, 3)).Should().Equal(new Dictionary<long, long> { [1] = 7200, [2] = 7200 });
        rollup.AppSeconds(new MonthKey(2026, 3)).Should().Be(14400);
        rollup.Groups(new MonthKey(2026, 2)).Should().BeEmpty();
        rollup.AppSeconds(new MonthKey(2026, 2)).Should().Be(0);
    }

    [Fact]
    public void Uptime_is_kept_apart_from_app_seconds()
    {
        // Two apps open at once for an hour: app-seconds count both, uptime counts the PC being on once.
        var rollup = new MonthRollup([Seg(1, At(2026, 3, 2, 9), At(2026, 3, 2, 10)), Seg(2, At(2026, 3, 2, 9), At(2026, 3, 2, 10))],
            Utc, At(2026, 4, 5, 0), null, false);

        rollup.AppSeconds(new MonthKey(2026, 3)).Should().Be(7200);
        rollup.Uptime(new MonthKey(2026, 3)).Should().Be(3600);
    }
}
