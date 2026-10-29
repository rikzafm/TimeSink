using Microsoft.Data.Sqlite;
using TimeSink.Core.Apps;

namespace TimeSink.Core.Usage;

/// <summary>
/// The Year "App x month" matrix (implementation.md 8.4, decisions 95-98), computed from <c>apps</c> and <c>sessions</c>
/// with the same rules as <see cref="YearQuery"/>: retired apps excluded, merged apps one row under the canonical app, an
/// open session counts to <c>now</c> if the Agent reports it running (or <c>running</c> is null), else to its heartbeat.
/// Columns run from the month tracking began (or January) to the current month.
/// </summary>
public static class MatrixQuery
{
    private const int TopApps = 3;
    private const int TrendWindow = 3;

    /// <summary>Colour level of <paramref name="seconds"/> against the app's best month: under 30%, 55%, 75%, 90%, else top.</summary>
    public static int Level(long seconds, long best)
    {
        if (best <= 0 || seconds <= 0) return 0;
        var pct = seconds * 100;
        return pct < 30 * best ? 0 : pct < 55 * best ? 1 : pct < 75 * best ? 2 : pct < 90 * best ? 3 : 4;
    }

    public static MatrixResult Run(UsageContext context)
    {
        using var c = UsageDb.OpenReadOnly(context.DbPath ?? throw new ArgumentException("The context has no database.", nameof(context)));
        return Run(new UsageWindow(new SqliteUsageSource(c), context));
    }

    internal static MatrixResult Run(UsageWindow window)
    {
        var context = window.Context;
        var (now, zone, gamesOnly, onSinceUtc) = (context.Now, context.Zone, context.GamesOnly, context.SignInUtc);
        var nowUtc = now.ToUnixTimeSeconds();
        var today = LocalDays.DateOf(nowUtc, zone);
        var year = today.Year;
        var apps = window.Apps;
        var started = TrackingStart.Of(apps, zone).FirstDay;
        if (started is null) return new MatrixResult(year, nowUtc, null, 1, [], [], 0);
        var first = started is { } s && s.Year == year ? s.Month : 1;
        var count = today.Month - first + 1;
        if (count < 1) return new MatrixResult(year, nowUtc, started, first, [], [], 0);

        var segs = window.Segments(LocalDays.StartOf(new DateOnly(year, first, 1), zone));
        var rollup = new MonthRollup(segs, zone, nowUtc, onSinceUtc, gamesOnly);
        var months = Enumerable.Range(0, count).Select(i => new MonthKey(year, first + i)).ToList();
        var perApp = new Dictionary<long, long[]>();
        for (var i = 0; i < count; i++)
            foreach (var (group, secs) in rollup.Groups(months[i]))
            {
                if (!perApp.TryGetValue(group, out var arr)) perApp[group] = arr = new long[count];
                arr[i] += secs;
            }

        // Month totals and the headline are PC uptime (decision 125); the per-app cells stay per-app.
        var uptimeByMonth = months.Select(rollup.Uptime).ToArray();
        var grand = uptimeByMonth.Sum();
        var rows = UsageRank.Rank(perApp.Where(kv => kv.Value.Sum() > 0)
            .Select(kv => BuildRow(kv.Key, UsageRank.NameOf(apps, kv.Key), kv.Value, first, grand)),
            apps, r => r.AppId, r => r.TotalSeconds);

        var columns = new List<MatrixMonth>(count);
        for (var i = 0; i < count; i++)
        {
            var idx = i;
            var top = UsageRank.Rank(rows.Where(r => r.Cells[idx].Seconds > 0), apps, r => r.AppId, r => r.Cells[idx].Seconds)
                .Take(TopApps).Select(r => new YearAppShare(r.AppId, r.Name, r.Cells[idx].Seconds)).ToList();
            columns.Add(new MatrixMonth(first + i, uptimeByMonth[idx], top));
        }
        return new MatrixResult(year, nowUtc, started, first, columns, rows, grand);
    }

    private static MatrixRow BuildRow(long id, string name, long[] secs, int firstMonth, long grand)
    {
        var best = secs.Max();
        var peak = Array.IndexOf(secs, best);
        var cells = secs.Select(v => new MatrixCell(v, Level(v, best))).ToList();
        var total = secs.Sum();

        Change? trend = null;
        var lastFrom = Math.Max(0, secs.Length - TrendWindow);
        var prevFrom = Math.Max(0, lastFrom - TrendWindow);
        if (lastFrom > 0)
        {
            var last = secs.Skip(lastFrom).Sum();
            var prev = secs.Skip(prevFrom).Take(lastFrom - prevFrom).Sum();
            trend = Change.Between(last, prev);
        }
        var share = grand <= 0 ? 0 : (int)Math.Round(total * 100.0 / grand, MidpointRounding.AwayFromZero);
        return new MatrixRow(id, name, cells, total, share, firstMonth + peak, best, trend);
    }
}
