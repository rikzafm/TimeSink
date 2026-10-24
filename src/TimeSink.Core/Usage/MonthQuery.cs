using Microsoft.Data.Sqlite;

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
    private const int TopCount = 4;
    private static readonly long[] HeatSeconds = [3600, 3 * 3600, 5 * 3600, 7 * 3600];

    private sealed record App(long Id, long Group, string Name);
    private readonly record struct Seg(long Group, long Start, long End);

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
        DayOfWeek weekStart, IReadOnlyCollection<long>? running = null)
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
        return Run(c, now, zone, year, month, weekStart, running);
    }

    public static MonthResult Run(SqliteConnection c, DateTimeOffset now, TimeZoneInfo zone, int year, int month,
        DayOfWeek weekStart, IReadOnlyCollection<long>? running = null)
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

        var apps = LoadApps(c);
        var segs = LoadSegments(c, DayStart(prevFirst, zone), DayStart(first.AddMonths(1), zone), nowUtc, apps, running);

        // group -> seconds, per day of this month and of the comparison window.
        var days = new Dictionary<long, long>[dim];
        for (var d = 0; d < dim; d++) days[d] = DayTotals(segs, first.AddDays(d), zone);
        var prevTotals = new Dictionary<long, long>();
        long prevTotal = 0;
        for (var d = 0; d < prevCounted; d++)
            foreach (var (g, s) in DayTotals(segs, prevFirst.AddDays(d), zone))
            {
                prevTotals[g] = prevTotals.GetValueOrDefault(g) + s;
                prevTotal += s;
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
                .OrderByDescending(a => a.Seconds).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            var seconds = amounts.Sum(a => a.Seconds);
            foreach (var a in amounts) monthTotals[a.AppId] = monthTotals.GetValueOrDefault(a.AppId) + a.Seconds;
            total += seconds;
            if (seconds > 0 && (best is null || seconds > best.Seconds)) best = new BestDay(date, seconds);
            monthDays.Add(new MonthDay(date, seconds, HeatLevel(seconds), date > today, date == today, amounts));
        }

        var top = monthTotals
            .Select(kv => (Id: kv.Key, Name: NameOf(apps, kv.Key), Seconds: kv.Value))
            .OrderByDescending(a => a.Seconds).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(TopCount)
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

    private static Dictionary<long, App> LoadApps(SqliteConnection c)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, COALESCE(canonical_id, id), display_name FROM apps WHERE state = 'tracked'";
        using var r = cmd.ExecuteReader();
        var d = new Dictionary<long, App>();
        while (r.Read()) d[r.GetInt64(0)] = new App(r.GetInt64(0), r.GetInt64(1), r.GetString(2));
        // A tracked alias whose canonical row is retired/absent still names its own group.
        foreach (var a in d.Values.ToList())
            if (!d.ContainsKey(a.Group) && a.Group != a.Id) d[a.Id] = a with { Group = a.Id };
        return d;
    }

    private static List<Seg> LoadSegments(SqliteConnection c, long from, long to, long nowUtc, Dictionary<long, App> apps,
        IReadOnlyCollection<long>? running)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT app_id, start_utc, end_utc, last_seen_utc FROM sessions
            WHERE start_utc <= $now AND start_utc < $to AND (end_utc IS NULL OR end_utc >= $from) ORDER BY start_utc
            """;
        cmd.Parameters.AddWithValue("$now", nowUtc);
        cmd.Parameters.AddWithValue("$to", to);
        cmd.Parameters.AddWithValue("$from", from);
        using var r = cmd.ExecuteReader();
        var list = new List<Seg>();
        while (r.Read())
        {
            var appId = r.GetInt64(0);
            if (!apps.TryGetValue(appId, out var app)) continue; // retired or unknown
            var start = r.GetInt64(1);
            long end;
            if (r.IsDBNull(2))
            {
                if (running is null || running.Contains(appId)) end = Math.Max(nowUtc, start);
                else end = Math.Clamp(r.GetInt64(3), start, nowUtc);
            }
            else end = r.GetInt64(2);
            list.Add(new Seg(app.Group, start, end));
        }
        return list;
    }

    private static long DayStart(DateOnly date, TimeZoneInfo zone) => LocalDays.StartOf(date, zone);
}
