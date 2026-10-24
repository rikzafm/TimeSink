namespace TimeSink.Core.Usage;

/// <summary>A span of one local day (UTC unix seconds), already clipped to that day.</summary>
public sealed record TodaySession(long StartUtc, long EndUtc, bool IsOpen)
{
    public long Seconds => Math.Max(0, EndUtc - StartUtc);
}

public sealed record SleepBand(long StartUtc, long EndUtc);

/// <summary>One hour column: app-seconds open in that hour (sum over apps) and the typical-day average for it.</summary>
public sealed record HourBucket(int Hour, long AppSeconds, double TypicalAppSeconds);

public sealed record FirstAppTile(string AppName, long StartUtc);

public sealed record LongestSessionTile(string AppName, long Seconds);

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

/// <summary>Everything the Today view shows, computed at <see cref="NowUtc"/>.</summary>
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
    IReadOnlyList<TodayAppRow> Apps);
