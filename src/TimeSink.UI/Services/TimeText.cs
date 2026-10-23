using System.Globalization;
using System.Windows;

namespace TimeSink.UI.Services;

/// <summary>Visible text helpers: string-table lookups and Windows-culture clock formatting (decision 12).</summary>
internal static class TimeText
{
    /// <summary>The current theme's string for <paramref name="key"/> (resource <c>Str.&lt;Key&gt;</c>), formatted with args.</summary>
    public static string Str(string key, params object[] args)
    {
        var text = Application.Current.TryFindResource("Str." + char.ToUpperInvariant(key[0]) + key[1..]) as string ?? $"[{key}]";
        return args.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, args);
    }

    public static DateTime Local(long utcSeconds) =>
        TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(utcSeconds), TimeZoneInfo.Local).DateTime;

    /// <summary>Clock time in the Windows short-time pattern (12 or 24 h), e.g. "1:42 pm" or "13:42".</summary>
    public static string Clock(long utcSeconds) => Local(utcSeconds).ToString("t", CultureInfo.CurrentCulture).ToLowerInvariant();

    /// <summary>Axis label for a whole hour: "6 am" / "06:00" style, minutes dropped for 12-hour clocks.</summary>
    public static string Hour(int hour)
    {
        var t = new DateTime(2000, 1, 1, hour % 24, 0, 0);
        var pattern = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern;
        return pattern.Contains('H') ? t.ToString("HH:mm", CultureInfo.CurrentCulture)
            : t.ToString(pattern.Replace(":mm", ""), CultureInfo.CurrentCulture).ToLowerInvariant();
    }
}
