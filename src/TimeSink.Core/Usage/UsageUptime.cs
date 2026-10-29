namespace TimeSink.Core.Usage;

/// <summary>
/// PC uptime as the usage queries see it (decision 125): nothing records the PC being on except tracked sessions, so a
/// finished day runs from its first tracked session to its last, a live day to now, and sleep bands are subtracted.
/// </summary>
internal static class UsageUptime
{
    /// <summary>
    /// Seconds the PC was on within [<paramref name="from"/>, <paramref name="to"/>), sleep excluded. The day starts at its
    /// first tracked session or <paramref name="onSince"/> (e.g. sign-in), whichever is earlier; a live day runs to
    /// <paramref name="to"/>, a finished one to the last tracked session end (nothing else records the PC being on).
    /// </summary>
    public static long Seconds(List<UsageSeg> segs, long from, long to, bool live, long? onSince, bool gamesOnly = false)
    {
        if (gamesOnly) return OpenSeconds(segs, from, to);
        var inRange = segs.Where(s => s.End > from && s.Start < to).ToList();
        // Per day, not per Windows session: after a mid-day restart the sign-in is later than the day's first activity.
        long start;
        if (inRange.Count > 0) start = Math.Max(from, Math.Min(onSince ?? long.MaxValue, inRange.Min(s => s.Start)));
        else if (onSince is { } on) start = Math.Max(from, on);
        else return 0;
        var end = live ? to : inRange.Count > 0 ? Math.Min(to, inRange.Max(s => s.End)) : start;
        if (end <= start) return 0;
        var asleep = SleepBands(segs, start, end).Sum(b => Math.Max(0, Math.Min(b.EndUtc, end) - Math.Max(b.StartUtc, start)));
        return Math.Max(0, end - start - asleep);
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
    public static List<SleepBand> SleepBands(List<UsageSeg> segs, long d0, long nowUtc)
    {
        var bands = new List<(long S, long E)>();
        foreach (var s in segs.Where(s => s.Sleep && s.End >= d0 && s.End <= nowUtc))
        {
            var next = segs.Where(x => x.Start > s.End).Select(x => x.Start).DefaultIfEmpty(nowUtc).Min();
            bands.Add((s.End, Math.Min(next, nowUtc)));
        }
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

    /// <summary>
    /// Uptime seconds per local day from <paramref name="segs"/>. Today (the live day) runs from
    /// <paramref name="onSinceUtc"/> (sign-in) when known; every other day is judged from its sessions alone.
    /// </summary>
    public static Dictionary<DateOnly, long> PerDay(List<UsageSeg> segs, TimeZoneInfo zone, long nowUtc, long? onSinceUtc = null, bool gamesOnly = false)
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

        var result = new Dictionary<DateOnly, long>(byDay.Count);
        foreach (var (day, list) in byDay)
        {
            var live = day == today;
            var from = LocalDays.StartOf(day, zone);
            var to = live ? nowUtc : LocalDays.StartOf(day.AddDays(1), zone);
            result[day] = Seconds(list, from, to, live, live ? onSinceUtc : null, gamesOnly);
        }
        return result;
    }
}
