namespace TimeSink.Core.Usage;

/// <summary>An active uptime interval in UTC seconds.</summary>
public readonly record struct UptimeInterval(long StartUtc, long EndUtc);

/// <summary>Breakdown of uptime across devices and overlap.</summary>
public sealed record DeviceUptimeBreakdown(
    long UnionSeconds,
    IReadOnlyDictionary<string, long> DeviceSeconds,
    long OnBothSeconds,
    IReadOnlyDictionary<string, List<UptimeInterval>> DeviceIntervals,
    List<UptimeInterval> UnionIntervals);

/// <summary>
/// PC uptime as the usage queries see it (decision 125, 168): nothing records the PC being on except tracked sessions, so a
/// finished day runs from its first tracked session to its last, a live day to now, and sleep bands are subtracted.
/// Across multiple devices, uptime intervals are unioned so an hour both devices were on counts once.
/// </summary>
internal static class UsageUptime
{
    /// <summary>
    /// Seconds the PC was on within [<paramref name="from"/>, <paramref name="to"/>), sleep excluded. Overlap across
    /// multiple devices is counted once.
    /// </summary>
    public static long Seconds(List<UsageSeg> segs, long from, long to, bool live, long? onSince, bool gamesOnly = false, string? localDeviceId = null) =>
        Breakdown(segs, from, to, live, onSince, gamesOnly, localDeviceId).UnionSeconds;

    /// <summary>
    /// Computes the uptime intervals for a single device within [<paramref name="from"/>, <paramref name="to"/>), sleep excluded.
    /// </summary>
    public static List<UptimeInterval> Intervals(List<UsageSeg> segs, long from, long to, bool live, long? onSince, bool gamesOnly = false)
    {
        if (gamesOnly)
        {
            var raw = new List<UptimeInterval>();
            foreach (var s in segs.Where(s => s.End > from && s.Start < to))
            {
                var st = Math.Max(s.Start, from);
                var en = Math.Min(s.End, to);
                if (en > st) raw.Add(new UptimeInterval(st, en));
            }
            return Union(raw);
        }

        var inRange = segs.Where(s => s.End > from && s.Start < to).ToList();
        long start;
        if (inRange.Count > 0) start = Math.Max(from, Math.Min(onSince ?? long.MaxValue, inRange.Min(s => s.Start)));
        else if (onSince is { } on) start = Math.Max(from, on);
        else return [];

        var end = live ? to : inRange.Count > 0 ? Math.Min(to, inRange.Max(s => s.End)) : start;
        if (end <= start) return [];

        var bands = InactiveBands(segs, start, end);
        var intervals = new List<UptimeInterval>();
        var cursor = start;
        foreach (var b in bands.OrderBy(b => b.StartUtc))
        {
            var bStart = Math.Max(start, b.StartUtc);
            var bEnd = Math.Min(end, b.EndUtc);
            if (bEnd <= bStart) continue;

            if (bStart > cursor)
            {
                intervals.Add(new UptimeInterval(cursor, bStart));
            }
            cursor = Math.Max(cursor, bEnd);
        }

        if (cursor < end)
        {
            intervals.Add(new UptimeInterval(cursor, end));
        }
        return intervals;
    }

    /// <summary>
    /// Unions overlapping or adjacent intervals, sorted by start time.
    /// </summary>
    public static List<UptimeInterval> Union(IEnumerable<UptimeInterval> intervals)
    {
        var sorted = intervals.Where(i => i.EndUtc > i.StartUtc).OrderBy(i => i.StartUtc).ThenBy(i => i.EndUtc).ToList();
        if (sorted.Count == 0) return [];

        var merged = new List<UptimeInterval>();
        var current = sorted[0];

        for (var i = 1; i < sorted.Count; i++)
        {
            var next = sorted[i];
            if (next.StartUtc <= current.EndUtc)
            {
                current = new UptimeInterval(current.StartUtc, Math.Max(current.EndUtc, next.EndUtc));
            }
            else
            {
                merged.Add(current);
                current = next;
            }
        }
        merged.Add(current);
        return merged;
    }

    /// <summary>
    /// Computes union uptime, per-device uptime, and overlap ("on both") seconds within [<paramref name="from"/>, <paramref name="to"/>).
    /// </summary>
    public static DeviceUptimeBreakdown Breakdown(List<UsageSeg> segs, long from, long to, bool live, long? onSince, bool gamesOnly = false, string? localDeviceId = null)
    {
        if (segs.Count == 0)
        {
            if (live && onSince is { } on && !gamesOnly)
            {
                var intervals = Intervals(segs, from, to, live, onSince, gamesOnly);
                var secs = intervals.Sum(i => i.EndUtc - i.StartUtc);
                var dev = localDeviceId ?? "";
                return new DeviceUptimeBreakdown(secs, new Dictionary<string, long> { [dev] = secs }, 0,
                    new Dictionary<string, List<UptimeInterval>> { [dev] = intervals }, intervals);
            }
            return new DeviceUptimeBreakdown(0, new Dictionary<string, long>(), 0,
                new Dictionary<string, List<UptimeInterval>>(), []);
        }

        var groups = segs.GroupBy(s => s.DeviceId ?? "").ToList();
        if (groups.Count <= 1)
        {
            var dev = groups[0].Key;
            var intervals = Intervals(groups[0].ToList(), from, to, live, onSince, gamesOnly);
            var secs = intervals.Sum(i => i.EndUtc - i.StartUtc);
            return new DeviceUptimeBreakdown(secs, new Dictionary<string, long> { [dev] = secs }, 0,
                new Dictionary<string, List<UptimeInterval>> { [dev] = intervals }, intervals);
        }

        var devIntervals = new Dictionary<string, List<UptimeInterval>>();
        var devSeconds = new Dictionary<string, long>();
        foreach (var g in groups)
        {
            var dev = g.Key;
            var isLocal = string.IsNullOrEmpty(localDeviceId)
                ? (g == groups[0])
                : string.Equals(dev, localDeviceId, StringComparison.OrdinalIgnoreCase);
            var devOnSince = isLocal ? onSince : null;
            var devLive = live && (isLocal || g.Any(s => s.Open));
            var intervals = Intervals(g.ToList(), from, to, devLive, devOnSince, gamesOnly);
            devIntervals[dev] = intervals;
            devSeconds[dev] = intervals.Sum(i => i.EndUtc - i.StartUtc);
        }

        var unionIntervals = Union(devIntervals.Values.SelectMany(x => x));
        var unionSecs = unionIntervals.Sum(i => i.EndUtc - i.StartUtc);
        var sumDevSecs = devSeconds.Values.Sum();
        var onBoth = Math.Max(0, sumDevSecs - unionSecs);

        return new DeviceUptimeBreakdown(unionSecs, devSeconds, onBoth, devIntervals, unionIntervals);
    }

    /// <summary>
    /// The Games filter's total (decision 151): the seconds in [<paramref name="from"/>, <paramref name="to"/>) with at least one
    /// of <paramref name="segs"/> open, overlaps counted once. It ignores sign-in and the gaps between Sessions, which would
    /// otherwise read as the PC's uptime; a sleep or shutdown already closed the Session.
    /// </summary>
    public static long OpenSeconds(List<UsageSeg> segs, long from, long to)
    {
        long total = 0, runEnd = from;
        foreach (var s in segs.Where(s => s.End > from && s.Start < to).OrderBy(s => s.Start))
        {
            var start = Math.Max(Math.Max(s.Start, from), runEnd);
            var end = Math.Min(s.End, to);
            if (end > start) total += end - start;
            runEnd = Math.Max(runEnd, end);
        }
        return total;
    }

    /// <summary>From each sleep-close to the next session start of any app (or now), merged and clipped to today.</summary>
    /// <summary>From each sleep or shutdown close to the next session start of any app (or now), merged and clipped to today.</summary>
    public static List<SleepBand> InactiveBands(List<UsageSeg> segs, long d0, long nowUtc)
    {
        var bands = new List<(long S, long E)>();
        foreach (var s in segs.Where(s => (s.Sleep || s.Off) && s.End >= d0 && s.End <= nowUtc))
        {
            var next = segs.Where(x => x.Start > s.End).Select(x => x.Start).DefaultIfEmpty(nowUtc).Min();
            bands.Add((s.End, Math.Min(next, nowUtc)));
        }
        return MergeBands(bands);
    }

    private static List<SleepBand> MergeBands(List<(long S, long E)> bands)
    {
        var merged = new List<SleepBand>();
        foreach (var (s, e) in bands.OrderBy(b => b.S))
        {
            if (e <= s) continue;
            if (merged.Count > 0 && s <= merged[^1].EndUtc)
                merged[^1] = merged[^1] with { EndUtc = Math.Max(merged[^1].EndUtc, e) };
            else merged.Add(new SleepBand(s, e));
        }
        return merged;
    }

    public static List<SleepBand> SleepBands(List<UsageSeg> segs, long d0, long nowUtc, string? liveDeviceId = null)
    {
        var bands = new List<(long S, long E)>();
        foreach (var group in segs.GroupBy(s => s.DeviceId ?? ""))
        {
            var devSegs = group.OrderBy(s => s.Start).ToList();
            var isLive = !string.IsNullOrEmpty(liveDeviceId) && string.Equals(group.Key, liveDeviceId, StringComparison.OrdinalIgnoreCase);
            foreach (var s in devSegs.Where(s => s.Sleep && s.End >= d0 && s.End <= nowUtc))
            {
                var next = devSegs.Where(x => x.Start > s.End).Select(x => (long?)x.Start).FirstOrDefault();
                if (next is { } n)
                {
                    bands.Add((s.End, Math.Min(n, nowUtc)));
                }
                else if (isLive)
                {
                    bands.Add((s.End, nowUtc));
                }
            }
        }
        return MergeBands(bands);
    }

    /// <summary>
    /// Uptime seconds and per-device breakdown per local day from <paramref name="segs"/>. Today (the live day) runs from
    /// <paramref name="onSinceUtc"/> (sign-in) when known; every other day is judged from its sessions alone.
    /// </summary>
    public static Dictionary<DateOnly, DeviceUptimeBreakdown> PerDayBreakdown(List<UsageSeg> segs, TimeZoneInfo zone, long nowUtc, long? onSinceUtc = null, bool gamesOnly = false, string? localDeviceId = null)
    {
        var today = LocalDays.DateOf(nowUtc, zone);
        var byDay = new Dictionary<DateOnly, List<UsageSeg>>();
        // Each day judges the whole Sessions that touch it, not their clipped slices: sleep bands end at a Session's real end.
        foreach (var slice in UsageDays.Slices(segs, zone))
        {
            if (!byDay.TryGetValue(slice.Day, out var list)) byDay[slice.Day] = list = [];
            list.Add(slice.Seg);
        }
        if (onSinceUtc is not null && !gamesOnly && !byDay.ContainsKey(today)) byDay[today] = [];

        var result = new Dictionary<DateOnly, DeviceUptimeBreakdown>(byDay.Count);
        foreach (var (day, list) in byDay)
        {
            var live = day == today;
            var from = LocalDays.StartOf(day, zone);
            var to = live ? nowUtc : LocalDays.StartOf(day.AddDays(1), zone);
            result[day] = Breakdown(list, from, to, live, live ? onSinceUtc : null, gamesOnly, localDeviceId);
        }
        return result;
    }

    /// <summary>
    /// Uptime seconds per local day from <paramref name="segs"/>, unioned across devices.
    /// </summary>
    public static Dictionary<DateOnly, long> PerDay(List<UsageSeg> segs, TimeZoneInfo zone, long nowUtc, long? onSinceUtc = null, bool gamesOnly = false, string? localDeviceId = null) =>
        PerDayBreakdown(segs, zone, nowUtc, onSinceUtc, gamesOnly, localDeviceId)
            .ToDictionary(kv => kv.Key, kv => kv.Value.UnionSeconds);
}
