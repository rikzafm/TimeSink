using Microsoft.Data.Sqlite;

namespace TimeSink.Core.Usage;

/// <summary>
/// The Week view's numbers (implementation.md 8.3, decisions 2, 3, 66-70): computed from <c>apps</c> and <c>sessions</c>
/// as a snapshot at <c>now</c>, with local-day boundaries from <paramref name="zone"/> (DST-aware: a day is 23, 24 or 25 h).
/// Retired apps are excluded and merged apps are one row under the canonical app. An open session counts up to
/// <c>now</c> if the Agent reports it running (or <c>running</c> is null), otherwise up to its last heartbeat.
/// </summary>
public static class WeekQuery
{
    public const int GridWeeks = 20;
    public const int SparkDays = 10;
    /// <summary>The streak is looked for this far back (and so tops out at this many days).</summary>
    public const int StreakLookbackDays = 365;

    public static WeekResult Run(string dbPath, DateTimeOffset now, TimeZoneInfo zone, WeekStart weekStart, IReadOnlyCollection<long>? running = null, long? onSinceUtc = null, int weeksBack = 0)
    {
        using var c = UsageDb.OpenReadOnly(dbPath);
        return Run(c, now, zone, weekStart, running, onSinceUtc, weeksBack);
    }

    public static WeekResult Run(SqliteConnection c, DateTimeOffset now, TimeZoneInfo zone, WeekStart weekStart, IReadOnlyCollection<long>? running = null, long? onSinceUtc = null, int weeksBack = 0)
    {
        var nowUtc = now.ToUnixTimeSeconds();
        var today = LocalDays.DateOf(nowUtc, zone);
        var offset = ((int)today.DayOfWeek - (int)weekStart.ToDayOfWeek() + 7) % 7;
        var thisWeek = today.AddDays(-offset);
        var gridStart = thisWeek.AddDays(-7 * (GridWeeks - 1));
        var shownWeek = thisWeek.AddDays(-7 * weeksBack); // the seven columns and app rows; the grid and streak stay anchored to today
        var lookStart = new[] { gridStart, today.AddDays(-StreakLookbackDays), shownWeek.AddDays(-7) }.Min();

        var apps = UsageLoader.LoadApps(c);
        var names = apps.Values.Where(a => a.Id == a.Group).ToDictionary(a => a.Id, a => a.Name);
        var segs = UsageLoader.LoadSegments(c, LocalDays.StartOf(lookStart, zone), nowUtc, apps, running);
        var uptime = UsageUptime.PerDay(segs, zone, nowUtc, onSinceUtc);

        // App seconds per local day (all apps, for the streak) and per (app group, day); the columns show PC uptime.
        var perDay = new Dictionary<DateOnly, long>();
        var perGroupDay = new Dictionary<(long, DateOnly), long>();
        foreach (var slice in UsageDays.Slices(segs, zone))
        {
            var overlap = slice.End - slice.Start;
            perDay[slice.Day] = perDay.GetValueOrDefault(slice.Day) + overlap;
            perGroupDay[(slice.Seg.Group, slice.Day)] = perGroupDay.GetValueOrDefault((slice.Seg.Group, slice.Day)) + overlap;
        }

        var days = Enumerable.Range(0, 7).Select(i =>
        {
            var d = shownWeek.AddDays(i);
            return new WeekDay(d, uptime.GetValueOrDefault(d), d == today, d > today);
        }).ToList();

        var heat = new List<HeatCell>(GridWeeks * 7);
        for (var i = 0; i < GridWeeks * 7; i++)
        {
            var d = gridStart.AddDays(i);
            var future = d > today;
            var secs = future ? 0 : uptime.GetValueOrDefault(d);
            heat.Add(new HeatCell(d, secs, future ? 0 : HeatLevel(secs), future));
        }

        // Weekly totals for the trend strip (anchored to today like the grid) and last week up to the same weekday.
        var weekTotals = Enumerable.Range(0, GridWeeks)
            .Select(w => heat.Skip(w * 7).Take(7).Sum(h => h.Seconds)).ToList();
        var prevWeek = days.Select((d, i) => (d, i)).Where(x => !x.d.IsFuture)
            .Sum(x => uptime.GetValueOrDefault(shownWeek.AddDays(x.i - 7)));

        // Streak: consecutive days with any time, counted back from today; an empty today does not break it.
        var streak = 0;
        var cursor = perDay.GetValueOrDefault(today) > 0 ? today : today.AddDays(-1);
        while (streak < StreakLookbackDays && perDay.GetValueOrDefault(cursor) > 0)
        {
            streak++;
            cursor = cursor.AddDays(-1);
        }

        var rows = new List<WeekAppRow>();
        foreach (var g in perGroupDay.Keys.Select(k => k.Item1).Distinct())
        {
            var week = days.Sum(d => perGroupDay.GetValueOrDefault((g, d.Date)));
            if (week == 0) continue;
            var spark = Enumerable.Range(0, SparkDays)
                .Select(i => perGroupDay.GetValueOrDefault((g, today.AddDays(i - (SparkDays - 1))))).ToList();
            rows.Add(new WeekAppRow(g, names.GetValueOrDefault(g, g.ToString()), week, spark));
        }
        rows = rows.OrderBy(r => UsageRank.Tier(apps, r.AppId)).ThenByDescending(r => r.Seconds).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

        return new WeekResult(today, shownWeek, nowUtc, days.Sum(d => d.Seconds), days, heat, streak, rows, weekTotals, prevWeek);
    }

    /// <summary>Heat step for a day's seconds: under 1 h, 3 h, 5 h, 7 h, then 7 h or more (the Month view uses the same steps).</summary>
    public static int HeatLevel(long seconds) => seconds switch
    {
        < 3600 => 0,
        < 3 * 3600 => 1,
        < 5 * 3600 => 2,
        < 7 * 3600 => 3,
        _ => 4,
    };
}
