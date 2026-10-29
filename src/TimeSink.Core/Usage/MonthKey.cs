namespace TimeSink.Core.Usage;

/// <summary>A calendar month (year and month 1-12). Ordered, with the month arithmetic the Year, Matrix and Wrapped queries need.</summary>
internal readonly record struct MonthKey(int Year, int Month) : IComparable<MonthKey>
{
    public static MonthKey Of(DateOnly day) => new(day.Year, day.Month);

    /// <summary>Months since January of year 0: consecutive months differ by 1.</summary>
    private int Number => Year * 12 + Month - 1;

    public DateOnly First => new(Year, Month, 1);

    public MonthKey AddMonths(int months)
    {
        var n = Number + months;
        return new MonthKey(n / 12, n % 12 + 1);
    }

    public MonthKey Prev => AddMonths(-1);

    /// <summary>The number of months from <paramref name="earlier"/> to this one (negative when this is the earlier).</summary>
    public int MonthsSince(MonthKey earlier) => Number - earlier.Number;

    public int CompareTo(MonthKey other) => Number.CompareTo(other.Number);

    public static bool operator <(MonthKey a, MonthKey b) => a.CompareTo(b) < 0;
    public static bool operator >(MonthKey a, MonthKey b) => a.CompareTo(b) > 0;
    public static bool operator <=(MonthKey a, MonthKey b) => a.CompareTo(b) <= 0;
    public static bool operator >=(MonthKey a, MonthKey b) => a.CompareTo(b) >= 0;
}
