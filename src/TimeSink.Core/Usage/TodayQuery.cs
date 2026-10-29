using Microsoft.Data.Sqlite;
using TimeSink.Core.Apps;

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


    /// <param name="forDay">The local day to show; null or any day after today means today (the live day).</param>
    /// <param name="onSinceUtc">When the PC became usable (sign-in); starts the live day's uptime. Null: first tracked session.</param>
    public static TodayResult Run(string dbPath, DateTimeOffset now, TimeZoneInfo zone, IReadOnlyCollection<long>? running = null, DateOnly? forDay = null, long? onSinceUtc = null)
    {
        using var c = UsageDb.OpenReadOnly(dbPath);
        return Run(c, now, zone, running, forDay, onSinceUtc);
    }

    public static TodayResult Run(SqliteConnection c, DateTimeOffset now, TimeZoneInfo zone, IReadOnlyCollection<long>? running = null, DateOnly? forDay = null, long? onSinceUtc = null)
    {
        var nowUtc = now.ToUnixTimeSeconds();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var date = forDay is { } want && want < today ? want : today;
        var isToday = date == today;
        var d0 = LocalDays.StartOf(date, zone);
        var d1 = LocalDays.StartOf(date.AddDays(1), zone);

        var apps = AppGroups.Load(c);
        var segs = UsageLoader.LoadSegments(c, LocalDays.StartOf(date.AddDays(-TypicalWeeks * 7), zone), nowUtc, apps, running);
        var byGroup = segs.ToLookup(s => s.Group);

        // Hourly chart + typical day.
        var todayHours = UsageDays.HourSums(segs, date, zone);
        var start = TrackingStart.Of(apps, zone);
        var typical = new double[Hours];
        var typicalDays = 0;
        for (var k = 1; k <= TypicalWeeks; k++)
        {
            var day = date.AddDays(-7 * k);
            if (!start.Counts(day)) continue;
            var sums = UsageDays.HourSums(segs, day, zone);
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
            var sessions = groupSegs.Where(s => UsageDays.Overlap(s, d0, d1) > 0 || (s.Open && s.End >= d0))
                .OrderBy(s => s.Start)
                .Select(s => new TodaySession(Math.Max(s.Start, d0), Math.Min(s.End, d1), s.Open && isToday)).ToList();
            var open = isToday ? groupSegs.Where(s => s.Open).ToList() : [];
            var seconds = groupSegs.Sum(s => UsageDays.Overlap(s, d0, d1));
            if (seconds == 0 && open.Count == 0) continue;

            var closedToday = groupSegs.Where(s => !s.Open && s.End >= d0 && s.End < d1).ToList();
            var added = LocalDays.DateOf(apps.Values.Where(a => a.Group == g.Key).Min(a => a.AddedUtc), zone);
            double? avg = null;
            var avgDays = 0;
            long avgSum = 0;
            for (var k = 1; k <= AverageDays; k++)
            {
                var day = date.AddDays(-k);
                if (day < added) continue;
                avgDays++;
                var a = LocalDays.StartOf(day, zone);
                var b = LocalDays.StartOf(day.AddDays(1), zone);
                avgSum += groupSegs.Sum(s => UsageDays.Overlap(s, a, b));
            }
            if (avgDays > 0) avg = avgSum / (double)avgDays;

            rows.Add(new TodayAppRow(
                g.Key, UsageRank.NameOf(apps, g.Key),
                seconds, open.Count > 0,
                open.Count > 0 ? open.Min(s => s.Start) : null,
                closedToday.Count > 0 ? closedToday.Max(s => s.End) : null,
                sessions, avg, avgDays));
        }
        // The live day lists running apps first (most time first), then closed ones by most recent close; Background apps
        // stay after the rest within each group. A past day keeps the plain time ranking.
        rows = isToday
            ? rows.OrderByDescending(r => r.IsRunning).ThenBy(r => UsageRank.IsBackground(apps, r.AppId))
                .ThenByDescending(r => r.IsRunning ? r.Seconds : r.LastClosedUtc ?? 0)
                .ThenByDescending(r => r.Seconds).ThenBy(r => r.Name, UsageRank.ByName).ToList()
            : UsageRank.Rank(rows, apps, r => r.AppId, r => r.Seconds);

        // Tiles.
        FirstAppTile? first = null;
        var started = segs.Where(s => s.Start >= d0 && s.Start < d1).OrderBy(s => s.Start).ThenBy(s => s.AppId).FirstOrDefault();
        if (started != default) first = new FirstAppTile(UsageRank.NameOf(apps, started.Group), started.Start);

        LongestSessionTile? longest = null;
        var best = segs.OrderByDescending(s => UsageDays.Overlap(s, d0, d1)).FirstOrDefault();
        if (best != default && UsageDays.Overlap(best, d0, d1) > 0) longest = new LongestSessionTile(best.Group, UsageRank.NameOf(apps, best.Group), UsageDays.Overlap(best, d0, d1));

        var total = rows.Sum(r => r.Seconds);
        var y0 = LocalDays.StartOf(date.AddDays(-1), zone);
        // Same wall-clock time yesterday (a DST change makes elapsed time differ from clock time).
        // A past day is compared with the whole day before it.
        var cut = isToday ? Math.Min(LocalDays.AtTime(date.AddDays(-1), TimeZoneInfo.ConvertTime(now, zone).TimeOfDay, zone), d0) : d0;
        var yesterdayByNow = segs.Sum(s => UsageDays.Overlap(s, y0, cut));

        // PC uptime: shown from the sign-in time when known, but compared like-for-like (first activity) with yesterday.
        var dayEnd = isToday ? nowUtc : d1;
        var uptime = UsageUptime.Seconds(segs, d0, dayEnd, isToday, onSinceUtc);
        // The previous day counts in full, so a day still in progress reads negative until it catches up.
        var uptimeVs = UsageUptime.Seconds(segs, d0, dayEnd, isToday, null) - UsageUptime.Seconds(segs, y0, d0, false, null);

        var openSegs = isToday ? segs.Where(s => s.Open).ToList() : [];
        return new TodayResult(date, d0, d1, nowUtc, total,
            openSegs.Select(s => s.Group).Distinct().Count(), openSegs.Count,
            hours, typicalDays > 0, UsageUptime.SleepBands(segs, d0, Math.Min(nowUtc, d1)), first, longest, total - yesterdayByNow, rows,
            uptime, uptimeVs, start.FirstDay);
    }
}
