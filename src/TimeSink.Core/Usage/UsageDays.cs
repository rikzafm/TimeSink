namespace TimeSink.Core.Usage;

/// <summary>One Session's share of a single Local day: clipped to that day's bounds, with the whole Session it came from.</summary>
internal readonly record struct UsageSlice(DateOnly Day, long Start, long End, UsageSeg Seg);

/// <summary>The part of a Session or slice that falls in one wall-clock hour (0-23) of a Local day.</summary>
internal readonly record struct HourPiece(int Hour, long Start, long End);

/// <summary>
/// Cuts Sessions at Local-day boundaries (DST-aware: a skipped midnight moves to the first valid hour, a repeated one
/// takes the later offset, so a day may last 23 or 25 hours). Callers aggregate the slices their own way; the whole
/// Session stays on every slice for "longest" and "one Session per day it touches". Months roll up from day slices,
/// and wall-clock hours are cut by <see cref="HourPieces"/>, so every query agrees on a DST day (decision 146).
/// </summary>
internal static class UsageDays
{
    /// <summary>Seconds of <paramref name="s"/> inside [a, b), for a window that is not a Local day.</summary>
    public static long Overlap(UsageSeg s, long a, long b) => Math.Max(0, Math.Min(s.End, b) - Math.Max(s.Start, a));

    /// <summary>Seconds per group on each Local day the Sessions touch.</summary>
    public static Dictionary<DateOnly, Dictionary<long, long>> PerDayGroup(IEnumerable<UsageSeg> segs, TimeZoneInfo zone)
    {
        var days = new Dictionary<DateOnly, Dictionary<long, long>>();
        foreach (var sl in Slices(segs, zone))
        {
            if (!days.TryGetValue(sl.Day, out var groups)) days[sl.Day] = groups = new Dictionary<long, long>();
            groups[sl.Seg.Group] = groups.GetValueOrDefault(sl.Seg.Group) + (sl.End - sl.Start);
        }
        return days;
    }

    /// <summary>
    /// [a, b) cut at every local whole hour. On a 23 h day the skipped hour never appears; on a 25 h day the repeated
    /// hour appears twice, so summing by <see cref="HourPiece.Hour"/> puts both passes in one column.
    /// </summary>
    public static IEnumerable<HourPiece> HourPieces(long a, long b, TimeZoneInfo zone)
    {
        for (var t = a; t < b;)
        {
            var local = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(t), zone);
            var next = Math.Min(t + 3600 - (local.Minute * 60 + local.Second), b);
            yield return new HourPiece(local.Hour, t, next);
            t = next;
        }
    }

    /// <summary>Seconds per wall-clock hour column (0-23) of one Local day.</summary>
    public static long[] HourSums(IEnumerable<UsageSeg> segs, DateOnly day, TimeZoneInfo zone)
    {
        var d0 = LocalDays.StartOf(day, zone);
        var d1 = LocalDays.StartOf(day.AddDays(1), zone);
        var sums = new long[24];
        foreach (var s in segs)
        {
            var a = Math.Max(s.Start, d0);
            var b = Math.Min(s.End, d1);
            if (b <= a) continue;
            foreach (var p in HourPieces(a, b, zone)) sums[p.Hour] += p.End - p.Start;
        }
        return sums;
    }

    public static IEnumerable<UsageSlice> Slices(IEnumerable<UsageSeg> segs, TimeZoneInfo zone)
    {
        foreach (var seg in segs)
        {
            if (seg.End <= seg.Start) continue;
            var last = LocalDays.DateOf(seg.End - 1, zone);
            for (var day = LocalDays.DateOf(seg.Start, zone); day <= last; day = day.AddDays(1))
            {
                var a = Math.Max(seg.Start, LocalDays.StartOf(day, zone));
                var b = Math.Min(seg.End, LocalDays.StartOf(day.AddDays(1), zone));
                if (b > a) yield return new UsageSlice(day, a, b, seg);
            }
        }
    }
}
