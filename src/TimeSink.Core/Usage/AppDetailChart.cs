using System.Globalization;

namespace TimeSink.Core.Usage;

/// <summary>How far back the app detail bar chart looks: the whole history, or one year, month or week.</summary>
public enum AppDetailRange { All, Year, Month, Week }

/// <summary>One bar: its label and the time in it. <see cref="From"/> to <see cref="To"/> (inclusive) is the span it covers.</summary>
public sealed record AppDetailBar(string Label, long Seconds, DateOnly From, DateOnly To);

/// <summary>
/// The bar chart and the time-of-day histogram of one period. <see cref="OpensByHour"/> counts session starts per local
/// hour (0-23) inside the period and <see cref="PeakHour"/> is the busiest of them (the earliest on a tie), null with none.
/// </summary>
public sealed record AppDetailChartData(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<AppDetailBar> Bars,
    long TotalSeconds,
    IReadOnlyList<int> OpensByHour,
    int? PeakHour,
    bool CanPrev,
    bool CanNext);

/// <summary>
/// Pure bucketing for the app detail bar chart: All is a bar per year, Year a bar per month, Month and Week a bar per day.
/// The period is the one containing the anchor date, clamped between the first tracked day and today.
/// </summary>
public static class AppDetailChart
{
    private static readonly CultureInfo En = CultureInfo.InvariantCulture;

    public static AppDetailChartData Build(
        AppDetailRange range, DateOnly anchor, DateOnly today, DateOnly? firstTracked, DayOfWeek weekStart,
        IReadOnlyDictionary<DateOnly, long> daySeconds, IReadOnlyList<AppDetailOpen> opens)
    {
        var first = firstTracked ?? today;
        var nav = new PeriodNavigator(UnitOf(range), first, today, weekStart);
        anchor = nav.Clamp(anchor);
        var (from, to) = Period(range, anchor, today, first, weekStart);

        var bars = new List<AppDetailBar>();
        switch (range)
        {
            case AppDetailRange.All:
                for (var y = from.Year; y <= to.Year; y++)
                    bars.Add(Bar(y.ToString(En), new DateOnly(y, 1, 1), new DateOnly(y, 12, 31), daySeconds));
                break;
            case AppDetailRange.Year:
                for (var m = 1; m <= 12; m++)
                {
                    var start = new DateOnly(from.Year, m, 1);
                    bars.Add(Bar(start.ToString("MMM", En), start, start.AddMonths(1).AddDays(-1), daySeconds));
                }
                break;
            default:
                for (var d = from; d <= to; d = d.AddDays(1))
                    bars.Add(Bar(range == AppDetailRange.Week ? d.ToString("ddd", En) : d.Day.ToString(En), d, d, daySeconds));
                break;
        }

        var byHour = new int[24];
        foreach (var o in opens)
            if (o.Date >= from && o.Date <= to) byHour[o.Hour]++;
        int? peak = null;
        for (var h = 0; h < 24; h++)
            if (byHour[h] > 0 && (peak is null || byHour[h] > byHour[peak.Value])) peak = h;

        var canPrev = range != AppDetailRange.All && nav.CanPrev(anchor);
        var canNext = range != AppDetailRange.All && nav.CanNext(anchor);
        return new AppDetailChartData(from, to, bars, bars.Sum(b => b.Seconds), byHour, peak, canPrev, canNext);
    }

    /// <summary>The anchor one period earlier or later (dir -1 or +1); All has a single period.</summary>
    public static DateOnly Step(AppDetailRange range, DateOnly anchor, int dir) =>
        range == AppDetailRange.All ? anchor : PeriodNavigator.StepBy(UnitOf(range), anchor, dir);

    /// <summary>The range the cycle button moves to: All, Year, Month, Week, then All again.</summary>
    public static AppDetailRange NextRange(AppDetailRange range) =>
        (AppDetailRange)(((int)range + 1) % Enum.GetValues<AppDetailRange>().Length);

    private static PeriodUnit UnitOf(AppDetailRange range) => range switch
    {
        AppDetailRange.Year => PeriodUnit.Year,
        AppDetailRange.Month => PeriodUnit.Month,
        AppDetailRange.Week => PeriodUnit.Week,
        _ => PeriodUnit.Day, // All has a single period: only its anchor is clamped
    };

    private static (DateOnly From, DateOnly To) Period(AppDetailRange range, DateOnly anchor, DateOnly today, DateOnly first, DayOfWeek weekStart)
    {
        switch (range)
        {
            case AppDetailRange.All:
                return (new DateOnly(first.Year, 1, 1), new DateOnly(today.Year, 12, 31));
            case AppDetailRange.Year:
                return (new DateOnly(anchor.Year, 1, 1), new DateOnly(anchor.Year, 12, 31));
            case AppDetailRange.Month:
                var m = new DateOnly(anchor.Year, anchor.Month, 1);
                return (m, m.AddMonths(1).AddDays(-1));
            default:
                var back = ((int)anchor.DayOfWeek - (int)weekStart + 7) % 7;
                var w = anchor.AddDays(-back);
                return (w, w.AddDays(6));
        }
    }

    private static AppDetailBar Bar(string label, DateOnly from, DateOnly to, IReadOnlyDictionary<DateOnly, long> daySeconds)
    {
        long seconds = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
            seconds += daySeconds.GetValueOrDefault(d);
        return new AppDetailBar(label, seconds, from, to);
    }
}
