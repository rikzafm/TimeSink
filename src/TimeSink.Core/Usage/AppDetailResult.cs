namespace TimeSink.Core.Usage;

/// <summary>Why a day cell is drawn the way it is: tracked days carry a heat level, the others are outlines.</summary>
public enum AppDetailDayKind { Tracked, BeforeTracking, Future }

/// <summary>One calendar day of the year grid. <see cref="Sessions"/> counts the sessions that touch the day.</summary>
public sealed record AppDetailDay(DateOnly Date, long Seconds, int Sessions, int Level, AppDetailDayKind Kind);

/// <summary>The most recent session of the app: when it started, how long it ran and whether it is still running.</summary>
public sealed record AppDetailLast(long StartUtc, long Seconds, bool Running);

/// <summary>
/// Everything the app detail page shows. The stats are lifetime; <see cref="Days"/> covers every day of
/// <see cref="Year"/> (January to December) and the year bounds are the first tracked year and the current one.
/// </summary>
public sealed record AppDetailResult(
    long AppId,
    string Name,
    string ExeName,
    DateOnly? FirstTracked,
    AppDetailLast? Last,
    long TotalSeconds,
    int SessionCount,
    long AverageSessionSeconds,
    long LongestSessionSeconds,
    int Year,
    int FirstYear,
    int LastYear,
    long YearSeconds,
    long PeakDaySeconds,
    IReadOnlyList<AppDetailDay> Days);
