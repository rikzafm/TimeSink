using System.Globalization;

namespace TimeSink.Core.Usage;

/// <summary>Durations are always <c>Xh YYm</c> (implementation.md 8.1); under an hour just <c>Ym</c>.</summary>
public static class DurationFormat
{
    public static string HoursMinutes(long seconds)
    {
        var minutes = Math.Max(0, seconds) / 60;
        return minutes >= 60
            ? string.Create(CultureInfo.InvariantCulture, $"{minutes / 60}h {minutes % 60:00}m")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}m");
    }

    /// <summary>A delta with an explicit sign, e.g. <c>+38m</c> or <c>-1h 30m</c>.</summary>
    public static string Signed(long seconds) => (seconds < 0 ? "-" : "+") + HoursMinutes(Math.Abs(seconds));
}
