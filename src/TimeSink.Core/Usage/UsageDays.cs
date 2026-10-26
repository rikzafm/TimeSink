namespace TimeSink.Core.Usage;

/// <summary>One Session's share of a single Local day: clipped to that day's bounds, with the whole Session it came from.</summary>
internal readonly record struct UsageSlice(DateOnly Day, long Start, long End, UsageSeg Seg);

/// <summary>
/// Cuts Sessions at Local-day boundaries (DST-aware: a skipped midnight moves to the first valid hour, a repeated one
/// takes the later offset, so a day may last 23 or 25 hours). Callers aggregate the slices their own way; the whole
/// Session stays on every slice for "longest" and "one Session per day it touches".
/// </summary>
internal static class UsageDays
{
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
