using Microsoft.Data.Sqlite;

using App = TimeSink.Core.Usage.UsageApp;
using Seg = TimeSink.Core.Usage.UsageSeg;

namespace TimeSink.Core.Usage;

/// <summary>
/// The Year "Wrapped" numbers (implementation.md 8.3), year to date at <c>now</c>, from <c>apps</c> and <c>sessions</c>.
/// Same rules as <see cref="YearQuery"/>: retired apps excluded, merged apps one group, an open session counts to
/// <c>now</c> if the Agent reports it running (or <c>running</c> is null), else to its heartbeat. Sessions are walked in
/// pieces that never cross a local hour (or day), so days, months, hour-of-day and "before noon" use local time.
/// (The piece length uses the offset at its start, so a DST change inside an hour is off by at most that hour.)
/// </summary>
public static class WrappedQuery
{
    private const int PodiumSize = 5;
    private const int OthersSize = 3;


    public static WrappedResult Run(string dbPath, DateTimeOffset now, TimeZoneInfo zone, IReadOnlyCollection<long>? running = null, long? onSinceUtc = null)
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

    public static WrappedResult Run(SqliteConnection c, DateTimeOffset now, TimeZoneInfo zone, IReadOnlyCollection<long>? running = null, long? onSinceUtc = null)
    {
        var nowUtc = now.ToUnixTimeSeconds();
        var today = LocalDays.DateOf(nowUtc, zone);
        var year = today.Year;
        var yearStart = LocalDays.StartOf(new DateOnly(year, 1, 1), zone);
        var windowStart = LocalDays.StartOf(new DateOnly(year - 1, 12, 1), zone); // December feeds January's month delta

        var apps = UsageLoader.LoadApps(c);
        var first = apps.Count == 0 ? (DateOnly?)null : LocalDays.DateOf(apps.Values.Min(a => a.AddedUtc), zone);
        var groups = apps.Values.Select(a => a.Group).Distinct().ToList();

        var perDay = new Dictionary<int, long>();          // local day number -> seconds (this year)
        var perMonth = new Dictionary<int, long>();        // year * 12 + month - 1 -> seconds (window)
        var perHour = new long[24];                        // local hour of day -> seconds (this year)
        var perGroup = new Dictionary<long, long>();       // this year
        long beforeNoon = 0, total = 0;

        var segs = UsageLoader.LoadSegments(c, windowStart, nowUtc, apps, running);
        foreach (var s in segs)
        {
            var t = Math.Max(s.Start, windowStart);
            var end = Math.Min(s.End, nowUtc);
            while (t < end)
            {
                var offset = (long)zone.GetUtcOffset(DateTimeOffset.FromUnixTimeSeconds(t)).TotalSeconds;
                var localSecs = t + offset;
                var dayNumber = (int)((localSecs - Mod(localSecs, 86400)) / 86400);
                var secOfDay = (int)Mod(localSecs, 86400);
                var step = Math.Min(end - t, 3600 - secOfDay % 3600);
                var date = DateOnly.FromDayNumber(719162 + dayNumber); // 719162 = day number of 1970-01-01
                var monthIdx = date.Year * 12 + date.Month - 1;
                perMonth[monthIdx] = perMonth.GetValueOrDefault(monthIdx) + step;
                if (t >= yearStart && date.Year == year)
                {
                    perDay[dayNumber] = perDay.GetValueOrDefault(dayNumber) + step;
                    perHour[secOfDay / 3600] += step;
                    perGroup[s.Group] = perGroup.GetValueOrDefault(s.Group) + step;
                    if (secOfDay < 12 * 3600) beforeNoon += step;
                    total += step;
                }
                t += step;
            }
        }

        // The headline is PC uptime (decision 125); everything else here stays app-seconds.
        var uptime = UsageUptime.PerDay(segs, zone, nowUtc, onSinceUtc).Where(kv => kv.Key.Year == year).Sum(kv => kv.Value);

        // Background apps (Steam, Wallpaper Engine) stay out of every ranking here; their time still counts in the totals.
        var ranked = perGroup.Where(kv => UsageRank.Tier(apps, kv.Key) == 0).Select(kv => new WrappedApp(kv.Key, NameOf(apps, kv.Key), kv.Value))
            .OrderByDescending(a => a.Seconds).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

        WrappedKindTop TopOf(Apps.AppKind kind)
        {
            var list = ranked.Where(a => UsageRank.KindOf(apps, a.AppId) == kind).OrderByDescending(a => a.Seconds)
                .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            return new WrappedKindTop(list.Take(PodiumSize).ToList(), list.Skip(PodiumSize).Take(OthersSize).ToList(), list.Sum(a => a.Seconds));
        }

        // Longest run of consecutive local days with any tracked time (earliest wins a tie).
        int bestLen = 0, bestStartDay = 0, runLen = 0, runStart = 0, prevDay = int.MinValue;
        foreach (var day in perDay.Where(kv => kv.Value > 0).Select(kv => kv.Key).OrderBy(d => d))
        {
            if (day == prevDay + 1) runLen++;
            else { runLen = 1; runStart = day; }
            prevDay = day;
            if (runLen > bestLen) { bestLen = runLen; bestStartDay = runStart; }
        }

        int? busiest = null;
        long busiestSecs = 0;
        for (var m = 1; m <= 12; m++)
        {
            var secs = perMonth.GetValueOrDefault(year * 12 + m - 1);
            if (secs > busiestSecs) { busiest = m; busiestSecs = secs; }
        }
        double? delta = null;
        if (busiest is { } bm)
        {
            var prev = perMonth.GetValueOrDefault(year * 12 + bm - 2);
            if (prev > 0) delta = (busiestSecs / (double)prev - 1) * 100;
        }

        int? peak = null;
        long peakSecs = 0;
        for (var h = 0; h < 24; h++)
            if (perHour[h] > peakSecs) { peak = h; peakSecs = perHour[h]; }

        var daysTracked = 0;
        if (first is { } f)
        {
            var from = f > new DateOnly(year, 1, 1) ? f : new DateOnly(year, 1, 1);
            daysTracked = Math.Max(0, today.DayNumber - from.DayNumber + 1);
        }

        return new WrappedResult(
            year, nowUtc, first, uptime, total, daysTracked, perDay.Count(kv => kv.Value > 0), groups.Count,
            ranked.Take(PodiumSize).ToList(), ranked.Skip(PodiumSize).Take(OthersSize).ToList(),
            bestLen, bestLen == 0 ? null : DateOnly.FromDayNumber(719162 + bestStartDay),
            bestLen == 0 ? null : DateOnly.FromDayNumber(719162 + bestStartDay + bestLen - 1),
            busiest, busiestSecs, delta, peak, peakSecs,
            total == 0 ? 0 : (int)Math.Round(beforeNoon * 100.0 / total, MidpointRounding.AwayFromZero),
            TopOf(Apps.AppKind.Game), TopOf(Apps.AppKind.Gacha));
    }

    private static long Mod(long a, long m) => (a % m + m) % m;

    private static string NameOf(Dictionary<long, App> apps, long group) => apps.TryGetValue(group, out var a) ? a.Name : group.ToString();

}
