using Microsoft.Data.Sqlite;

using App = TimeSink.Core.Usage.UsageApp;
using Seg = TimeSink.Core.Usage.UsageSeg;

namespace TimeSink.Core.Usage;

/// <summary>
/// The Today view's numbers (implementation.md 8.3), computed from <c>apps</c> and <c>sessions</c> as a snapshot at
/// <c>now</c>. Retired apps are excluded; merged apps are one row under their canonical app. An open session counts up
/// to <c>now</c> if the Agent reports it as running (or when <c>running</c> is null: trust the database), otherwise
/// only up to its last heartbeat.
/// </summary>
public static class TodayQuery
{
    private const int TypicalWeeks = 8;
    private const int AverageDays = 7;
    private const int Hours = 24;


    public static TodayResult Run(string dbPath, DateTimeOffset now, TimeZoneInfo zone, IReadOnlyCollection<long>? running = null)
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

    public static TodayResult Run(SqliteConnection c, DateTimeOffset now, TimeZoneInfo zone, IReadOnlyCollection<long>? running = null)
    {
        var nowUtc = now.ToUnixTimeSeconds();
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var d0 = DayStart(date, zone);
        var d1 = DayStart(date.AddDays(1), zone);

        var apps = UsageLoader.LoadApps(c);
        var segs = UsageLoader.LoadSegments(c, DayStart(date.AddDays(-TypicalWeeks * 7), zone), nowUtc, apps, running);
        var byGroup = segs.ToLookup(s => s.Group);

        // Hourly chart + typical day.
        var todayHours = HourSums(segs, d0, d1);
        var firstDate = apps.Count == 0 ? (DateOnly?)null : LocalDate(apps.Values.Min(a => a.AddedUtc), zone);
        var typical = new double[Hours];
        var typicalDays = 0;
        for (var k = 1; k <= TypicalWeeks; k++)
        {
            var day = date.AddDays(-7 * k);
            if (firstDate is null || day < firstDate) continue;
            var sums = HourSums(segs, DayStart(day, zone), DayStart(day.AddDays(1), zone));
            for (var h = 0; h < Hours; h++) typical[h] += sums[h];
            typicalDays++;
        }
        var hours = Enumerable.Range(0, Hours)
            .Select(h => new HourBucket(h, todayHours[h], typicalDays == 0 ? 0 : typical[h] / typicalDays)).ToList();

        // Rows.
        var rows = new List<TodayAppRow>();
        foreach (var g in byGroup)
        {
            var groupSegs = g.ToList();
            var sessions = groupSegs.Where(s => Overlap(s, d0, d1) > 0 || (s.Open && s.End >= d0))
                .OrderBy(s => s.Start)
                .Select(s => new TodaySession(Math.Max(s.Start, d0), Math.Min(s.End, d1), s.Open)).ToList();
            var open = groupSegs.Where(s => s.Open).ToList();
            var seconds = groupSegs.Sum(s => Overlap(s, d0, d1));
            if (seconds == 0 && open.Count == 0) continue;

            var closedToday = groupSegs.Where(s => !s.Open && s.End >= d0 && s.End < d1).ToList();
            var added = LocalDate(apps.Values.Where(a => a.Group == g.Key).Min(a => a.AddedUtc), zone);
            double? avg = null;
            var avgDays = 0;
            long avgSum = 0;
            for (var k = 1; k <= AverageDays; k++)
            {
                var day = date.AddDays(-k);
                if (day < added) continue;
                avgDays++;
                var a = DayStart(day, zone);
                var b = DayStart(day.AddDays(1), zone);
                avgSum += groupSegs.Sum(s => Overlap(s, a, b));
            }
            if (avgDays > 0) avg = avgSum / (double)avgDays;

            rows.Add(new TodayAppRow(
                g.Key, apps.TryGetValue(g.Key, out var root) ? root.Name : groupSegs[0].AppId.ToString(),
                seconds, open.Count > 0,
                open.Count > 0 ? open.Min(s => s.Start) : null,
                closedToday.Count > 0 ? closedToday.Max(s => s.End) : null,
                sessions, avg, avgDays));
        }
        rows = rows.OrderByDescending(r => r.Seconds).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

        // Tiles.
        FirstAppTile? first = null;
        var started = segs.Where(s => s.Start >= d0 && s.Start < d1).OrderBy(s => s.Start).ThenBy(s => s.AppId).FirstOrDefault();
        if (started != default) first = new FirstAppTile(NameOf(apps, started.Group), started.Start);

        LongestSessionTile? longest = null;
        var best = segs.OrderByDescending(s => Overlap(s, d0, d1)).FirstOrDefault();
        if (best != default && Overlap(best, d0, d1) > 0) longest = new LongestSessionTile(NameOf(apps, best.Group), Overlap(best, d0, d1));

        var total = rows.Sum(r => r.Seconds);
        var y0 = DayStart(date.AddDays(-1), zone);
        var yesterdayByNow = segs.Sum(s => Overlap(s, y0, y0 + (nowUtc - d0)));

        var openSegs = segs.Where(s => s.Open).ToList();
        return new TodayResult(date, d0, d1, nowUtc, total,
            openSegs.Select(s => s.Group).Distinct().Count(), openSegs.Count,
            hours, typicalDays > 0, SleepBands(segs, d0, nowUtc), first, longest, total - yesterdayByNow, rows);
    }

    private static string NameOf(Dictionary<long, App> apps, long group) => apps.TryGetValue(group, out var a) ? a.Name : group.ToString();


    private static long Overlap(Seg s, long a, long b) => Math.Max(0, Math.Min(s.End, b) - Math.Max(s.Start, a));

    /// <summary>App-seconds per hour column. Columns are elapsed hours from local midnight; hour 23 absorbs a 25-hour day's extra hour.</summary>
    private static long[] HourSums(List<Seg> segs, long d0, long d1)
    {
        var sums = new long[Hours];
        for (var h = 0; h < Hours; h++)
        {
            var a = d0 + 3600L * h;
            var b = h == Hours - 1 ? d1 : Math.Min(d0 + 3600L * (h + 1), d1);
            if (a >= b) continue;
            foreach (var s in segs) sums[h] += Overlap(s, a, b);
        }
        return sums;
    }

    /// <summary>From each sleep-close to the next session start of any app (or now), merged and clipped to today.</summary>
    private static List<SleepBand> SleepBands(List<Seg> segs, long d0, long nowUtc)
    {
        var bands = new List<(long S, long E)>();
        foreach (var s in segs.Where(s => s.Sleep && s.End >= d0 && s.End <= nowUtc))
        {
            var next = segs.Where(x => x.Start > s.End).Select(x => x.Start).DefaultIfEmpty(nowUtc).Min();
            bands.Add((s.End, Math.Min(next, nowUtc)));
        }
        var merged = new List<SleepBand>();
        foreach (var (s, e) in bands.OrderBy(b => b.S))
        {
            if (e <= s) continue;
            if (merged.Count > 0 && s <= merged[^1].EndUtc)
                merged[^1] = merged[^1] with { EndUtc = Math.Max(merged[^1].EndUtc, e) };
            else merged.Add(new SleepBand(s, e));
        }
        return merged;
    }

    private static DateOnly LocalDate(long utc, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(utc), zone).DateTime);

    /// <summary>UTC unix seconds of local midnight (DST-aware; a skipped midnight moves to the first valid hour).</summary>
    private static long DayStart(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        while (zone.IsInvalidTime(local)) local = local.AddHours(1);
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUnixTimeSeconds();
    }
}
