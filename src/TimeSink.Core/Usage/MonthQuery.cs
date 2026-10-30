using Microsoft.Data.Sqlite;
using TimeSink.Core.Apps;

using Seg = TimeSink.Core.Usage.UsageSeg;

namespace TimeSink.Core.Usage;

/// <summary>
/// The Month view's numbers (implementation.md 8.3), computed from <c>apps</c> and <c>sessions</c> like
/// <see cref="TodayQuery"/> (decision 56): retired apps excluded, merged apps one entry under the canonical app,
/// sessions clipped to local days, an open session counted to <c>now</c> when the Agent reports it running (or when
/// <c>running</c> is null) and otherwise only to its heartbeat.
/// "Days counted" is the elapsed days of the month (through today for the current month, all for a past month, none
/// for a future one); the previous-month comparison covers the same number of days (see docs/decisions.md).
/// </summary>
public static class MonthQuery
{
    /// <summary>The <c>week.start</c> setting (Mon / Sun / Sat); anything else is Monday.</summary>
    public static DayOfWeek ParseWeekStart(string? value) => WeekStarts.Parse(value).ToDayOfWeek();

    public static MonthResult Run(UsageContext context, int year, int month) => UsageWindow.Over(context, w => Run(w, year, month));

    internal static MonthResult Run(UsageWindow window, int year, int month)
    {
        var context = window.Context;
        var (now, zone, gamesOnly) = (context.Now, context.Zone, context.GamesOnly);
        var (weekStart, onSinceUtc) = (context.WeekStartDay, context.SignInUtc);
        var nowUtc = now.ToUnixTimeSeconds();
        var today = window.Today;
        var first = new DateOnly(year, month, 1);
        var dim = DateTime.DaysInMonth(year, month);
        var isCurrent = today.Year == year && today.Month == month;
        var counted = isCurrent ? today.Day : first < today ? dim : 0;

        var prevFirst = first.AddMonths(-1);
        var prevDim = DateTime.DaysInMonth(prevFirst.Year, prevFirst.Month);
        var prevCounted = Math.Min(counted, prevDim);

        var apps = window.Apps;
        var start = TrackingStart.Of(apps, zone);
        var segs = window.Segments(DayStart(prevFirst, zone), to: DayStart(first.AddMonths(1), zone));

        // PC uptime per local day: the headline numbers; per-app seconds below feed the day detail and the top apps.
        var uptime = UsageUptime.PerDay(segs, zone, nowUtc, onSinceUtc, gamesOnly);

        // group -> seconds, per day of this month and of the comparison window.
        var perDay = UsageDays.PerDayGroup(segs, zone);
        Dictionary<long, long> DayTotals(DateOnly date) => perDay.GetValueOrDefault(date) ?? [];
        var days = new Dictionary<long, long>[dim];
        for (var d = 0; d < dim; d++) days[d] = DayTotals(first.AddDays(d));
        var prevTotals = new Dictionary<long, long>();
        long prevTotal = 0;
        for (var d = 0; d < prevCounted; d++)
        {
            foreach (var (g, s) in DayTotals(prevFirst.AddDays(d)))
                prevTotals[g] = prevTotals.GetValueOrDefault(g) + s;
            prevTotal += uptime.GetValueOrDefault(prevFirst.AddDays(d));
        }

        var monthDays = new List<MonthDay>(dim);
        var monthTotals = new Dictionary<long, long>();
        long total = 0;
        BestDay? best = null;
        for (var d = 0; d < dim; d++)
        {
            var date = first.AddDays(d);
            var amounts = UsageRank.Rank(days[d].Select(kv => new MonthAppAmount(kv.Key, UsageRank.NameOf(apps, kv.Key), kv.Value)),
                apps, a => a.AppId, a => a.Seconds);
            var seconds = uptime.GetValueOrDefault(date);
            foreach (var a in amounts) monthTotals[a.AppId] = monthTotals.GetValueOrDefault(a.AppId) + a.Seconds;
            total += seconds;
            if (seconds > 0 && (best is null || seconds > best.Seconds)) best = new BestDay(date, seconds);
            monthDays.Add(new MonthDay(date, seconds, HeatScale.Level(seconds), date > today, date == today, amounts));
        }

        var top = UsageRank.Rank(monthTotals, apps, kv => kv.Key, kv => kv.Value)
            .Select(kv =>
            {
                var prev = prevTotals.GetValueOrDefault(kv.Key);
                return new MonthTopApp(kv.Key, UsageRank.NameOf(apps, kv.Key), kv.Value, prev, kv.Value - prev, Change.Between(kv.Value, prev));
            }).ToList();

        var weeks = MonthGrid.Layout(year, month, weekStart).Weeks
            .Select(row =>
            {
                var cells = row.Select(d => d is { } date ? monthDays[date.Day - 1] : null).ToList();
                return new MonthWeek(cells, cells.Sum(x => x?.Seconds ?? 0));
            }).ToList();

        // The daily average counts only the elapsed days from the Tracking start on (decision 147, replacing 78).
        var averageDays = Enumerable.Range(0, counted).Count(d => start.Counts(first.AddDays(d)));
        var selected = isCurrent ? today : counted == 0 ? first : first.AddDays(dim - 1);
        return new MonthResult(year, month, first, dim, counted, isCurrent, weekStart, nowUtc, monthDays, weeks,
            total, prevTotal, total - prevTotal, Change.Between(total, prevTotal),
            averageDays > 0 ? total / (double)averageDays : 0, best, top, selected, start.FirstDay);
    }

    private static long DayStart(DateOnly date, TimeZoneInfo zone) => LocalDays.StartOf(date, zone);
}
