using TimeSink.Core.Usage;

namespace TimeSink.Tests.Usage;

/// <summary>
/// Runs the Today, Week and Day detail queries against a database file with the loose arguments the fixtures use,
/// building the <see cref="UsageContext"/> the real loader would hand each query.
/// </summary>
internal static class Q
{
    public static UsageContext Ctx(string db, DateTimeOffset now, TimeZoneInfo zone, IReadOnlyCollection<long>? running = null,
        long? onSinceUtc = null, WeekStart weekStart = WeekStart.Mon, bool gamesOnly = false) =>
        new(now, zone, running, () => onSinceUtc, () => weekStart, gamesOnly, db);

    public static TodayResult Today(string db, DateTimeOffset now, TimeZoneInfo zone, IReadOnlyCollection<long>? running = null,
        DateOnly? forDay = null, long? onSinceUtc = null, bool gamesOnly = false) =>
        TodayQuery.Run(Ctx(db, now, zone, running, onSinceUtc, gamesOnly: gamesOnly), forDay);

    public static WeekResult Week(string db, DateTimeOffset now, TimeZoneInfo zone, WeekStart weekStart,
        IReadOnlyCollection<long>? running = null, long? onSinceUtc = null, int weeksBack = 0, bool gamesOnly = false) =>
        WeekQuery.Run(Ctx(db, now, zone, running, onSinceUtc, weekStart, gamesOnly), weeksBack);

    public static DayDetail DayDetail(string db, DateOnly day, DateTimeOffset now, TimeZoneInfo zone, WeekStart weekStart,
        IReadOnlyCollection<long>? running = null, long? onSinceUtc = null, bool gamesOnly = false) =>
        DayDetailQuery.Run(Ctx(db, now, zone, running, onSinceUtc, weekStart, gamesOnly), day);
}
