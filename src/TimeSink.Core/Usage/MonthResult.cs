namespace TimeSink.Core.Usage;

/// <summary>One app's seconds (merged apps are one entry under the canonical app).</summary>
public sealed record MonthAppAmount(long AppId, string Name, long Seconds);

/// <summary>One calendar day. <see cref="Heat"/> is 0-4 (under 1 h, 3 h, 5 h, 7 h, 7 h or more).</summary>
public sealed record MonthDay(
    DateOnly Date,
    long Seconds,
    int Heat,
    bool IsFuture,
    bool IsToday,
    IReadOnlyList<MonthAppAmount> Apps)
{
    /// <summary>The day's bar: top apps by time, an Other bucket and the Mostly / Spread verdict (see <see cref="DayBars"/>).</summary>
    public DayBar Bar => DayBars.Of(Apps);
}

/// <summary>A grid row: seven cells (null = blank padding outside the month) and the row's total.</summary>
public sealed record MonthWeek(IReadOnlyList<MonthDay?> Days, long TotalSeconds);

public sealed record BestDay(DateOnly Date, long Seconds);

/// <summary>A top app with its time against the same days of the previous month; the percentage is null with no baseline.</summary>
public sealed record MonthTopApp(
    long AppId,
    string Name,
    long Seconds,
    long PreviousSeconds,
    long DeltaSeconds,
    Change? Delta);

/// <summary>Everything the Month view shows.</summary>
public sealed record MonthResult(
    int Year,
    int Month,
    DateOnly FirstDay,
    int DaysInMonth,
    int DaysCounted,
    bool IsCurrent,
    DayOfWeek WeekStart,
    long NowUtc,
    IReadOnlyList<MonthDay> Days,
    IReadOnlyList<MonthWeek> Weeks,
    long TotalSeconds,
    long PreviousSeconds,
    long DeltaSeconds,
    Change? Delta,
    double DailyAverageSeconds,
    BestDay? Best,
    IReadOnlyList<MonthTopApp> TopApps,
    DateOnly DefaultSelected,
    DateOnly? FirstTracked = null);
