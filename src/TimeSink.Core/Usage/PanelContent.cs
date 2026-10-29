namespace TimeSink.Core.Usage;

/// <summary>Which comparison line a detail panel shows.</summary>
public enum PanelComparisonKind { TypicalForHour, VsWeekAverage, VsSameDayLastWeek }

/// <summary>
/// One comparison line: how far the panel's total is from a reference, in seconds (negative is below it).
/// <see cref="ReferenceSeconds"/> is the reference itself when the line shows it too (the typical hour).
/// </summary>
public sealed record PanelComparison(PanelComparisonKind Kind, long DeltaSeconds, long? ReferenceSeconds = null)
{
    /// <summary>At or above the reference: the panel draws it as a gain.</summary>
    public bool Good => DeltaSeconds >= 0;
}

/// <summary>The app a Week sparkline click asked about: its time on the panel's day, 0 when it was not used.</summary>
public sealed record PanelFocus(long AppId, string Name, long Seconds);

/// <summary>
/// What a detail panel under a clicked bar says, without wording: the total, the apps (the <see cref="Focus"/> app first),
/// the sessions (an hour panel only), the active span (a day panel only) and the comparison lines. The views turn it into
/// text from the string table (decision 64).
/// </summary>
public sealed record PanelContent(
    long TotalSeconds,
    int SessionCount,
    long? SpanStartUtc,
    long? SpanEndUtc,
    PanelFocus? Focus,
    IReadOnlyList<BarApp> Apps,
    IReadOnlyList<BarSession> Sessions,
    IReadOnlyList<PanelComparison> Comparisons);

/// <summary>Builds the <see cref="PanelContent"/> of a Today hour bar and of a Week day column.</summary>
public static class PanelContents
{
    /// <summary>The hour panel: app time and sessions in the hour, and the typical-hour line when the day has a baseline.</summary>
    public static PanelContent ForHour(TodayResult r, int hour)
    {
        var d = BarDetail.ForHour(r, hour);
        var comparisons = new List<PanelComparison>();
        if (r.HasTypical)
            comparisons.Add(new PanelComparison(PanelComparisonKind.TypicalForHour,
                (long)Math.Round(d.AppSeconds - d.TypicalAppSeconds), (long)Math.Round(d.TypicalAppSeconds)));
        return new PanelContent(d.AppSeconds, d.Sessions.Count, null, null, null, d.Apps, d.Sessions, comparisons);
    }

    /// <summary>
    /// The day panel: the day's total and active span, its apps, and the lines against the week's average and the same day
    /// a week earlier. A <paramref name="focus"/> app (found by id) moves to the front.
    /// </summary>
    public static PanelContent ForDay(DayDetail d, (long AppId, string Name)? focus = null)
    {
        var apps = d.Apps.ToList();
        PanelFocus? focused = null;
        if (focus is { } f)
        {
            var at = apps.FindIndex(a => a.AppId == f.AppId);
            focused = new PanelFocus(f.AppId, f.Name, at >= 0 ? apps[at].Seconds : 0);
            if (at > 0)
            {
                var first = apps[at];
                apps.RemoveAt(at);
                apps.Insert(0, first);
            }
        }
        var comparisons = new List<PanelComparison>
        {
            new(PanelComparisonKind.VsWeekAverage, (long)Math.Round(d.TotalSeconds - d.WeekAverageSeconds)),
            new(PanelComparisonKind.VsSameDayLastWeek, d.TotalSeconds - d.SameDayLastWeekSeconds),
        };
        var span = d.FirstStartUtc is not null && d.LastEndUtc is not null;
        return new PanelContent(d.TotalSeconds, 0, span ? d.FirstStartUtc : null, span ? d.LastEndUtc : null, focused, apps, [], comparisons);
    }
}
