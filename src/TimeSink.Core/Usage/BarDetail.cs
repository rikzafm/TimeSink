namespace TimeSink.Core.Usage;

/// <summary>One app's share of a clicked bar.</summary>
public sealed record BarApp(long AppId, string Name, long Seconds);

/// <summary>One session (clipped to the clicked hour) of a clicked Today bar.</summary>
public sealed record BarSession(long AppId, string AppName, long StartUtc, long EndUtc)
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
    /// The detail of wall-clock hour column <paramref name="hour"/> (0-23) of the day in <paramref name="r"/>, cut in its zone at local whole
    /// hours the same way the chart is (a repeated hour on a 25 h day holds both passes; a skipped hour is empty).
    /// </summary>
    public static HourDetail ForHour(TodayResult r, int hour)
    {
        var windows = UsageDays.HourPieces(r.DayStartUtc, r.DayEndUtc, r.Zone)
            .Where(p => p.Hour == hour).Select(p => (S: p.Start, E: p.End)).ToList();

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
                    sessions.Add(new BarSession(row.AppId, row.Name, a, b));
                }
            if (secs > 0) apps.Add(new BarApp(row.AppId, row.Name, secs));
        }

        var bucket = r.Hours[hour];
        return new HourDetail(hour, windows.Count > 0 ? windows[0].S : r.DayStartUtc, windows.Count > 0 ? windows[^1].E : r.DayStartUtc,
            apps.Sum(a => a.Seconds), bucket.TypicalAppSeconds,
            apps.OrderByDescending(a => a.Seconds).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
            sessions.OrderBy(s => s.StartUtc).ToList());
    }
}
