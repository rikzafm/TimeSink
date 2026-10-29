namespace TimeSink.Core.Usage;

/// <summary>A span of one local day (UTC unix seconds), already clipped to that day.</summary>
public sealed record TodaySession(long StartUtc, long EndUtc, bool IsOpen)
{
    public long Seconds => Math.Max(0, EndUtc - StartUtc);
}

public sealed record SleepBand(long StartUtc, long EndUtc);

/// <summary>One hour column: app-seconds open in that hour (sum over apps) and the typical-day average for it.</summary>
public sealed record HourBucket(int Hour, long AppSeconds, double TypicalAppSeconds);

public sealed record FirstAppTile(long AppId, string AppName, long StartUtc);

public sealed record LongestSessionTile(long AppId, string AppName, long Seconds);

/// <summary>One app row (merged apps are one row under their canonical app).</summary>
public sealed record TodayAppRow(
    long AppId,
    string Name,
    long Seconds,
    bool IsRunning,
    long? RunningSinceUtc,
    long? LastClosedUtc,
    IReadOnlyList<TodaySession> Sessions,
    double? SevenDayAverageSeconds,
    int SevenDayDays);

/// <summary>Everything the Today view shows, computed at <see cref="NowUtc"/> with the day cut in <see cref="Zone"/>.</summary>
public sealed record TodayResult(
    DateOnly Date,
    long DayStartUtc,
    long DayEndUtc,
    long NowUtc,
    long TotalSeconds,
    int RunningCount,
    int OpenSessionCount,
    IReadOnlyList<HourBucket> Hours,
    bool HasTypical,
    IReadOnlyList<SleepBand> SleepBands,
    FirstAppTile? FirstApp,
    LongestSessionTile? Longest,
    long VsYesterdaySeconds,
    IReadOnlyList<TodayAppRow> Apps,
    long UptimeSeconds,
    long UptimeVsYesterdaySeconds,
    TimeZoneInfo Zone,
    DateOnly? FirstTracked = null,
    bool GamesOnly = false)
{
    /// <summary>Seconds from the snapshot to <paramref name="nowUtc"/>, never negative.</summary>
    public long ElapsedAt(long nowUtc) => Math.Max(0, nowUtc - NowUtc);

    /// <summary>
    /// PC uptime at <paramref name="nowUtc"/>: the live day keeps counting after the snapshot, a past day does not. With the
    /// Games filter on it is the time a game is open, so it keeps counting only while one is (decision 151).
    /// </summary>
    public long UptimeAt(long nowUtc, bool live) => UptimeSeconds + (live && (!GamesOnly || OpenSessionCount > 0) ? ElapsedAt(nowUtc) : 0);

    /// <summary>Combined app time at <paramref name="nowUtc"/>: every open Session keeps running (decision 149).</summary>
    public long CombinedSecondsAt(long nowUtc) => TotalSeconds + ElapsedAt(nowUtc) * OpenSessionCount;

    /// <summary>One row's time at <paramref name="nowUtc"/>: a running app keeps counting.</summary>
    public long RowSecondsAt(TodayAppRow row, long nowUtc) => row.Seconds + (row.IsRunning ? ElapsedAt(nowUtc) : 0);
}
