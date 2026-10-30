using TimeSink.Core.Theming;

namespace TimeSink.Core.Usage;

/// <summary>Which <see cref="Phrase"/> a result is told with. The views draw what comes back and decide none of the wording (decision 164).</summary>
public static class UsagePhrases
{
    /// <summary>The Week headline's lines: the change against last week so far, then the daily average; a missing one is left out.</summary>
    public static IReadOnlyList<Phrase> WeekSummary(WeekResult r)
    {
        var phrases = new List<Phrase>();
        if (r.ChangeSeconds is { } change)
            phrases.Add(new Phrase(change >= 0 ? StringKeys.WeekVsLastUp : StringKeys.WeekVsLastDown, UsageText.HoursMinutes(Math.Abs(change))));
        if (r.AverageSeconds > 0)
            phrases.Add(new Phrase(StringKeys.WeekAvg, UsageText.HoursMinutes((long)r.AverageSeconds)));
        return phrases;
    }

    /// <summary>
    /// What a Week day panel says under its title: the day's total and active span, or, for a focus app, its time of the
    /// total ("not used" when it had none). <paramref name="clock"/> formats a UTC second as the user's clock time.
    /// </summary>
    public static Phrase DaySubtitle(PanelContent c, Func<long, string> clock)
    {
        object span = c is { SpanStartUtc: { } a, SpanEndUtc: { } b } ? new Phrase(PanelStringKeys.DaySpan, clock(a), clock(b)) : "";
        var total = UsageText.HoursMinutes(c.TotalSeconds);
        if (c.Focus is not { } f) return new Phrase(PanelStringKeys.DayTotal, total, span);
        object used = f.Seconds > 0 ? UsageText.HoursMinutes(f.Seconds) : new Phrase(PanelStringKeys.DayNotUsed);
        return new Phrase(PanelStringKeys.DayFocus, f.Name, used, total, span);
    }

    /// <summary>What a Today hour panel says under its title: app time and sessions, or that nothing happened.</summary>
    public static Phrase HourSubtitle(PanelContent c) =>
        c.TotalSeconds > 0 ? new Phrase(PanelStringKeys.HourSummary, UsageText.HoursMinutes(c.TotalSeconds), c.SessionCount) : new Phrase(PanelStringKeys.HourEmpty);

    /// <summary>
    /// What the Year panel says about a month, as pieces the view joins with nothing between: where it stands (before
    /// tracking, projected, on pace, against the month before, the first month tracked), then "your best month" for a
    /// finished best month. A finished month with nothing to say gives none.
    /// </summary>
    public static IReadOnlyList<Phrase> YearMonthNote(YearResult r, YearMonth m)
    {
        var started = r.TrackingStarted;
        var prev = UsageText.MonthAbbr(m.Month - 1);
        switch (m.Kind)
        {
            case YearMonthKind.BeforeTracking:
                return [started is { } s ? new Phrase(YearStringKeys.NoteBeforeOn, UsageText.ShortDate(s)) : new Phrase(YearStringKeys.NoteBefore)];
            case YearMonthKind.Future:
                return [new Phrase(YearStringKeys.NoteProjected)];
            case YearMonthKind.Current:
                return [m.Delta is { } d
                    ? new Phrase(YearStringKeys.NoteCurrentVs, Hours(m.Seconds), Hours(m.ProjectedSeconds), UsageText.Delta(d), prev)
                    : new Phrase(YearStringKeys.NoteCurrent, Hours(m.Seconds), Hours(m.ProjectedSeconds))];
        }

        var note = new List<Phrase>();
        if (m.Delta is { } change) note.Add(new Phrase(YearStringKeys.NoteVs, UsageText.Delta(change), prev));
        else if (started is { } first && first.Year == r.Year && first.Month == m.Month) note.Add(new Phrase(YearStringKeys.NoteFirst, UsageText.ShortDate(first)));
        if (m.Month == r.BestMonth) note.Add(new Phrase(YearStringKeys.NoteBest));
        return note;
    }

    private static Phrase Hours(long seconds) => new(YearStringKeys.Hours, UsageText.Hours(seconds));

    /// <summary>The comparison lines of an hour or day panel, each labelled by its kind with a signed value.</summary>
    public static IReadOnlyList<PanelLine> PanelLines(PanelContent c) => c.Comparisons.Select(x => x.Kind switch
    {
        PanelComparisonKind.TypicalForHour => new PanelLine(new Phrase(PanelStringKeys.HourTypical),
            new Phrase(PanelStringKeys.HourTypicalValue, UsageText.HoursMinutes(x.ReferenceSeconds ?? 0), UsageText.SignedHoursMinutes(x.DeltaSeconds)), x.Good),
        PanelComparisonKind.VsWeekAverage => new PanelLine(new Phrase(PanelStringKeys.DayVsAverage),
            new Phrase(PanelStringKeys.Delta, UsageText.SignedHoursMinutes(x.DeltaSeconds)), x.Good),
        _ => new PanelLine(new Phrase(PanelStringKeys.DayVsLastWeek),
            new Phrase(PanelStringKeys.Delta, UsageText.SignedHoursMinutes(x.DeltaSeconds)), x.Good),
    }).ToList();
}
