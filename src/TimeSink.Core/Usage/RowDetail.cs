namespace TimeSink.Core.Usage;

/// <summary>
/// What an expanded Today row shows (decision 149): the Sessions worth drawing, how many blips were left out, the
/// longest of them, the app's time against its 7-day average and the two dial fractions of a 24 h day.
/// </summary>
/// <param name="Shown">Sessions of at least <see cref="RowDetails.MinShownSessionSeconds"/>, in time order.</param>
/// <param name="Blips">Shorter Sessions: not drawn or listed, only counted.</param>
/// <param name="AverageSeconds">The 7-day average, or null when the app has none (or it is 0).</param>
/// <param name="VsAverage">The row's time against <paramref name="AverageSeconds"/>; null without an average.</param>
public sealed record RowDetail(
    IReadOnlyList<TodaySession> Shown,
    int Blips,
    long LongestSeconds,
    double? AverageSeconds,
    Change? VsAverage,
    double TotalFraction,
    double? AverageFraction);

public static class RowDetails
{
    /// <summary>A Session shorter than this is a blip.</summary>
    public const int MinShownSessionSeconds = 60;

    public static IReadOnlyList<TodaySession> ShownSessions(TodayAppRow row) =>
        row.Sessions.Where(s => s.Seconds >= MinShownSessionSeconds).ToList();

    public static RowDetail Of(TodayAppRow row)
    {
        var shown = ShownSessions(row);
        double? avg = row.SevenDayAverageSeconds is { } a && a > 0 ? a : null;
        return new RowDetail(
            shown, row.Sessions.Count - shown.Count,
            shown.Count > 0 ? shown.Max(s => s.Seconds) : 0,
            avg, avg is { } mean ? Change.Between(row.Seconds, mean) : null,
            row.Seconds / 86400.0, avg is { } v ? v / 86400.0 : null);
    }
}
