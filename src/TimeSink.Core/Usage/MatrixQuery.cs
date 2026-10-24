using Microsoft.Data.Sqlite;

namespace TimeSink.Core.Usage;

/// <summary>
/// The Year "App x month" matrix (implementation.md 8.4, decisions 90-93), computed from <c>apps</c> and <c>sessions</c>
/// with the same rules as <see cref="YearQuery"/>: retired apps excluded, merged apps one row under the canonical app, an
/// open session counts to <c>now</c> if the Agent reports it running (or <c>running</c> is null), else to its heartbeat.
/// Columns run from the month tracking began (or January) to the current month.
/// </summary>
public static class MatrixQuery
{
    private const int TopApps = 3;
    private const int TrendWindow = 3;

    private sealed record App(long Id, long Group, string Name, long AddedUtc);
    private readonly record struct Seg(long Group, long Start, long End);

    /// <summary>Colour level of <paramref name="seconds"/> against the app's best month: under 30%, 55%, 75%, 90%, else top.</summary>
    public static int Level(long seconds, long best)
    {
        if (best <= 0 || seconds <= 0) return 0;
        var pct = seconds * 100;
        return pct < 30 * best ? 0 : pct < 55 * best ? 1 : pct < 75 * best ? 2 : pct < 90 * best ? 3 : 4;
    }

    public static MatrixResult Run(string dbPath, DateTimeOffset now, TimeZoneInfo zone, IReadOnlyCollection<long>? running = null)
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

    public static MatrixResult Run(SqliteConnection c, DateTimeOffset now, TimeZoneInfo zone, IReadOnlyCollection<long>? running = null)
    {
        var nowUtc = now.ToUnixTimeSeconds();
        var today = LocalDays.DateOf(nowUtc, zone);
        var year = today.Year;
        var apps = LoadApps(c);
        DateOnly? started = apps.Count == 0 ? null : LocalDays.DateOf(apps.Values.Min(a => a.AddedUtc), zone);
        if (started is null) return new MatrixResult(year, nowUtc, null, 1, [], [], 0);
        var first = started is { } s && s.Year == year ? s.Month : 1;
        var count = today.Month - first + 1;
        if (count < 1) return new MatrixResult(year, nowUtc, started, first, [], [], 0);

        var bounds = new long[count + 1];
        for (var i = 0; i <= count; i++) bounds[i] = LocalDays.StartOf(new DateOnly(year, first, 1).AddMonths(i), zone);

        var perApp = new Dictionary<long, long[]>();
        foreach (var seg in LoadSegments(c, bounds[0], nowUtc, apps, running))
        {
            for (var i = 0; i < count; i++)
            {
                var o = Math.Min(seg.End, bounds[i + 1]) - Math.Max(seg.Start, bounds[i]);
                if (o <= 0) continue;
                if (!perApp.TryGetValue(seg.Group, out var arr)) perApp[seg.Group] = arr = new long[count];
                arr[i] += o;
            }
        }

        var grand = perApp.Values.Sum(a => a.Sum());
        var rows = perApp.Where(kv => kv.Value.Sum() > 0)
            .Select(kv => BuildRow(kv.Key, NameOf(apps, kv.Key), kv.Value, first, grand))
            .OrderByDescending(r => r.TotalSeconds).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

        var months = new List<MatrixMonth>(count);
        for (var i = 0; i < count; i++)
        {
            var idx = i;
            var top = rows.Where(r => r.Cells[idx].Seconds > 0)
                .OrderByDescending(r => r.Cells[idx].Seconds).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(TopApps).Select(r => new YearAppShare(r.AppId, r.Name, r.Cells[idx].Seconds)).ToList();
            months.Add(new MatrixMonth(first + i, rows.Sum(r => r.Cells[idx].Seconds), top));
        }
        return new MatrixResult(year, nowUtc, started, first, months, rows, grand);
    }

    private static MatrixRow BuildRow(long id, string name, long[] secs, int firstMonth, long grand)
    {
        var best = secs.Max();
        var peak = Array.IndexOf(secs, best);
        var cells = secs.Select(v => new MatrixCell(v, Level(v, best))).ToList();
        var total = secs.Sum();

        double? trend = null;
        var lastFrom = Math.Max(0, secs.Length - TrendWindow);
        var prevFrom = Math.Max(0, lastFrom - TrendWindow);
        if (lastFrom > 0)
        {
            var last = secs.Skip(lastFrom).Sum();
            var prev = secs.Skip(prevFrom).Take(lastFrom - prevFrom).Sum();
            if (prev > 0) trend = (last / (double)prev - 1) * 100;
        }
        var share = grand <= 0 ? 0 : (int)Math.Round(total * 100.0 / grand, MidpointRounding.AwayFromZero);
        return new MatrixRow(id, name, cells, total, share, firstMonth + peak, best, trend);
    }

    private static string NameOf(Dictionary<long, App> apps, long group) => apps.TryGetValue(group, out var a) ? a.Name : group.ToString();

    private static Dictionary<long, App> LoadApps(SqliteConnection c)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, COALESCE(canonical_id, id), display_name, added_utc FROM apps WHERE state = 'tracked'";
        using var r = cmd.ExecuteReader();
        var d = new Dictionary<long, App>();
        while (r.Read()) d[r.GetInt64(0)] = new App(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetInt64(3));
        // A tracked alias whose canonical row is retired/absent still names its own group.
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
}
