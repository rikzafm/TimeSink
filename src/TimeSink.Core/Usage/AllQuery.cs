using Microsoft.Data.Sqlite;

namespace TimeSink.Core.Usage;

/// <summary>
/// The All tab's lifetime table (implementation.md 8.3), computed from <c>apps</c> and <c>sessions</c> like
/// <see cref="MonthQuery"/> (decision 56): sessions are clipped to local days (DST-aware), merged apps are one row
/// under the canonical app, and an open session counts to <c>now</c> when the Agent reports it running (or when
/// <c>running</c> is null) and otherwise only to its heartbeat. Retired apps keep their own numbers in
/// <see cref="AllResult.Retired"/> and are left out of the totals and shares.
/// </summary>
public static class AllQuery
{
    private sealed class Group
    {
        public required long Id;
        public required string Name;
        public required bool Retired;
        public bool Background;
        public long RetiredUtc;
        public long Seconds;
        public long Longest;
        public readonly SortedSet<DateOnly> Days = [];
        public readonly Dictionary<(int, int), long> Months = [];
    }

    public static AllResult Run(string dbPath, DateTimeOffset now, TimeZoneInfo zone, IReadOnlyCollection<long>? running = null, long? onSinceUtc = null)
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
        return Run(c, now, zone, running, onSinceUtc);
    }

    public static AllResult Run(SqliteConnection c, DateTimeOffset now, TimeZoneInfo zone, IReadOnlyCollection<long>? running = null, long? onSinceUtc = null)
    {
        var nowUtc = now.ToUnixTimeSeconds();
        var groups = new Dictionary<long, Group>();
        var groupOf = new Dictionary<long, long>();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT id, canonical_id, display_name, state, retired_utc, is_background FROM apps ORDER BY id";
            using var r = cmd.ExecuteReader();
            var aliases = new List<(long Id, long Canonical)>();
            while (r.Read())
            {
                var id = r.GetInt64(0);
                if (!r.IsDBNull(1)) { aliases.Add((id, r.GetInt64(1))); continue; }
                groups[id] = new Group
                {
                    Id = id, Name = r.GetString(2), Retired = r.GetString(3) == "retired",
                    RetiredUtc = r.IsDBNull(4) ? 0 : r.GetInt64(4), Background = r.GetInt64(5) != 0,
                };
                groupOf[id] = id;
            }
            foreach (var (id, canonical) in aliases)
                if (groups.ContainsKey(canonical)) groupOf[id] = canonical;
        }

        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT app_id, start_utc, end_utc, last_seen_utc FROM sessions WHERE start_utc <= $now ORDER BY start_utc";
            cmd.Parameters.AddWithValue("$now", nowUtc);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var appId = r.GetInt64(0);
                if (!groupOf.TryGetValue(appId, out var gid)) continue;
                var start = r.GetInt64(1);
                long end;
                if (r.IsDBNull(2))
                {
                    if (running is null || running.Contains(appId)) end = Math.Max(nowUtc, start);
                    else end = Math.Clamp(r.GetInt64(3), start, nowUtc);
                }
                else end = r.GetInt64(2);
                if (end <= start) continue;
                Add(groups[gid], start, end, zone);
            }
        }

        // Day sets of the tracked groups for the total's "days used".
        var trackedDays = new HashSet<DateOnly>();
        long total = 0; // app time summed over tracked apps: the shares' denominator
        foreach (var g in groups.Values.Where(g => !g.Retired))
        {
            total += g.Seconds;
            trackedDays.UnionWith(g.Days);
        }

        var apps = groups.Values.Where(g => !g.Retired).OrderBy(g => g.Id).Select(g => new AllAppRow(
            g.Id, g.Name, g.Seconds, g.Days.Count, Average(g.Seconds, g.Days.Count), total > 0 ? g.Seconds / (double)total : 0,
            g.Days.Count > 0 ? g.Days.Min : null, BestMonth(g), g.Longest, g.Background)).ToList();
        var retired = groups.Values.Where(g => g.Retired).OrderByDescending(g => g.RetiredUtc).ThenBy(g => g.Id).Select(g => new AllRetiredRow(
            g.Id, g.Name, g.Seconds, g.Days.Count, Average(g.Seconds, g.Days.Count),
            LocalDays.DateOf(g.RetiredUtc, zone))).ToList();

        // The footer total is PC uptime, not the app sum.
        var trackedApps = UsageLoader.LoadApps(c);
        var uptime = UsageUptime.PerDay(UsageLoader.LoadSegments(c, 0, nowUtc, trackedApps, running), zone, nowUtc, onSinceUtc).Values.Sum();
        return new AllResult(apps, retired, new AllTotals(uptime, trackedDays.Count, Average(uptime, trackedDays.Count)));
    }

    private static double Average(long seconds, int days) => days > 0 ? seconds / (double)days : 0;

    private static AllBestMonth? BestMonth(Group g) =>
        g.Months.Count == 0 ? null
            : g.Months.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.Item1).ThenBy(kv => kv.Key.Item2)
                .Select(kv => new AllBestMonth(kv.Key.Item1, kv.Key.Item2, kv.Value)).First();

    private static void Add(Group g, long start, long end, TimeZoneInfo zone)
    {
        g.Longest = Math.Max(g.Longest, end - start);
        var day = LocalDays.DateOf(start, zone);
        var last = LocalDays.DateOf(end - 1, zone);
        while (day <= last)
        {
            var a = Math.Max(start, LocalDays.StartOf(day, zone));
            var b = Math.Min(end, LocalDays.StartOf(day.AddDays(1), zone));
            if (b > a)
            {
                var s = b - a;
                g.Seconds += s;
                g.Days.Add(day);
                var key = (day.Year, day.Month);
                g.Months[key] = g.Months.GetValueOrDefault(key) + s;
            }
            day = day.AddDays(1);
        }
    }
}
