using System.Globalization;

namespace TimeSink.Core.Usage;

/// <summary>
/// Clock text in the Windows regional setting (story 48, decision 12): 12 or 24 hour comes from the culture's short time
/// pattern, which on Windows is the user's own choice in Region settings (the process culture is the user default with
/// overrides on, see <see cref="WindowsCulture"/>). Nothing here hard-codes a clock style.
/// </summary>
public static class ClockFormat
{
    /// <summary>The user's regional culture including their Windows customisations (short time pattern, separators).</summary>
    public static CultureInfo WindowsCulture()
    {
        var c = CultureInfo.CurrentCulture;
        return c.UseUserOverride ? c : new CultureInfo(c.Name, useUserOverride: true);
    }

    public static bool Is24Hour(CultureInfo culture) => culture.DateTimeFormat.ShortTimePattern.Contains('H');

    /// <summary>Short time in the culture's pattern, lower-cased: "1:42 pm" or "13:42".</summary>
    public static string Clock(DateTime local, CultureInfo culture)
        => local.ToString(culture.DateTimeFormat.ShortTimePattern, culture).ToLowerInvariant();

    /// <summary>Axis label for a whole hour: "06:00" for 24 hour clocks, "6 am" (minutes dropped) for 12 hour ones.</summary>
    public static string Hour(int hour, CultureInfo culture)
    {
        var t = new DateTime(2000, 1, 1, ((hour % 24) + 24) % 24, 0, 0);
        var pattern = culture.DateTimeFormat.ShortTimePattern;
        if (Is24Hour(culture)) return t.ToString("HH:mm", culture);
        var noMinutes = pattern.Replace(culture.DateTimeFormat.TimeSeparator + "mm", "").Replace(":mm", "");
        if (noMinutes.Contains("mm")) noMinutes = "h tt";
        return t.ToString(noMinutes, culture).ToLowerInvariant();
    }
}
