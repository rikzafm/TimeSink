namespace TimeSink.Core.Usage;

/// <summary>The size of a step: a Local day, a calendar week (from a chosen first day), a month or a year.</summary>
public enum PeriodUnit { Day, Week, Month, Year }

/// <summary>
/// Moving through periods between the first tracked day and today: nothing earlier than the first tracked day, nothing
/// later than today, and a period is reachable when it overlaps that span. <c>today</c> is a parameter so callers pass the
/// Local day they already hold. A first day after today counts as today.
/// </summary>
public sealed class PeriodNavigator
{
    private readonly DayOfWeek _weekStart;

    public PeriodNavigator(PeriodUnit unit, DateOnly first, DateOnly today, DayOfWeek weekStart = DayOfWeek.Monday)
    {
        Unit = unit;
        Today = today;
        First = first > today ? today : first;
        _weekStart = weekStart;
    }

    public PeriodUnit Unit { get; }

    public DateOnly First { get; }

    public DateOnly Today { get; }

    /// <summary>True when <paramref name="day"/> lies between the first tracked day and today.</summary>
    public bool Contains(DateOnly day) => day >= First && day <= Today;

    /// <summary>The nearest day inside the span.</summary>
    public DateOnly Clamp(DateOnly day) => day < First ? First : day > Today ? Today : day;

    /// <summary>The first day of the period holding <paramref name="day"/>.</summary>
    public DateOnly StartOf(DateOnly day) => Unit switch
    {
        PeriodUnit.Week => day.AddDays(-(((int)day.DayOfWeek - (int)_weekStart + 7) % 7)),
        PeriodUnit.Month => new DateOnly(day.Year, day.Month, 1),
        PeriodUnit.Year => new DateOnly(day.Year, 1, 1),
        _ => day,
    };

    /// <summary>The anchor one period earlier (<paramref name="dir"/> -1) or later (+1). It is not clamped.</summary>
    public DateOnly Step(DateOnly anchor, int dir) => StepBy(Unit, anchor, dir);

    /// <summary>The anchor one <paramref name="unit"/> earlier or later, for callers that have no span to hold.</summary>
    public static DateOnly StepBy(PeriodUnit unit, DateOnly anchor, int dir) => unit switch
    {
        PeriodUnit.Week => anchor.AddDays(7 * dir),
        PeriodUnit.Month => anchor.AddMonths(dir),
        PeriodUnit.Year => anchor.AddYears(dir),
        _ => anchor.AddDays(dir),
    };

    public bool CanPrev(DateOnly anchor) => StartOf(anchor) > StartOf(First);

    public bool CanNext(DateOnly anchor) => StartOf(anchor) < StartOf(Today);
}
