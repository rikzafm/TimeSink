using Microsoft.Data.Sqlite;
using TimeSink.Core.Apps;

using Seg = TimeSink.Core.Usage.UsageSeg;

namespace TimeSink.Core.Usage;

/// <summary>
/// The Year "Pace" numbers (implementation.md 8.4, decisions 66-71), computed from <c>apps</c> and <c>sessions</c> as a
/// snapshot at <c>now</c>. Same rules as <see cref="TodayQuery"/>: retired apps excluded, merged apps one group, an open
/// session counts to <c>now</c> if the Agent reports it running (or <c>running</c> is null), else to its heartbeat.
/// </summary>
public static class YearQuery
{
    private const int RecentMonths = 3;


    public static YearResult Run(string dbPath, DateTimeOffset now, TimeZoneInfo zone, IReadOnlyCollection<long>? running = null, long? onSinceUtc = null, bool gamesOnly = false)
    {
        using var c = UsageDb.OpenReadOnly(dbPath);
        return Run(c, now, zone, running, onSinceUtc, gamesOnly: gamesOnly);
    }

    public static YearResult Run(SqliteConnection c, DateTimeOffset now, TimeZoneInfo zone, IReadOnlyCollection<long>? running = null, long? onSinceUtc = null, bool gamesOnly = false)
    {
        var nowUtc = now.ToUnixTimeSeconds();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var year = today.Year;
        var curIdx = Idx(today.Year, today.Month);
        var yearIdx = Idx(year, 1);
        var firstIdx = yearIdx - RecentMonths + 1; // two earlier months feed the "last 3 months" and January's delta

        var apps = AppGroups.Load(c, gamesOnly: gamesOnly);
        var start = TrackingStart.Of(apps, zone);
        var first = start.FirstDay;
        var startIdx = first is null ? int.MaxValue : Idx(first.Value.Year, first.Value.Month);
        var partialFirst = first is { } f && start.IsPartialMonth(f.Year, f.Month) ? startIdx : -1; // started mid-month: not a full month

        // Seconds per (group, month index) over the window [two months before the year .. now].
        var winFrom = LocalDays.StartOf(new DateOnly(firstIdx / 12, firstIdx % 12 + 1, 1), zone);
        var segs = UsageLoader.LoadSegments(c, winFrom, nowUtc, apps, running);
        var perMonth = new Dictionary<int, Dictionary<long, long>>();
        foreach (var sl in UsageDays.Slices(segs, zone))
        {
            var idx = Idx(sl.Day.Year, sl.Day.Month);
            if (idx < firstIdx || idx > curIdx) continue;
            if (!perMonth.TryGetValue(idx, out var g)) perMonth[idx] = g = new Dictionary<long, long>();
            g[sl.Seg.Group] = g.GetValueOrDefault(sl.Seg.Group) + (sl.End - sl.Start);
        }

        // Month totals are PC uptime; the per-app shares above only feed each month's top apps.
        var uptimeByMonth = new Dictionary<int, long>();
        foreach (var (day, secs) in UsageUptime.PerDay(segs, zone, nowUtc, onSinceUtc, gamesOnly))
        {
            var idx = Idx(day.Year, day.Month);
            uptimeByMonth[idx] = uptimeByMonth.GetValueOrDefault(idx) + secs;
        }
        long Secs(int idx) => uptimeByMonth.GetValueOrDefault(idx);

        // Current month projection: seconds / days elapsed since tracking (or month) began * days in month.
        long curSecs = Secs(curIdx);
        long curProj = curSecs;
        var hasPace = first is not null;
        if (hasPace)
        {
            var monthFirst = new DateOnly(today.Year, today.Month, 1);
            var from = start.Since(monthFirst);
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
                ? UsageRank.Rank(shares, apps, kv => kv.Key, kv => kv.Value)
                    .Select(kv => new YearAppShare(kv.Key, UsageRank.NameOf(apps, kv.Key), kv.Value)).ToList()
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
}
