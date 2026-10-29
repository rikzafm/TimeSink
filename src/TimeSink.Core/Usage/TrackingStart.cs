using TimeSink.Core.Apps;

namespace TimeSink.Core.Usage;

/// <summary>
/// The Tracking start: the first Local day on which a non-retired Tracked app existed (the earliest added date among
/// the loaded apps; null when nothing is tracked). Days before it are not counted, and a month that begins after the
/// 1st is only partly tracked. Every usage query asks this one value instead of repeating the date expression
/// (decision 147).
/// </summary>
internal readonly record struct TrackingStart(DateOnly? FirstDay)
{
    public static TrackingStart Of(AppGroups apps, TimeZoneInfo zone) =>
        new(apps.Count == 0 ? null : LocalDays.DateOf(apps.Values.Min(a => a.AddedUtc), zone));

    /// <summary>Whether <paramref name="day"/> falls on or after the Tracking start.</summary>
    public bool Counts(DateOnly day) => FirstDay is { } f && day >= f;

    /// <summary>Whether tracking began after the 1st of this month, so the month is not a full one.</summary>
    public bool IsPartialMonth(int year, int month) => FirstDay is { Day: > 1 } f && f.Year == year && f.Month == month;

    /// <summary>The later of <paramref name="day"/> and the Tracking start (just <paramref name="day"/> when nothing is tracked).</summary>
    public DateOnly Since(DateOnly day) => FirstDay is { } f && f > day ? f : day;
}
