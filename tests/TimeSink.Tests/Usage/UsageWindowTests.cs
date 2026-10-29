using TimeSink.Core.Apps;
using TimeSink.Core.Usage;

namespace TimeSink.Tests.Usage;

/// <summary>The second adapter of <see cref="IUsageSource"/>: apps and sessions held in memory, counting how often sessions are read.</summary>
internal sealed class InMemoryUsageSource : IUsageSource
{
    private readonly List<GroupedApp> _apps = [];
    private readonly List<RawSession> _sessions = [];

    public int SessionReads { get; private set; }

    public InMemoryUsageSource App(long id, string name, long addedUtc, long? main = null)
    {
        _apps.Add(new GroupedApp(id, main ?? id, name, addedUtc));
        return this;
    }

    public InMemoryUsageSource Session(long app, long start, long? end, long? lastSeen = null, string? reason = null)
    {
        _sessions.Add(new RawSession(app, start, end, lastSeen ?? end ?? start, reason ?? (end is null ? null : "exit")));
        return this;
    }

    public AppGroups Apps(bool gamesOnly)
    {
        var groups = AppGroups.From(_apps);
        return gamesOnly ? groups.OnlyGames() : groups;
    }

    public IEnumerable<RawSession> Sessions(long from, long nowUtc, long? to)
    {
        SessionReads++;
        return _sessions.Where(s => s.StartUtc <= nowUtc && (to is null || s.StartUtc < to) && (s.EndUtc is null || s.EndUtc >= from))
            .OrderBy(s => s.StartUtc).ToList();
    }
}

public sealed class UsageWindowTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 11, 14, 20, 0, TimeSpan.Zero); // Wed
    private static long At(int day, int hour, int minute = 0) => new DateTimeOffset(2026, 3, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    private static UsageContext Context(IReadOnlyCollection<long>? running = null, bool gamesOnly = false) =>
        new(Now, TimeZoneInfo.Utc, running, () => null, () => WeekStart.Mon, gamesOnly);

    private static InMemoryUsageSource Source() =>
        new InMemoryUsageSource()
            .App(1, "Chrome", At(1, 0))
            .Session(1, At(9, 9), At(9, 11))      // Mon
            .Session(1, At(2, 10), At(2, 11))     // the Monday before
            .Session(1, At(11, 11), null, lastSeen: At(11, 14, 19)); // open since Wed 11:00

    [Fact]
    public void A_day_detail_reads_the_sessions_once_for_the_week_and_both_days()
    {
        var src = Source();
        var detail = DayDetailQuery.Run(new UsageWindow(src, Context([])), new DateOnly(2026, 3, 9));

        src.SessionReads.Should().Be(1);
        detail.TotalSeconds.Should().Be(7200);
        detail.SameDayLastWeekSeconds.Should().Be(3600);
        detail.Apps.Select(a => (a.Name, a.Seconds)).Should().Equal(("Chrome", 7200L));
    }

    [Fact]
    public void Today_counts_an_open_session_to_now_when_the_app_is_running()
    {
        var running = TodayQuery.Run(new UsageWindow(Source(), Context([1])));
        var notRunning = TodayQuery.Run(new UsageWindow(Source(), Context([])));

        running.Apps.Single().Seconds.Should().Be(At(11, 14, 20) - At(11, 11));
        notRunning.Apps.Single().Seconds.Should().Be(At(11, 14, 19) - At(11, 11)); // up to the last heartbeat
    }

    [Fact]
    public void A_nearer_start_is_answered_from_memory_and_an_earlier_one_reads_again()
    {
        var src = Source();
        var window = new UsageWindow(src, Context());

        window.Segments(At(1, 0));
        window.Segments(At(9, 0)).Should().ContainSingle(s => s.Start == At(9, 9)).And.HaveCount(2); // the Monday session and the open one
        src.SessionReads.Should().Be(1);

        window.Segments(At(1, 0) - 86400);
        src.SessionReads.Should().Be(2);
    }

    [Fact]
    public void The_games_filter_leaves_the_apps_of_other_kinds_out()
    {
        new UsageWindow(Source(), Context(gamesOnly: true)).Apps.Count.Should().Be(0);
        new UsageWindow(Source(), Context()).Apps.Count.Should().Be(1);
    }
}
