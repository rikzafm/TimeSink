using TimeSink.Core.Usage;

namespace TimeSink.Tests.Usage;

/// <summary>With the Games filter on, a day's total is the time a game is open, not the PC's uptime (decision 151).</summary>
public sealed class GamesUptimeTests
{
    private static UsageSeg Seg(long app, long start, long end, bool open = false) => new(app, app, start, end, open, false);

    [Fact]
    public void Overlapping_games_count_once_and_gaps_count_nothing()
    {
        List<UsageSeg> segs = [Seg(1, 100, 400), Seg(2, 300, 500), Seg(3, 900, 1000)];

        UsageUptime.OpenSeconds(segs, 0, 2000).Should().Be(400 + 100);
    }

    [Fact]
    public void Open_time_is_clipped_to_the_range()
    {
        List<UsageSeg> segs = [Seg(1, 50, 400)];

        UsageUptime.OpenSeconds(segs, 100, 300).Should().Be(200);
    }

    [Fact]
    public void Games_mode_ignores_sign_in_and_the_span_between_sessions()
    {
        List<UsageSeg> segs = [Seg(1, 1000, 1100), Seg(1, 5000, 5100)];

        UsageUptime.Seconds(segs, 0, 10000, live: false, onSince: 10, gamesOnly: true).Should().Be(200);
        UsageUptime.Seconds(segs, 0, 10000, live: false, onSince: 10).Should().Be(5100 - 10);
    }

    [Fact]
    public void Games_mode_gives_a_live_day_with_no_game_nothing()
    {
        UsageUptime.PerDay([], TimeZoneInfo.Utc, 50_000, onSinceUtc: 1_000, gamesOnly: true).Should().BeEmpty();
    }

    [Fact]
    public void A_live_games_day_stops_counting_when_no_game_is_open()
    {
        var none = new TodayResult(new DateOnly(2026, 3, 11), 0, 86400, 1000, 0, 0, 0, [], false, [], null, null, 0, [], 600, 0, null, GamesOnly: true);
        var open = none with { OpenSessionCount = 1 };

        none.UptimeAt(1060, live: true).Should().Be(600);
        open.UptimeAt(1060, live: true).Should().Be(660);
        (none with { GamesOnly = false }).UptimeAt(1060, live: true).Should().Be(660);
    }
}
