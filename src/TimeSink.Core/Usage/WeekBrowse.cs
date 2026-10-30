namespace TimeSink.Core.Usage;

/// <summary>
/// What a click on a Week day asks for: the panel now (<see cref="OpenNow"/>), or first the week holding the day,
/// <see cref="LoadWeeksBack"/> weeks before the current one.
/// </summary>
public readonly record struct DayClick(int? LoadWeeksBack)
{
    public bool OpenNow => LoadWeeksBack is null;
}

/// <summary>
/// Where the Week view can go from the week a <see cref="WeekResult"/> shows (decision 165): the weeks before and after it
/// between the first tracked week and this one, the week behind each trend bar, and what a day click needs. Weeks are
/// counted back from the current one (0), the way <see cref="WeekQuery"/> takes them. Built from a result, so the bounds
/// and the Week start are the ones that result was read with.
/// </summary>
public sealed class WeekBrowse
{
    private readonly PeriodNavigator _weeks;
    private readonly DateOnly _shown;
    private readonly int _bars;

    public WeekBrowse(WeekResult shown)
    {
        _weeks = new PeriodNavigator(PeriodUnit.Week, shown.FirstTracked ?? shown.Today, shown.Today, shown.WeekStartDate.DayOfWeek);
        _shown = shown.WeekStartDate;
        _bars = shown.WeekTotals.Count;
        WeeksBack = _weeks.WeeksBack(_shown);
    }

    /// <summary>The shown week, in whole weeks before the current one.</summary>
    public int WeeksBack { get; }

    public bool CanEarlier => _weeks.CanPrev(_shown);

    public bool CanLater => _weeks.CanNext(_shown);

    /// <summary>The week before the shown one; null at the first tracked week.</summary>
    public int? Earlier() => CanEarlier ? _weeks.WeeksBack(_weeks.Step(_shown, -1)) : null;

    /// <summary>The week after the shown one; null at the current week.</summary>
    public int? Later() => CanLater ? _weeks.WeeksBack(_weeks.Step(_shown, 1)) : null;

    /// <summary>The first day of the week behind trend bar <paramref name="i"/>: the strip ends on the current week and runs oldest first.</summary>
    public DateOnly TrendBarStart(int i) => _weeks.WeekAnchor(_bars - 1 - i);

    /// <summary>The week to load for a click on trend bar <paramref name="i"/>; null when it is the shown week. A bar before the first tracked week gives the first one.</summary>
    public int? TrendBar(int i)
    {
        var target = _weeks.WeeksBack(TrendBarStart(i));
        return target == WeeksBack ? null : target;
    }

    /// <summary>True when <paramref name="day"/> is one of the seven days shown.</summary>
    public bool Shows(DateOnly day) => day >= _shown && day < _shown.AddDays(7);

    public DayClick OnDayClicked(DateOnly day) => Shows(day) ? new DayClick(null) : new DayClick(_weeks.WeeksBack(day));
}
