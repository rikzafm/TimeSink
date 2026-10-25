namespace TimeSink.Core.Usage;

/// <summary>The calendar month with the most time for one app.</summary>
public sealed record AllBestMonth(int Year, int Month, long Seconds);

/// <summary>
/// One tracked app in the All table. <see cref="Share"/> is a fraction (0-1) of the tracked total; the average is
/// seconds per day used. Merged apps are one row under the canonical app.
/// </summary>
public sealed record AllAppRow(
    long AppId,
    string Name,
    long Seconds,
    int DaysUsed,
    double AverageSecondsPerDay,
    double Share,
    DateOnly? FirstTracked,
    AllBestMonth? BestMonth,
    long LongestSessionSeconds,
    bool Background = false);

/// <summary>A retired app: listed below the table, dimmed, never part of the totals or shares.</summary>
public sealed record AllRetiredRow(
    long AppId,
    string Name,
    long Seconds,
    int DaysUsed,
    double AverageSecondsPerDay,
    DateOnly StoppedDate);

/// <summary>Totals over tracked apps only. <see cref="Seconds"/> is PC uptime; <see cref="DaysUsed"/> counts days on which any tracked app had time.</summary>
public sealed record AllTotals(long Seconds, int DaysUsed, double AverageSecondsPerDay);

/// <summary>Everything the All tab shows (rows are unsorted: see <see cref="AllSorting"/>).</summary>
public sealed record AllResult(
    IReadOnlyList<AllAppRow> Apps,
    IReadOnlyList<AllRetiredRow> Retired,
    AllTotals Totals);
