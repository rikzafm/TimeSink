using System.Globalization;
using System.Windows;
using TimeSink.Core.Usage;

namespace TimeSink.UI.Services;

/// <summary>Visible text helpers: string-table lookups and Windows-culture clock formatting (decision 12).</summary>
internal static class TimeText
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> ResourceKeys = new();

    /// <summary>The current theme's string for <paramref name="key"/> (resource <c>Str.&lt;Key&gt;</c>), formatted with args.</summary>
    public static string Str(string key, params object[] args)
    {
        var resourceKey = ResourceKeys.GetOrAdd(key, static k => "Str." + char.ToUpperInvariant(k[0]) + k[1..]);
        var text = Application.Current.TryFindResource(resourceKey) as string ?? $"[{key}]";
        return args.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, args);
    }

    public static DateTime Local(long utcSeconds) =>
        TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(utcSeconds), TimeZoneInfo.Local).DateTime;

    /// <summary>Clock time in the Windows short-time pattern (12 or 24 h), e.g. "1:42 pm" or "13:42" (see <see cref="ClockFormat"/>).</summary>
    public static string Clock(long utcSeconds) => ClockFormat.Clock(Local(utcSeconds), CultureInfo.CurrentCulture);

    /// <summary>Axis label for a whole hour: "6 am" / "06:00" style, minutes dropped for 12-hour clocks.</summary>
    public static string Hour(int hour) => ClockFormat.Hour(hour, CultureInfo.CurrentCulture);
}
