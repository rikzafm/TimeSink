namespace TimeSink.Core.Usage;

/// <summary>One piece of a day's bar; <see cref="AppId"/> is null for the Other bucket.</summary>
public sealed record DayBarSegment(long? AppId, string Name, long Seconds);

public enum DayVerdict { Empty, Mostly, Spread }

/// <summary>A day's apps as the Month bar draws them, with the line under it ("Mostly X" or "Spread over n apps").</summary>
public sealed record DayBar(IReadOnlyList<DayBarSegment> Segments, DayVerdict Verdict, DayBarSegment? Leader, int AppCount);

/// <summary>
/// The Month day bar rules (decision 148): the top <see cref="MaxSegments"/> apps by time, whatever their rank (a
/// Background app is not pushed last here), the rest as one Other bucket, and "Mostly X" only when X has at least
/// 1.5 times the time of the runner-up.
/// </summary>
public static class DayBars
{
    public const int MaxSegments = 7;

    public static DayBar Of(IReadOnlyList<MonthAppAmount> apps)
    {
        var byTime = apps.OrderByDescending(a => a.Seconds).ToList();
        var segments = byTime.Take(MaxSegments).Select(a => new DayBarSegment(a.AppId, a.Name, a.Seconds)).ToList();
        var rest = byTime.Skip(MaxSegments).Sum(a => a.Seconds);
        if (rest > 0) segments.Add(new DayBarSegment(null, "Other", rest));

        if (byTime.Count == 0) return new DayBar(segments, DayVerdict.Empty, null, 0);
        var leader = segments[0];
        var mostly = byTime.Count == 1 || byTime[0].Seconds * 2 >= byTime[1].Seconds * 3;
        return new DayBar(segments, mostly ? DayVerdict.Mostly : DayVerdict.Spread, mostly ? leader : null, byTime.Count);
    }
}
