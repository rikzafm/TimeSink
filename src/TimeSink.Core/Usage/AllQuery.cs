using Microsoft.Data.Sqlite;
using TimeSink.Core.Apps;

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
        public int Sessions;
        public long LastUsed; // end of the latest session; "now" while it runs
        public readonly SortedSet<DateOnly> Days = [];
        public readonly Dictionary<(int, int), long> Months = [];
    }

    public static AllResult Run(string dbPath, DateTimeOffset now, TimeZoneInfo zone, IReadOnlyCollection<long>? running = null, long? onSinceUtc = null, bool gamesOnly = false)
    {
        using var c = UsageDb.OpenReadOnly(dbPath);
        return Run(c, now, zone, running, onSinceUtc, gamesOnly: gamesOnly);
    }

    public static AllResult Run(SqliteConnection c, DateTimeOffset now, TimeZoneInfo zone, IReadOnlyCollection<long>? running = null, long? onSinceUtc = null, bool gamesOnly = false)
    {
        var nowUtc = now.ToUnixTimeSeconds();
        var usageApps = AppGroups.Load(c, includeRetired: true, gamesOnly: gamesOnly);
        var groups = usageApps.Values.Where(a => a.Group == a.Id).ToDictionary(a => a.Id, a => new Group
        {
            Id = a.Id, Name = a.Name, Retired = a.Retired, RetiredUtc = a.RetiredUtc, Background = a.Background,
        });

        var loaded = UsageLoader.LoadSegments(c, 0, nowUtc, usageApps, running);
        var segs = new List<UsageSeg>();
        foreach (var seg in loaded)
        {
            if (seg.End <= seg.Start) continue;
            var group = groups[seg.Group];
            group.Sessions++;
            group.LastUsed = Math.Max(group.LastUsed, seg.End);
            group.Longest = Math.Max(group.Longest, seg.End - seg.Start);
            segs.Add(seg);
        }
        AddDays(groups, segs, zone);

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
            g.Days.Count > 0 ? g.Days.Min : null, BestMonth(g), g.Longest, g.Background, g.Sessions, g.LastUsed)).ToList();
        var retired = groups.Values.Where(g => g.Retired && g.Seconds >= TimeSink.Core.Apps.AppLibrary.MinRetiredSeconds).OrderByDescending(g => g.RetiredUtc).ThenBy(g => g.Id).Select(g => new AllRetiredRow(
            g.Id, g.Name, g.Seconds, g.Days.Count, Average(g.Seconds, g.Days.Count),
            LocalDays.DateOf(g.RetiredUtc, zone))).ToList();

        // The footer total is PC uptime, not the app sum.
        var uptime = UsageUptime.PerDay(loaded.Where(s => !usageApps[s.AppId].Retired).ToList(), zone, nowUtc, onSinceUtc, gamesOnly).Values.Sum();
        return new AllResult(apps, retired, new AllTotals(uptime, trackedDays.Count, Average(uptime, trackedDays.Count)));
    }

    private static void AddDays(Dictionary<long, Group> groups, List<UsageSeg> segs, TimeZoneInfo zone)
    {
        foreach (var slice in UsageDays.Slices(segs, zone))
        {
            var g = groups[slice.Seg.Group];
            var s = slice.End - slice.Start;
            g.Seconds += s;
            g.Days.Add(slice.Day);
            var key = (slice.Day.Year, slice.Day.Month);
            g.Months[key] = g.Months.GetValueOrDefault(key) + s;
        }
    }

    private static double Average(long seconds, int days) => days > 0 ? seconds / (double)days : 0;

    private static AllBestMonth? BestMonth(Group g) =>
        g.Months.Count == 0 ? null
            : g.Months.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.Item1).ThenBy(kv => kv.Key.Item2)
                .Select(kv => new AllBestMonth(kv.Key.Item1, kv.Key.Item2, kv.Value)).First();
}
