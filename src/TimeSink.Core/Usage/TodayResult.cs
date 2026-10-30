namespace TimeSink.Core.Usage;

/// <summary>
/// A span of one local day (UTC unix seconds), already clipped to that day. <paramref name="BeganUtc"/> is where the whole
/// Session really started when that was before the day began (a carry-over from the day before); null otherwise.
/// </summary>
public sealed record TodaySession(long StartUtc, long EndUtc, bool IsOpen, long? BeganUtc = null)
{
    /// <summary>Time inside this day.</summary>
    public long Seconds => Math.Max(0, EndUtc - StartUtc);

    public bool CarriesOver => BeganUtc is { } b && b < StartUtc;

    /// <summary>The Session's start, including the part before this day.</summary>
    public long FullStartUtc => CarriesOver ? BeganUtc!.Value : StartUtc;

    /// <summary>The whole Session's length, including the part before this day.</summary>
    public long FullSeconds => Math.Max(0, EndUtc - FullStartUtc);
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
    /// <summary>
    /// Position of <paramref name="utc"/> in the day as a wall-clock fraction from 0 to 1 (the hour columns are wall-clock
    /// hours, so a DST day is still 24 columns); a time outside the day clamps to its edge.
    /// </summary>
    public double DayFraction(long utc)
    {
        var wall = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(utc), Zone).DateTime;
        return Math.Clamp((wall - Date.ToDateTime(TimeOnly.MinValue)).TotalSeconds / 86400.0, 0, 1);
    }

    /// <summary>
    /// Where an instant before the day started falls on the 24 h dial, as the same wall-clock position it had on its own day
    /// (22:10 the evening before is 0.924); one more than a day back clamps to 0, the whole ring.
    /// </summary>
    public double DayFractionBefore(long utc)
    {
        var wall = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(utc), Zone).DateTime;
        return Math.Clamp(1 + (wall - Date.ToDateTime(TimeOnly.MinValue)).TotalSeconds / 86400.0, 0, 1);
    }

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
