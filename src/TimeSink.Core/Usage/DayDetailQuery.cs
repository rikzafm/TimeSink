using TimeSink.Core.Apps;

namespace TimeSink.Core.Usage;

/// <summary>What a Week column shows when clicked. <see cref="TotalSeconds"/> is the day's PC uptime, the number on the column itself.</summary>
public sealed record DayDetail(
    DateOnly Date,
    long TotalSeconds,
    long? FirstStartUtc,
    long? LastEndUtc,
    IReadOnlyList<BarApp> Apps,
    double WeekAverageSeconds,
    long SameDayLastWeekSeconds);

/// <summary>
/// The Week day panel (decision 148): one database open and one live context for the clicked day, its week and the
/// same day a week earlier. The total is the Week column's own number, so the panel and the column cannot disagree on
/// the live day; the average is the clicked day's week (not always the current one), by the rule of
/// <see cref="WeekResult.AverageSeconds"/>.
/// </summary>
public static class DayDetailQuery
{
    public static DayDetail Run(string dbPath, DateOnly day, DateTimeOffset now, TimeZoneInfo zone, WeekStart weekStart,
        IReadOnlyCollection<long>? running = null, long? onSinceUtc = null, bool gamesOnly = false)
    {
        using var c = UsageDb.OpenReadOnly(dbPath);
        var today = LocalDays.DateOf(now.ToUnixTimeSeconds(), zone);
        var dow = weekStart.ToDayOfWeek();
        var weeksBack = Math.Max(0, (WeekStarts.Containing(today, dow).DayNumber - WeekStarts.Containing(day, dow).DayNumber) / 7);

        var week = WeekQuery.Run(c, now, zone, weekStart, running, onSinceUtc, weeksBack, gamesOnly);
        var thisDay = TodayQuery.Run(c, now, zone, running, day, onSinceUtc, gamesOnly: gamesOnly);
        var lastWeek = TodayQuery.Run(c, now, zone, running, day.AddDays(-7), gamesOnly: gamesOnly);

        var apps = thisDay.Apps.Select(a => new BarApp(a.AppId, a.Name, a.Seconds)).ToList();
        var sessions = thisDay.Apps.SelectMany(a => a.Sessions).ToList();
        long? first = sessions.Count == 0 ? null : sessions.Min(s => s.StartUtc);
        long? last = sessions.Count == 0 ? null : sessions.Max(s => s.EndUtc);
        var column = week.Days.FirstOrDefault(d => d.Date == day);
        return new DayDetail(thisDay.Date, column?.Seconds ?? thisDay.UptimeSeconds, first, last, apps, week.AverageSeconds, lastWeek.UptimeSeconds);
    }
}
