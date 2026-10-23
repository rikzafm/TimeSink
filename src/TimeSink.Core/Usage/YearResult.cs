namespace TimeSink.Core.Usage;

public enum YearMonthKind
{
    /// <summary>Tracking had not begun: drawn dotted/empty.</summary>
    BeforeTracking,
    /// <summary>A finished month with real data.</summary>
    Tracked,
    /// <summary>The month in progress: real data plus a projected extension.</summary>
    Current,
    /// <summary>A month still to come: only a ghost bar at the recent average.</summary>
    Future,
}

/// <summary>One app's share of a month (merged apps are one row under their canonical app).</summary>
public sealed record YearAppShare(long AppId, string Name, long Seconds);

/// <summary>
/// One of the 12 columns. <see cref="Seconds"/> is real data; <see cref="ProjectedSeconds"/> is the full-month projection
/// for the current month, the ghost value for future months, and equals <see cref="Seconds"/> otherwise.
/// <see cref="DeltaPercent"/> compares with the previous month (the projection for the current month); null when there
/// is nothing to compare with.
/// </summary>
public sealed record YearMonth(
    int Month,
    YearMonthKind Kind,
    long Seconds,
    long ProjectedSeconds,
    double? DeltaPercent,
    long? PreviousSeconds,
    IReadOnlyList<YearAppShare> TopApps);

/// <summary>Everything the Year "Pace" sub-view shows, computed at <see cref="NowUtc"/>.</summary>
public sealed record YearResult(
    int Year,
    long NowUtc,
    DateOnly? TrackingStarted,
    IReadOnlyList<YearMonth> Months,
    long TotalSeconds,
    long? AverageMonthSeconds,
    int? BestMonth,
    long BestMonthSeconds,
    long? PaceYearSeconds,
    int RecentMonthCount);
