namespace TimeSink.Core.Usage;

/// <summary>One matrix cell: the app's seconds in a month and its colour level 0-4 relative to its own best month.</summary>
public readonly record struct MatrixCell(long Seconds, int Level);

/// <summary>
/// One app row (merged apps are one row under the canonical app). <see cref="Cells"/> has one entry per visible month
/// (index 0 = <see cref="MatrixResult.FirstMonth"/>). <see cref="TrendPercent"/> compares the last three visible months with
/// the three before them; null when there is no earlier month with time.
/// </summary>
public sealed record MatrixRow(
    long AppId,
    string Name,
    IReadOnlyList<MatrixCell> Cells,
    long TotalSeconds,
    int SharePercent,
    int PeakMonth,
    long PeakSeconds,
    double? TrendPercent);

/// <summary>A month column: the column total and its top apps (for the tap summary).</summary>
public sealed record MatrixMonth(int Month, long TotalSeconds, IReadOnlyList<YearAppShare> TopApps);

/// <summary>Everything the Year "Matrix" sub-view shows, computed at <see cref="NowUtc"/>.</summary>
public sealed record MatrixResult(
    int Year,
    long NowUtc,
    DateOnly? TrackingStarted,
    int FirstMonth,
    IReadOnlyList<MatrixMonth> Months,
    IReadOnlyList<MatrixRow> Rows,
    long GrandSeconds);
