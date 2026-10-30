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


    public static YearResult Run(UsageContext context)
    {
        return UsageWindow.Over(context, Run);
    }

    internal static YearResult Run(UsageWindow window)
    {
        var context = window.Context;
        var (now, zone, gamesOnly, onSinceUtc) = (context.Now, context.Zone, context.GamesOnly, context.SignInUtc);
        var nowUtc = now.ToUnixTimeSeconds();
        var today = window.Today;
        var year = today.Year;
        var cur = MonthKey.Of(today);
        var firstMonth = new MonthKey(year, 1).AddMonths(-(RecentMonths - 1)); // two earlier months feed the "last 3 months" and January's delta

        var apps = window.Apps;
        var start = TrackingStart.Of(apps, zone);
        var first = start.FirstDay;
        MonthKey? startMonth = first is { } fd ? MonthKey.Of(fd) : null;
        bool BeforeTracking(MonthKey m) => startMonth is not { } s || m < s;
        MonthKey? partialFirst = first is { } f && start.IsPartialMonth(f.Year, f.Month) ? startMonth : null; // started mid-month: not a full month

        // The window runs from two months before the year to now. Month totals are PC uptime; the per-app shares only feed
        // each month's top apps.
        var segs = window.Segments(LocalDays.StartOf(firstMonth.First, zone));
        var rollup = new MonthRollup(segs, zone, nowUtc, onSinceUtc, gamesOnly);
        long Secs(MonthKey m) => rollup.Uptime(m);

        // Current month projection: seconds / days elapsed since tracking (or month) began * days in month.
        long curSecs = Secs(cur);
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
        for (var i = cur.AddMonths(-(RecentMonths - 1)); i <= cur; i = i.AddMonths(1))
        {
            if (BeforeTracking(i)) continue;
            if (i == cur) recent.Add(curProj);
            else if (i != partialFirst) recent.Add(Secs(i));
        }
        var ghost = recent.Count == 0 ? 0 : (long)Math.Round(recent.Sum() / (double)recent.Count);

        var months = new List<YearMonth>(12);
        for (var m = 1; m <= 12; m++)
        {
            var month = new MonthKey(year, m);
            var kind = BeforeTracking(month) ? YearMonthKind.BeforeTracking
                : month > cur ? YearMonthKind.Future
                : month == cur ? YearMonthKind.Current : YearMonthKind.Tracked;
            var secs = kind is YearMonthKind.Tracked or YearMonthKind.Current ? Secs(month) : 0;
            var proj = kind switch { YearMonthKind.Current => curProj, YearMonthKind.Future => ghost, _ => secs };

            long? prev = null;
            Change? delta = null;
            if (kind is YearMonthKind.Tracked or YearMonthKind.Current && !BeforeTracking(month.Prev))
            {
                prev = Secs(month.Prev);
                delta = Change.Between(proj, prev.Value);
            }

            var top = kind is YearMonthKind.Tracked or YearMonthKind.Current && rollup.Groups(month) is { Count: > 0 } shares
                ? UsageRank.Rank(shares, apps, kv => kv.Key, kv => kv.Value)
                    .Select(kv => new YearAppShare(kv.Key, UsageRank.NameOf(apps, kv.Key), kv.Value)).ToList()
                : new List<YearAppShare>();
            months.Add(new YearMonth(m, kind, secs, proj, delta, prev, top));
        }

        var total = months.Sum(m => m.Seconds);
        var fullMonths = months.Where(m => m.Kind == YearMonthKind.Tracked && new MonthKey(year, m.Month) != partialFirst).ToList();
        long? average = fullMonths.Count == 0 ? null : (long)Math.Round(fullMonths.Average(m => (double)m.Seconds));

        var best = months.Where(m => m.Kind is YearMonthKind.Tracked or YearMonthKind.Current && m.Seconds > 0)
            .OrderByDescending(m => m.Seconds).FirstOrDefault();

        long? pace = hasPace
            ? total + (curProj - curSecs) + months.Where(m => m.Kind == YearMonthKind.Future).Sum(m => m.ProjectedSeconds)
            : null;

        return new YearResult(year, nowUtc, first, months, total, average, best?.Month, best?.Seconds ?? 0, pace,
            hasPace ? recent.Count : 0);
    }
}
