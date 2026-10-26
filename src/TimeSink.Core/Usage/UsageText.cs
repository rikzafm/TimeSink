using System.Globalization;

namespace TimeSink.Core.Usage;

/// <summary>Language-free display text for usage figures; views wrap it in their own string-table templates.</summary>
public static class UsageText
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Hours without a unit: one decimal under ten hours ("2.5"), whole hours from ten up ("27"); negative input reads "0".</summary>
    public static string Hours(long seconds)
    {
        var h = Math.Max(0, seconds) / 3600.0;
        if (h < 9.95) return h == 0 ? "0" : h.ToString("0.0", Inv);
        return Math.Round(h, MidpointRounding.AwayFromZero).ToString("0", Inv);
    }

    /// <summary>A change in percent with an explicit sign ("+12%", "-3%"); it rounds first, so zero reads "0%". Non-finite input reads "".</summary>
    public static string Delta(double percent)
    {
        if (!double.IsFinite(percent)) return "";
        var p = (long)Math.Round(percent, MidpointRounding.AwayFromZero);
        return (p > 0 ? "+" : "") + p.ToString(Inv) + "%";
    }

    /// <summary>The digits of a percent change without sign or "%" ("12"), for templates that pick up/down wording themselves.</summary>
    public static string DeltaMagnitude(double percent) =>
        double.IsFinite(percent) ? Math.Abs((long)Math.Round(percent, MidpointRounding.AwayFromZero)).ToString(Inv) : "";

    /// <summary>Same as <see cref="Delta"/> for a ratio change (0.12 reads "+12%").</summary>
    public static string DeltaOfRatio(double fraction) => Delta(fraction * 100);

    /// <summary>Calendar cell text: blank for none, "42m" under an hour, hours from an hour up ("2.5h", "12h").</summary>
    public static string MonthCell(long seconds)
    {
        if (seconds <= 0) return "";
        return seconds < 3600 ? (seconds / 60).ToString(Inv) + "m" : Hours(seconds) + "h";
    }

    /// <summary>Durations are always <c>Xh YYm</c> (implementation.md 8.1); under an hour just <c>Ym</c>.</summary>
    public static string HoursMinutes(long seconds)
    {
        var minutes = Math.Max(0, seconds) / 60;
        return minutes >= 60
            ? string.Create(Inv, $"{minutes / 60}h {minutes % 60:00}m")
            : string.Create(Inv, $"{minutes}m");
    }

    /// <summary>A delta with an explicit sign, e.g. <c>+38m</c> or <c>-1h 30m</c>.</summary>
    public static string SignedHoursMinutes(long seconds) => (seconds < 0 ? "-" : "+") + HoursMinutes(Math.Abs(seconds));

    /// <summary>Average hours per day used, one decimal ("2.6").</summary>
    public static string AveragePerDay(double secondsPerDay) => (Math.Max(0, secondsPerDay) / 3600.0).ToString("0.0", Inv);

    /// <summary>Share of the tracked total (or any fraction of a whole) as a whole percent; a non-zero share under 1% reads "&lt;1%".</summary>
    public static string Share(double fraction)
    {
        if (fraction <= 0) return "0%";
        var p = (int)Math.Round(fraction * 100, MidpointRounding.AwayFromZero);
        return p == 0 ? "<1%" : p.ToString(Inv) + "%";
    }

    /// <summary>Which Local day <paramref name="utcSeconds"/> falls on relative to <paramref name="nowUtc"/> in <paramref name="zone"/>;
    /// compares dates, not elapsed time, so 23 h and 25 h days still count as one day. A time after now on today's date is Today.</summary>
    public static DayBucket DayOf(long utcSeconds, long nowUtc, TimeZoneInfo zone)
    {
        var day = LocalDays.DateOf(utcSeconds, zone);
        var today = LocalDays.DateOf(nowUtc, zone);
        var kind = day >= today ? DayKind.Today : day == today.AddDays(-1) ? DayKind.Yesterday : DayKind.Date;
        return new DayBucket(kind, day, day.Year == today.Year);
    }
}

public enum DayKind { Today, Yesterday, Date }

/// <summary>The day a time falls on; <see cref="Date"/> is its Local day and <see cref="InCurrentYear"/> compares with the year of now.</summary>
public readonly record struct DayBucket(DayKind Kind, DateOnly Date, bool InCurrentYear);
