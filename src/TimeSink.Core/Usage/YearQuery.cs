using Microsoft.Data.Sqlite;

namespace TimeSink.Core.Usage;

/// <summary>
/// The Year "Pace" numbers (implementation.md 8.4, decisions 66-71), computed from <c>apps</c> and <c>sessions</c> as a
/// snapshot at <c>now</c>. Same rules as <see cref="TodayQuery"/>: retired apps excluded, merged apps one group, an open
/// session counts to <c>now</c> if the Agent reports it running (or <c>running</c> is null), else to its heartbeat.
/// </summary>
public static class YearQuery
{
    private const int RecentMonths = 3;
    private const int TopApps = 4;

    private sealed record App(long Id, long Group, string Name, long AddedUtc);
    private readonly record struct Seg(long Group, long Start, long End);

    public static YearResult Run(string dbPath, DateTimeOffset now, TimeZoneInfo zone, IReadOnlyCollection<long>? running = null)
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
        return Run(c, now, zone, running);
    }

    public static YearResult Run(SqliteConnection c, DateTimeOffset now, TimeZoneInfo zone, IReadOnlyCollection<long>? running = null)
    {
        var nowUtc = now.ToUnixTimeSeconds();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var year = today.Year;
        var curIdx = Idx(today.Year, today.Month);
        var yearIdx = Idx(year, 1);
        var firstIdx = yearIdx - RecentMonths + 1; // two earlier months feed the "last 3 months" and January's delta

        var apps = LoadApps(c);
        var first = apps.Count == 0 ? (DateOnly?)null : LocalDate(apps.Values.Min(a => a.AddedUtc), zone);
        var startIdx = first is null ? int.MaxValue : Idx(first.Value.Year, first.Value.Month);
        var partialFirst = first is { Day: > 1 } ? startIdx : -1; // started mid-month: not a full month

        // Seconds per (group, month index) over the window [two months before the year .. now].
        var winFrom = MonthStart(firstIdx, zone);
        var segs = LoadSegments(c, winFrom, nowUtc, apps, running);
        var bounds = new long[curIdx - firstIdx + 2];
        for (var i = 0; i < bounds.Length; i++) bounds[i] = MonthStart(firstIdx + i, zone);
        var perMonth = new Dictionary<int, Dictionary<long, long>>();
        foreach (var s in segs)
        {
            for (var i = 0; i < bounds.Length - 1; i++)
            {
                var o = Math.Max(0, Math.Min(s.End, bounds[i + 1]) - Math.Max(s.Start, bounds[i]));
                if (o == 0) continue;
                if (!perMonth.TryGetValue(firstIdx + i, out var g)) perMonth[firstIdx + i] = g = new Dictionary<long, long>();
                g[s.Group] = g.GetValueOrDefault(s.Group) + o;
            }
        }
        long Secs(int idx) => perMonth.TryGetValue(idx, out var g) ? g.Values.Sum() : 0;

        // Current month projection: seconds / days elapsed since tracking (or month) began * days in month.
        long curSecs = Secs(curIdx);
        long curProj = curSecs;
        var hasPace = first is not null;
        if (hasPace)
        {
            var monthFirst = new DateOnly(today.Year, today.Month, 1);
            var from = first!.Value > monthFirst ? first.Value : monthFirst;
            var elapsed = Math.Max(1, today.DayNumber - from.DayNumber + 1);
            curProj = (long)Math.Round(curSecs / (double)elapsed * DateTime.DaysInMonth(today.Year, today.Month));
        }

        // Ghost value: mean of the last three months (the current one at its projection), skipping months before
        // tracking and the part-month tracking began in (unless that is the current month).
        var recent = new List<long>();
        for (var i = curIdx - RecentMonths + 1; i <= curIdx; i++)
        {
            if (i < startIdx) continue;
            if (i == curIdx) recent.Add(curProj);
            else if (i != partialFirst) recent.Add(Secs(i));
        }
        var ghost = recent.Count == 0 ? 0 : (long)Math.Round(recent.Sum() / (double)recent.Count);

        var months = new List<YearMonth>(12);
        for (var m = 1; m <= 12; m++)
        {
            var idx = Idx(year, m);
            var kind = idx < startIdx ? YearMonthKind.BeforeTracking
                : idx > curIdx ? YearMonthKind.Future
                : idx == curIdx ? YearMonthKind.Current : YearMonthKind.Tracked;
            var secs = kind is YearMonthKind.Tracked or YearMonthKind.Current ? Secs(idx) : 0;
            var proj = kind switch { YearMonthKind.Current => curProj, YearMonthKind.Future => ghost, _ => secs };

            long? prev = null;
            double? delta = null;
            if (kind is YearMonthKind.Tracked or YearMonthKind.Current && idx - 1 >= startIdx)
            {
                prev = Secs(idx - 1);
                if (prev > 0) delta = (proj / (double)prev.Value - 1) * 100;
            }

            var top = kind is YearMonthKind.Tracked or YearMonthKind.Current && perMonth.TryGetValue(idx, out var shares)
                ? shares.OrderByDescending(kv => kv.Value).ThenBy(kv => NameOf(apps, kv.Key), StringComparer.CurrentCultureIgnoreCase)
                    .Take(TopApps).Select(kv => new YearAppShare(kv.Key, NameOf(apps, kv.Key), kv.Value)).ToList()
                : new List<YearAppShare>();
            months.Add(new YearMonth(m, kind, secs, proj, delta, prev, top));
        }

        var total = months.Sum(m => m.Seconds);
        var fullMonths = months.Where(m => m.Kind == YearMonthKind.Tracked && Idx(year, m.Month) != partialFirst).ToList();
        long? average = fullMonths.Count == 0 ? null : (long)Math.Round(fullMonths.Average(m => (double)m.Seconds));

        var best = months.Where(m => m.Kind is YearMonthKind.Tracked or YearMonthKind.Current && m.Seconds > 0)
            .OrderByDescending(m => m.Seconds).FirstOrDefault();

        long? pace = hasPace
            ? total + (curProj - curSecs) + months.Where(m => m.Kind == YearMonthKind.Future).Sum(m => m.ProjectedSeconds)
            : null;

        return new YearResult(year, nowUtc, first, months, total, average, best?.Month, best?.Seconds ?? 0, pace,
            hasPace ? recent.Count : 0);
    }

    private static int Idx(int year, int month) => year * 12 + month - 1;

    private static string NameOf(Dictionary<long, App> apps, long group) => apps.TryGetValue(group, out var a) ? a.Name : group.ToString();

    private static Dictionary<long, App> LoadApps(SqliteConnection c)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, COALESCE(canonical_id, id), display_name, added_utc FROM apps WHERE state = 'tracked'";
        using var r = cmd.ExecuteReader();
        var d = new Dictionary<long, App>();
        while (r.Read()) d[r.GetInt64(0)] = new App(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetInt64(3));
        foreach (var a in d.Values.ToList())
            if (!d.ContainsKey(a.Group) && a.Group != a.Id) d[a.Id] = a with { Group = a.Id };
        return d;
    }

    private static List<Seg> LoadSegments(SqliteConnection c, long from, long nowUtc, Dictionary<long, App> apps, IReadOnlyCollection<long>? running)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT app_id, start_utc, end_utc, last_seen_utc FROM sessions
            WHERE start_utc <= $now AND (end_utc IS NULL OR end_utc >= $from) ORDER BY start_utc
            """;
        cmd.Parameters.AddWithValue("$now", nowUtc);
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
                end = running is null || running.Contains(appId) ? Math.Max(nowUtc, start) : Math.Clamp(r.GetInt64(3), start, nowUtc);
            else end = r.GetInt64(2);
            list.Add(new Seg(app.Group, start, end));
        }
        return list;
    }

    private static DateOnly LocalDate(long utc, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(utc), zone).DateTime);

    private static long MonthStart(int idx, TimeZoneInfo zone)
    {
        var local = new DateTime(idx / 12, idx % 12 + 1, 1);
        while (zone.IsInvalidTime(local)) local = local.AddHours(1);
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUnixTimeSeconds();
    }
}
