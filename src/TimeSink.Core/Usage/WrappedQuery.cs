using Microsoft.Data.Sqlite;
using TimeSink.Core.Apps;

using Seg = TimeSink.Core.Usage.UsageSeg;

namespace TimeSink.Core.Usage;

/// <summary>
/// The Year "Wrapped" numbers (implementation.md 8.3), year to date at <c>now</c>, from <c>apps</c> and <c>sessions</c>.
/// Same rules as <see cref="YearQuery"/>: retired apps excluded, merged apps one group, an open session counts to
/// <c>now</c> if the Agent reports it running (or <c>running</c> is null), else to its heartbeat. Sessions are cut by
/// <see cref="UsageDays"/> into Local days, and each day into wall-clock hours, so days, months, hour-of-day and
/// "before noon" are exact on a DST day like Today (decisions 118, 146).
/// </summary>
public static class WrappedQuery
{
    private const int PodiumSize = 5;
    private const int OthersSize = 3;


    public static WrappedResult Run(string dbPath, DateTimeOffset now, TimeZoneInfo zone, IReadOnlyCollection<long>? running = null, long? onSinceUtc = null)
    {
        using var c = UsageDb.OpenReadOnly(dbPath);
        return Run(c, now, zone, running, onSinceUtc);
    }

    public static WrappedResult Run(SqliteConnection c, DateTimeOffset now, TimeZoneInfo zone, IReadOnlyCollection<long>? running = null, long? onSinceUtc = null)
    {
        var nowUtc = now.ToUnixTimeSeconds();
        var today = LocalDays.DateOf(nowUtc, zone);
        var year = today.Year;
        var yearStart = LocalDays.StartOf(new DateOnly(year, 1, 1), zone);
        var windowStart = LocalDays.StartOf(new DateOnly(year - 1, 12, 1), zone); // December feeds January's month delta

        var apps = AppGroups.Load(c);
        var first = apps.Count == 0 ? (DateOnly?)null : LocalDays.DateOf(apps.Values.Min(a => a.AddedUtc), zone);
        var groups = apps.Values.Select(a => a.Group).Distinct().ToList();

        var perDay = new Dictionary<int, long>();          // DateOnly.DayNumber -> seconds (this year)
        var perMonth = new Dictionary<int, long>();        // year * 12 + month - 1 -> seconds (window)
        var perHour = new long[24];                        // local hour of day -> seconds (this year)
        var perGroup = new Dictionary<long, long>();       // this year
        long beforeNoon = 0, total = 0;

        var segs = UsageLoader.LoadSegments(c, windowStart, nowUtc, apps, running);
        var windowed = segs.Select(s => s with { Start = Math.Max(s.Start, windowStart), End = Math.Min(s.End, nowUtc) });
        foreach (var sl in UsageDays.Slices(windowed, zone))
        {
            var len = sl.End - sl.Start;
            var monthIdx = sl.Day.Year * 12 + sl.Day.Month - 1;
            perMonth[monthIdx] = perMonth.GetValueOrDefault(monthIdx) + len;
            if (sl.Day.Year != year) continue;
            perDay[sl.Day.DayNumber] = perDay.GetValueOrDefault(sl.Day.DayNumber) + len;
            perGroup[sl.Seg.Group] = perGroup.GetValueOrDefault(sl.Seg.Group) + len;
            total += len;
            foreach (var p in UsageDays.HourPieces(sl.Start, sl.End, zone))
            {
                perHour[p.Hour] += p.End - p.Start;
                if (p.Hour < 12) beforeNoon += p.End - p.Start;
            }
        }

        // The headline is PC uptime (decision 125); everything else here stays app-seconds.
        var uptime = UsageUptime.PerDay(segs, zone, nowUtc, onSinceUtc).Where(kv => kv.Key.Year == year).Sum(kv => kv.Value);

        // Background apps (Steam, Wallpaper Engine) stay out of every ranking here; their time still counts in the totals.
        var ranked = UsageRank.Rank(perGroup.Where(kv => !UsageRank.IsBackground(apps, kv.Key))
            .Select(kv => new WrappedApp(kv.Key, UsageRank.NameOf(apps, kv.Key), kv.Value)), apps, a => a.AppId, a => a.Seconds);

        WrappedKindTop TopOf(Apps.AppKind kind)
        {
            var list = ranked.Where(a => UsageRank.KindOf(apps, a.AppId) == kind).ToList();
            return new WrappedKindTop(list.Take(PodiumSize).ToList(), list.Skip(PodiumSize).Take(OthersSize).ToList(), list.Sum(a => a.Seconds));
        }

        // One entry per app group: its single longest session that ended this year (a session counts whole, as in All).
        var longest = UsageRank.Rank(segs.Where(s => s.End > yearStart && !UsageRank.IsBackground(apps, s.Group))
            .GroupBy(s => s.Group).Select(g => new WrappedApp(g.Key, UsageRank.NameOf(apps, g.Key), g.Max(s => s.End - s.Start)))
            .Where(a => a.Seconds > 0), apps, a => a.AppId, a => a.Seconds)
            .Take(PodiumSize).ToList();

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
            bestLen, bestLen == 0 ? null : DateOnly.FromDayNumber(bestStartDay),
            bestLen == 0 ? null : DateOnly.FromDayNumber(bestStartDay + bestLen - 1),
            busiest, busiestSecs, delta, peak, peakSecs,
            total == 0 ? 0 : (int)Math.Round(beforeNoon * 100.0 / total, MidpointRounding.AwayFromZero),
            TopOf(Apps.AppKind.Game), TopOf(Apps.AppKind.Gacha), longest);
    }
}
