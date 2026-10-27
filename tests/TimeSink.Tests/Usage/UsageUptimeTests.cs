using TimeSink.Core.Usage;

namespace TimeSink.Tests.Usage;

public sealed class UsageUptimeTests
{
    private const long H = 3600;

    [Fact]
    public void A_gap_after_a_shutdown_close_is_not_uptime()
    {
        // On from midnight, shut down at 04:00, booted again at 19:40, now 22:20.
        var segs = new List<UsageSeg>
        {
            new(1, 1, 0, 4 * H, false, true),
            new(1, 1, 19 * H + 2400, 22 * H + 1200, true, false),
        };
        UsageUptime.Seconds(segs, 0, 22 * H + 1200, live: true, onSince: null).Should().Be(4 * H + (22 * H + 1200 - (19 * H + 2400)));
    }
}
