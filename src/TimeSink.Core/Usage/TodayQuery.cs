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
    public static TodayResult Run(UsageContext context, DateOnly? forDay = null) => UsageWindow.Over(context, w => Run(w, forDay));

    /// <param name="useSignIn">False compares like-for-like (the day's own first activity) instead of starting the uptime at sign-in.</param>
    internal static TodayResult Run(UsageWindow window, DateOnly? forDay = null, bool useSignIn = true)
    {
        var context = window.Context;
        var (now, zone, gamesOnly) = (context.Now, context.Zone, context.GamesOnly);
        var onSinceUtc = useSignIn ? context.SignInUtc : null; // when the PC became usable; starts the live day's uptime. Null: first tracked session.
        var nowUtc = now.ToUnixTimeSeconds();
        var today = window.Today;
        var date = forDay is { } want && want < today ? want : today;
        var isToday = date == today;
        var d0 = LocalDays.StartOf(date, zone);
        var d1 = LocalDays.StartOf(date.AddDays(1), zone);

        var apps = window.Apps;
        var segs = window.Segments(LocalDays.StartOf(date.AddDays(-TypicalWeeks * 7), zone));
        var byGroup = segs.ToLookup(s => s.Group);

        // PC uptime breakdown.
        var dayEnd = isToday ? nowUtc : d1;
        var breakdown = UsageUptime.Breakdown(segs, d0, dayEnd, isToday, onSinceUtc, gamesOnly, window.LocalDeviceId);
        var uptime = breakdown.UnionSeconds;
        var onBoth = breakdown.OnBothSeconds;

        // Hourly chart + typical day.
        var todayHours = UsageDays.HourSums(segs, date, zone);
        var start = TrackingStart.Of(apps, zone);
        var typical = new double[Hours];
        var typicalUptime = new double[Hours];
        var typicalDays = 0;
        for (var k = 1; k <= TypicalWeeks; k++)
        {
            var day = date.AddDays(-7 * k);
            if (!start.Counts(day)) continue;
            var sums = UsageDays.HourSums(segs, day, zone);
            for (var h = 0; h < Hours; h++) typical[h] += sums[h];

            var pastD0 = LocalDays.StartOf(day, zone);
            var pastD1 = LocalDays.StartOf(day.AddDays(1), zone);
            var pastBreakdown = UsageUptime.Breakdown(segs, pastD0, pastD1, false, null, gamesOnly, window.LocalDeviceId);
            foreach (var p in UsageDays.HourPieces(pastD0, pastD1, zone))
            {
                var h = p.Hour;
                if (h < 0 || h >= Hours) continue;
                foreach (var interval in pastBreakdown.UnionIntervals)
                {
                    var a = Math.Max(interval.StartUtc, p.Start);
                    var b = Math.Min(interval.EndUtc, p.End);
                    if (b > a) typicalUptime[h] += b - a;
                }
            }
            typicalDays++;
        }

        var hourUptime = new long[Hours];
        var hourlyDeviceUptime = new Dictionary<string, long>[Hours];
        var hourlyDeviceSeconds = new Dictionary<string, long>[Hours];
        var hourlyDeviceApps = new Dictionary<string, HashSet<long>>[Hours];
        for (var h = 0; h < Hours; h++)
        {
            hourlyDeviceUptime[h] = [];
            hourlyDeviceSeconds[h] = [];
            hourlyDeviceApps[h] = [];
        }

        foreach (var p in UsageDays.HourPieces(d0, d1, zone))
        {
            var h = p.Hour;
            if (h < 0 || h >= Hours) continue;
            foreach (var interval in breakdown.UnionIntervals)
            {
                var a = Math.Max(interval.StartUtc, p.Start);
                var b = Math.Min(interval.EndUtc, p.End);
                if (b > a) hourUptime[h] += b - a;
            }
            foreach (var (dev, intervals) in breakdown.DeviceIntervals)
            {
                long devSecs = 0;
                foreach (var interval in intervals)
                {
                    var a = Math.Max(interval.StartUtc, p.Start);
                    var b = Math.Min(interval.EndUtc, p.End);
                    if (b > a) devSecs += b - a;
                }
                if (devSecs > 0)
                    hourlyDeviceUptime[h][dev] = hourlyDeviceUptime[h].GetValueOrDefault(dev) + devSecs;
            }
        }

        var byGroupForHours = segs.GroupBy(s => s.Group);
        foreach (var groupSegs in byGroupForHours)
        {
            var byDev = groupSegs.GroupBy(s => string.IsNullOrEmpty(s.DeviceId) ? (apps.TryGetValue(s.AppId, out var ga) ? ga.DeviceId : "") : s.DeviceId);
            foreach (var devGroup in byDev)
            {
                var dev = devGroup.Key;
                var sorted = devGroup.Where(s => s.End > d0 && s.Start < d1).OrderBy(s => s.Start).ToList();
                var unioned = new List<(long Start, long End)>();
                foreach (var s in sorted)
                {
                    var st = Math.Max(s.Start, d0);
                    var en = Math.Min(s.End, d1);
                    if (en <= st) continue;
                    if (unioned.Count > 0 && st <= unioned[^1].End)
                        unioned[^1] = (unioned[^1].Start, Math.Max(unioned[^1].End, en));
                    else
                        unioned.Add((st, en));
                }
                foreach (var interval in unioned)
                {
                    foreach (var p in UsageDays.HourPieces(interval.Start, interval.End, zone))
                    {
                        var h = p.Hour;
                        if (h >= 0 && h < Hours)
                            hourlyDeviceSeconds[h][dev] = hourlyDeviceSeconds[h].GetValueOrDefault(dev) + (p.End - p.Start);
                    }
                }
            }
            foreach (var s in groupSegs)
            {
                var a = Math.Max(s.Start, d0);
                var b = Math.Min(s.End, d1);
                if (b <= a) continue;
                var dev = string.IsNullOrEmpty(s.DeviceId) ? (apps.TryGetValue(s.AppId, out var ga) ? ga.DeviceId : "") : s.DeviceId;
                foreach (var p in UsageDays.HourPieces(a, b, zone))
                {
                    var h = p.Hour;
                    if (h >= 0 && h < Hours)
                    {
                        if (!hourlyDeviceApps[h].TryGetValue(dev, out var appSet))
                            hourlyDeviceApps[h][dev] = appSet = [];
                        appSet.Add(s.AppId);
                    }
                }
            }
        }

        var hours = Enumerable.Range(0, Hours).Select(h =>
        {
            var byDev = new Dictionary<string, DeviceHour>();
            foreach (var dev in hourlyDeviceSeconds[h].Keys.Union(hourlyDeviceApps[h].Keys).Union(hourlyDeviceUptime[h].Keys))
            {
                var devSecs = hourlyDeviceSeconds[h].GetValueOrDefault(dev);
                var distinctApps = hourlyDeviceApps[h].TryGetValue(dev, out var set) ? set.Count : 0;
                var devUptime = hourlyDeviceUptime[h].GetValueOrDefault(dev);
                byDev[dev] = new DeviceHour(devSecs, distinctApps, devUptime);
            }
            return new HourBucket(
                h,
                hourUptime[h],
                todayHours[h],
                typicalDays == 0 ? 0 : typicalUptime[h] / typicalDays,
                byDev.Count > 0 ? byDev : null,
                typicalDays == 0 ? 0 : typical[h] / typicalDays);
        }).ToList();

        // Rows.
        var rows = new List<TodayAppRow>();
        foreach (var g in byGroup)
        {
            var groupSegs = g.ToList();
            var sessions = groupSegs.Where(s => UsageDays.Overlap(s, d0, d1) > 0 || (s.Open && s.End >= d0))
                .OrderBy(s => s.Start)
                .Select(s => new TodaySession(
                    Math.Max(s.Start, d0),
                    Math.Min(s.End, d1),
                    s.Open && isToday,
                    s.Start < d0 ? s.Start : null,
                    string.IsNullOrEmpty(s.DeviceId) ? (apps.TryGetValue(s.AppId, out var ga) ? ga.DeviceId : "") : s.DeviceId)).ToList();
            var open = isToday ? groupSegs.Where(s => s.Open).ToList() : [];
            var seconds = UsageUptime.OpenSeconds(groupSegs, d0, d1);
            if (seconds == 0 && open.Count == 0) continue;

            var devSecs = new Dictionary<string, long>();
            var byDev = groupSegs.GroupBy(s => string.IsNullOrEmpty(s.DeviceId) ? (apps.TryGetValue(s.AppId, out var ga) ? ga.DeviceId : "") : s.DeviceId);
            foreach (var devGroup in byDev)
            {
                var sec = UsageUptime.OpenSeconds(devGroup.ToList(), d0, d1);
                if (sec > 0)
                    devSecs[devGroup.Key] = sec;
            }
            long rowOnBoth = 0;
            if (devSecs.Count > 1)
            {
                var union = UsageUptime.OpenSeconds(groupSegs, d0, d1);
                rowOnBoth = Math.Max(0, devSecs.Values.Sum() - union);
            }

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
                avgSum += UsageUptime.OpenSeconds(groupSegs, a, b);
            }
            if (avgDays > 0) avg = avgSum / (double)avgDays;

            rows.Add(new TodayAppRow(
                g.Key, UsageRank.NameOf(apps, g.Key),
                seconds, open.Count > 0,
                open.Count > 0 ? open.Min(s => s.Start) : null,
                closedToday.Count > 0 ? closedToday.Max(s => s.End) : null,
                sessions, avg, avgDays,
                devSecs.Count > 0 ? devSecs : null, rowOnBoth));
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
        if (started != default) first = new FirstAppTile(started.Group, UsageRank.NameOf(apps, started.Group), started.Start);

        LongestSessionTile? longest = null;
        var best = segs.OrderByDescending(s => UsageDays.Overlap(s, d0, d1)).FirstOrDefault();
        if (best != default && UsageDays.Overlap(best, d0, d1) > 0) longest = new LongestSessionTile(best.Group, UsageRank.NameOf(apps, best.Group), UsageDays.Overlap(best, d0, d1));

        var total = rows.Sum(r => r.Seconds);
        var y0 = LocalDays.StartOf(date.AddDays(-1), zone);
        // Same wall-clock time yesterday (a DST change makes elapsed time differ from clock time).
        // A past day is compared with the whole day before it.
        var cut = isToday ? Math.Min(LocalDays.AtTime(date.AddDays(-1), TimeZoneInfo.ConvertTime(now, zone).TimeOfDay, zone), d0) : d0;
        var yesterdayByGroup = segs.GroupBy(s => s.Group);
        var yesterdayByNow = yesterdayByGroup.Sum(g => UsageUptime.OpenSeconds(g.ToList(), y0, cut));

        // PC uptime: shown from the sign-in time when known, but compared like-for-like (first activity) with yesterday.

        var devUptimes = new Dictionary<string, DeviceUptime>();
        foreach (var (devId, devSecs) in breakdown.DeviceSeconds)
        {
            var devSegs = segs.Where(s => (string.IsNullOrEmpty(s.DeviceId) ? (apps.TryGetValue(s.AppId, out var ga) ? ga.DeviceId : "") : s.DeviceId) == devId).ToList();
            var lastActive = devSegs.Count > 0 ? devSegs.Max(s => s.End) : (long?)null;
            var isLive = isToday && (devSegs.Any(s => s.Open) || (string.Equals(devId, window.LocalDeviceId, StringComparison.OrdinalIgnoreCase) && onSinceUtc != null));
            devUptimes[devId] = new DeviceUptime(devId, devSecs, lastActive, isLive);
        }

        // The previous day counts in full, so a day still in progress reads negative until it catches up.
        var vsYesterdayBreakdown = UsageUptime.Breakdown(segs, d0, dayEnd, isToday, null, gamesOnly, window.LocalDeviceId);
        var yesterdayBreakdown = UsageUptime.Breakdown(segs, y0, d0, false, null, gamesOnly, window.LocalDeviceId);
        var uptimeVs = vsYesterdayBreakdown.UnionSeconds - yesterdayBreakdown.UnionSeconds;

        var openSegs = isToday ? segs.Where(s => s.Open).ToList() : [];
        return new TodayResult(date, d0, d1, nowUtc, total,
            openSegs.Select(s => s.Group).Distinct().Count(), openSegs.Count,
            hours, typicalDays > 0, UsageUptime.SleepBands(segs, d0, Math.Min(nowUtc, d1), isToday ? window.LocalDeviceId : null), first, longest, total - yesterdayByNow, rows,
            uptime, uptimeVs, zone, start.FirstDay, gamesOnly, devUptimes.Count > 0 ? devUptimes : null, onBoth);
    }
}
