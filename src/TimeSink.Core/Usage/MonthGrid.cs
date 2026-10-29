namespace TimeSink.Core.Usage;

/// <summary>
/// The calendar shape of one month for a chosen Week start: how many blank cells come before the 1st, the weekday of each
/// column and the month cut into rows of seven (a null cell is padding before the 1st or after the last day). It holds dates
/// only; each caller attaches its own numbers to them.
/// </summary>
public sealed class MonthGrid
{
    private MonthGrid(DateOnly first, int days, int lead, IReadOnlyList<DayOfWeek> weekdays, IReadOnlyList<IReadOnlyList<DateOnly?>> weeks)
    {
        First = first;
        Days = days;
        Lead = lead;
        Weekdays = weekdays;
        Weeks = weeks;
    }

    public DateOnly First { get; }

    public int Days { get; }

    /// <summary>The blank cells before the 1st: the index of the 1st in a flat run of cells.</summary>
    public int Lead { get; }

    /// <summary>The weekday of each of the seven columns, starting with the Week start.</summary>
    public IReadOnlyList<DayOfWeek> Weekdays { get; }

    /// <summary>Rows of seven cells: the dates of the month, null where the row reaches past either end.</summary>
    public IReadOnlyList<IReadOnlyList<DateOnly?>> Weeks { get; }

    public static MonthGrid Layout(int year, int month, DayOfWeek weekStart)
    {
        var first = new DateOnly(year, month, 1);
        var days = DateTime.DaysInMonth(year, month);
        var lead = WeekStarts.DaysIntoWeek(first, weekStart);
        var weekdays = Enumerable.Range(0, 7).Select(i => (DayOfWeek)(((int)weekStart + i) % 7)).ToList();
        var weeks = new List<IReadOnlyList<DateOnly?>>();
        for (var offset = -lead; offset < days; offset += 7)
        {
            var cells = new List<DateOnly?>(7);
            for (var k = 0; k < 7; k++)
            {
                var idx = offset + k;
                cells.Add(idx >= 0 && idx < days ? first.AddDays(idx) : null);
            }
            weeks.Add(cells);
        }
        return new MonthGrid(first, days, lead, weekdays, weeks);
    }
}
