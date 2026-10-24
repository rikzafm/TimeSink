namespace TimeSink.Core.Usage;

/// <summary>One app's share of a clicked bar.</summary>
public sealed record BarApp(long AppId, string Name, long Seconds);

/// <summary>One session (clipped to the clicked hour) of a clicked Today bar.</summary>
public sealed record BarSession(string AppName, long StartUtc, long EndUtc)
{
    public long Seconds => Math.Max(0, EndUtc - StartUtc);
}

/// <summary>What a Today hour bar shows when clicked: app-seconds per app, the sessions inside the hour and the typical-day baseline.</summary>
public sealed record HourDetail(
    int Hour,
    long StartUtc,
    long EndUtc,
    long AppSeconds,
    double TypicalAppSeconds,
    IReadOnlyList<BarApp> Apps,
    IReadOnlyList<BarSession> Sessions);

public static class BarDetail
{
    /// <summary>
    /// The detail of wall-clock hour column <paramref name="hour"/> (0-23) of the day in <paramref name="r"/>, cut at local whole
    /// hours the same way the chart is (a repeated hour on a 25 h day holds both passes; a skipped hour is empty).
    /// </summary>
    public static HourDetail ForHour(TodayResult r, int hour, TimeZoneInfo zone)
    {
        var windows = new List<(long S, long E)>();
        for (var t = r.DayStartUtc; t < r.DayEndUtc;)
        {
            var local = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(t), zone);
            var next = Math.Min(t + 3600 - (local.Minute * 60 + local.Second), r.DayEndUtc);
            if (local.Hour == hour) windows.Add((t, next));
            t = next;
        }

        var apps = new List<BarApp>();
        var sessions = new List<BarSession>();
        foreach (var row in r.Apps)
        {
            long secs = 0;
            foreach (var s in row.Sessions)
                foreach (var (ws, we) in windows)
                {
                    var a = Math.Max(s.StartUtc, ws);
                    var b = Math.Min(s.EndUtc, we);
                    if (b <= a) continue;
                    secs += b - a;
                    sessions.Add(new BarSession(row.Name, a, b));
                }
            if (secs > 0) apps.Add(new BarApp(row.AppId, row.Name, secs));
        }

        var bucket = r.Hours[hour];
        return new HourDetail(hour, windows.Count > 0 ? windows[0].S : r.DayStartUtc, windows.Count > 0 ? windows[^1].E : r.DayStartUtc,
            apps.Sum(a => a.Seconds), bucket.TypicalAppSeconds,
            apps.OrderByDescending(a => a.Seconds).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
            sessions.OrderBy(s => s.StartUtc).ToList());
    }

    /// <summary>
    /// The detail of one Week column: its apps by time (merged apps are one row), the average of the seven days in view
    /// (future days excluded) and the same weekday seven days earlier.
    /// </summary>
    public static DayDetail ForDay(string dbPath, DateOnly day, DateTimeOffset now, TimeZoneInfo zone, WeekStart weekStart)
    {
        var week = WeekQuery.Run(dbPath, now, zone, weekStart);
        var thisDay = TodayQuery.Run(dbPath, now, zone, null, day);
        var lastWeek = TodayQuery.Run(dbPath, now, zone, null, day.AddDays(-7));
        var counted = week.Days.Where(d => !d.IsFuture).ToList();
        var average = counted.Count == 0 ? 0 : counted.Sum(d => d.Seconds) / (double)counted.Count;
        return Compose(thisDay, average, lastWeek.UptimeSeconds);
    }

    internal static DayDetail Compose(TodayResult day, double weekAverageSeconds, long sameDayLastWeekSeconds)
    {
        var apps = day.Apps.Select(a => new BarApp(a.AppId, a.Name, a.Seconds)).ToList();
        long? first = day.Apps.SelectMany(a => a.Sessions).Select(s => (long?)s.StartUtc).Min();
        long? last = day.Apps.SelectMany(a => a.Sessions).Select(s => (long?)s.EndUtc).Max();
        return new DayDetail(day.Date, day.UptimeSeconds, first, last, apps, weekAverageSeconds, sameDayLastWeekSeconds);
    }
}

/// <summary>What a Week column shows when clicked. <see cref="TotalSeconds"/> is the day's PC uptime.</summary>
public sealed record DayDetail(
    DateOnly Date,
    long TotalSeconds,
    long? FirstStartUtc,
    long? LastEndUtc,
    IReadOnlyList<BarApp> Apps,
    double WeekAverageSeconds,
    long SameDayLastWeekSeconds);
