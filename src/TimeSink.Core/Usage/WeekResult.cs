namespace TimeSink.Core.Usage;

/// <summary>Which weekday a week starts on (setting <c>week.start</c>).</summary>
public enum WeekStart { Mon, Sun, Sat }

public static class WeekStarts
{
    public static WeekStart Parse(string? stored) =>
        Enum.TryParse<WeekStart>(stored, ignoreCase: false, out var w) && Enum.IsDefined(w) ? w : WeekStart.Mon;

    /// <summary>How many days <paramref name="day"/> is after the first day of its week (0 on the Week start itself).</summary>
    public static int DaysIntoWeek(DateOnly day, DayOfWeek weekStart) => ((int)day.DayOfWeek - (int)weekStart + 7) % 7;

    /// <summary>The first day of the week holding <paramref name="day"/>.</summary>
    public static DateOnly Containing(DateOnly day, DayOfWeek weekStart) => day.AddDays(-DaysIntoWeek(day, weekStart));

    public static DayOfWeek ToDayOfWeek(this WeekStart w) => w switch
    {
        WeekStart.Sun => DayOfWeek.Sunday,
        WeekStart.Sat => DayOfWeek.Saturday,
        _ => DayOfWeek.Monday,
    };
}

/// <summary>One of the seven columns of the current week.</summary>
public sealed record WeekDay(DateOnly Date, long Seconds, bool IsToday, bool IsFuture);

/// <summary>One cell of the 20-week grid. <see cref="Level"/> is the heat step 0-4 (0 for no or little time, and for future days).</summary>
public sealed record HeatCell(DateOnly Date, long Seconds, int Level, bool IsFuture);

/// <summary>One app row (merged apps are one row under the canonical app): this week's time and a 10-day sparkline.</summary>
public sealed record WeekAppRow(long AppId, string Name, long Seconds, IReadOnlyList<long> Spark);

/// <summary>Everything the Week view shows.</summary>
/// <param name="WeekTotals">PC uptime per week for the 20 grid weeks, oldest first (the last is the current week).</param>
/// <param name="PrevWeekSeconds">The week before the shown one, counting only the weekdays that have started in the shown week.</param>
/// <param name="Heat">140 cells, week-major: index = week * 7 + dayOfWeek (week 0 is the oldest, week 19 the current one).</param>
public sealed record WeekResult(
    DateOnly Today,
    DateOnly WeekStartDate,
    long NowUtc,
    long TotalSeconds,
    IReadOnlyList<WeekDay> Days,
    IReadOnlyList<HeatCell> Heat,
    int StreakDays,
    IReadOnlyList<WeekAppRow> Apps,
    IReadOnlyList<long> WeekTotals,
    long PrevWeekSeconds,
    DateOnly? FirstTracked);
