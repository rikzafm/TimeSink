using Microsoft.Data.Sqlite;

using App = TimeSink.Core.Usage.UsageApp;
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
    private static readonly long[] HeatSeconds = [3600, 3 * 3600, 5 * 3600, 7 * 3600];


    /// <summary>Heat step 0-4 for a day total: under 1 h, 3 h, 5 h, 7 h, otherwise 7 h or more.</summary>
    public static int HeatLevel(long seconds)
    {
        for (var i = 0; i < HeatSeconds.Length; i++)
            if (seconds < HeatSeconds[i]) return i;
        return HeatSeconds.Length;
    }

    /// <summary>The <c>week.start</c> setting (Mon / Sun / Sat); anything else is Monday.</summary>
    public static DayOfWeek ParseWeekStart(string? value) => WeekStarts.Parse(value).ToDayOfWeek();

    public static MonthResult Run(string dbPath, DateTimeOffset now, TimeZoneInfo zone, int year, int month,
        DayOfWeek weekStart, IReadOnlyCollection<long>? running = null, long? onSinceUtc = null)
    {
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false,
        }.ToString());
        c.Open();
        using (var p = c.CreateCommand())
        {
            p.CommandText = "PRAGMA busy_timeout=2000";
            p.ExecuteNonQuery();
        }
        return Run(c, now, zone, year, month, weekStart, running, onSinceUtc);
    }

    public static MonthResult Run(SqliteConnection c, DateTimeOffset now, TimeZoneInfo zone, int year, int month,
        DayOfWeek weekStart, IReadOnlyCollection<long>? running = null, long? onSinceUtc = null)
    {
        var nowUtc = now.ToUnixTimeSeconds();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var first = new DateOnly(year, month, 1);
        var dim = DateTime.DaysInMonth(year, month);
        var isCurrent = today.Year == year && today.Month == month;
        var counted = isCurrent ? today.Day : first < today ? dim : 0;

        var prevFirst = first.AddMonths(-1);
        var prevDim = DateTime.DaysInMonth(prevFirst.Year, prevFirst.Month);
        var prevCounted = Math.Min(counted, prevDim);

        var apps = UsageLoader.LoadApps(c);
        var segs = UsageLoader.LoadSegments(c, DayStart(prevFirst, zone), nowUtc, apps, running, to: DayStart(first.AddMonths(1), zone));

        // PC uptime per local day: the headline numbers; per-app seconds below feed the day detail and the top apps.
        var uptime = UsageUptime.PerDay(segs, zone, nowUtc, onSinceUtc);

        // group -> seconds, per day of this month and of the comparison window.
        var days = new Dictionary<long, long>[dim];
        for (var d = 0; d < dim; d++) days[d] = DayTotals(segs, first.AddDays(d), zone);
        var prevTotals = new Dictionary<long, long>();
        long prevTotal = 0;
        for (var d = 0; d < prevCounted; d++)
        {
            foreach (var (g, s) in DayTotals(segs, prevFirst.AddDays(d), zone))
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
            var amounts = days[d]
                .Select(kv => new MonthAppAmount(kv.Key, NameOf(apps, kv.Key), kv.Value))
                .OrderBy(a => UsageRank.Tier(apps, a.AppId)).ThenByDescending(a => a.Seconds).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            var seconds = uptime.GetValueOrDefault(date);
            foreach (var a in amounts) monthTotals[a.AppId] = monthTotals.GetValueOrDefault(a.AppId) + a.Seconds;
            total += seconds;
            if (seconds > 0 && (best is null || seconds > best.Seconds)) best = new BestDay(date, seconds);
            monthDays.Add(new MonthDay(date, seconds, HeatLevel(seconds), date > today, date == today, amounts));
        }

        var top = monthTotals
            .Select(kv => (Id: kv.Key, Name: NameOf(apps, kv.Key), Seconds: kv.Value))
            .OrderBy(a => UsageRank.Tier(apps, a.Id)).ThenByDescending(a => a.Seconds).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(a =>
            {
                var prev = prevTotals.GetValueOrDefault(a.Id);
                return new MonthTopApp(a.Id, a.Name, a.Seconds, prev, a.Seconds - prev, prev > 0 ? (a.Seconds - prev) / (double)prev : null);
            }).ToList();

        var lead = ((int)first.DayOfWeek - (int)weekStart + 7) % 7;
        var weeks = new List<MonthWeek>();
        for (var offset = -lead; offset < dim; offset += 7)
        {
            var cells = new List<MonthDay?>(7);
            for (var k = 0; k < 7; k++)
            {
                var idx = offset + k;
                cells.Add(idx >= 0 && idx < dim ? monthDays[idx] : null);
            }
            weeks.Add(new MonthWeek(cells, cells.Sum(x => x?.Seconds ?? 0)));
        }

        var selected = isCurrent ? today : counted == 0 ? first : first.AddDays(dim - 1);
        return new MonthResult(year, month, first, dim, counted, isCurrent, weekStart, nowUtc, monthDays, weeks,
            total, prevTotal, total - prevTotal, prevTotal > 0 ? (total - prevTotal) / (double)prevTotal : null,
            counted > 0 ? total / (double)counted : 0, best, top, selected);
    }

    private static Dictionary<long, long> DayTotals(List<Seg> segs, DateOnly date, TimeZoneInfo zone)
    {
        var a = DayStart(date, zone);
        var b = DayStart(date.AddDays(1), zone);
        var d = new Dictionary<long, long>();
        foreach (var s in segs)
        {
            var o = Math.Min(s.End, b) - Math.Max(s.Start, a);
            if (o > 0) d[s.Group] = d.GetValueOrDefault(s.Group) + o;
        }
        return d;
    }

    private static string NameOf(Dictionary<long, App> apps, long group) => apps.TryGetValue(group, out var a) ? a.Name : group.ToString();


    private static long DayStart(DateOnly date, TimeZoneInfo zone) => LocalDays.StartOf(date, zone);
}
