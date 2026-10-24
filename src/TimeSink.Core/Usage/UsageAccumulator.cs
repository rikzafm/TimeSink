using System.Globalization;
using TimeSink.Core.Tracking;

namespace TimeSink.Core.Usage;

/// <summary>
/// Maintains the derived <c>daily_usage</c> table. Ranges are split at local midnight of the given zone
/// (DST-aware: days can be 23 or 25 hours long). A reversed range subtracts.
/// </summary>
public sealed class UsageAccumulator
{
    private readonly ISessionStore _store;
    private readonly TimeZoneInfo _zone;

    public UsageAccumulator(ISessionStore store, TimeZoneInfo zone)
    {
        _store = store;
        _zone = zone;
    }

    public void Add(long appId, DateTimeOffset from, DateTimeOffset to) =>
        Add(appId, from.ToUnixTimeSeconds(), to.ToUnixTimeSeconds());

    public void Add(long appId, long fromUtc, long toUtc)
    {
        foreach (var (day, seconds) in Split(fromUtc, toUtc))
            _store.AddDailyUsage(appId, day, seconds);
    }

    /// <summary>Recomputes daily_usage from sessions (open sessions count up to last_seen).</summary>
    public void RebuildAll()
    {
        var totals = new Dictionary<(long, string), long>();
        foreach (var s in _store.GetAllSessions())
        {
            var end = s.EndUtc ?? s.LastSeenUtc;
            foreach (var (day, seconds) in Split(s.StartUtc, end))
                totals[(s.AppId, day)] = totals.GetValueOrDefault((s.AppId, day)) + seconds;
        }
        _store.ReplaceDailyUsage(totals.Where(t => t.Value > 0)
            .Select(t => new DailyUsageRow(t.Key.Item1, t.Key.Item2, t.Value)).ToList());
    }

    private IEnumerable<(string Day, long Seconds)> Split(long from, long to)
    {
        if (from == to) yield break;
        long sign = 1;
        if (to < from) { (from, to) = (to, from); sign = -1; }

        var cur = from;
        while (cur < to)
        {
            var local = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(cur), _zone);
            var next = Math.Min(to, NextLocalMidnightUtc(local.Date));
            if (next <= cur) next = to; // defensive: never loop forever
            yield return (local.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), sign * (next - cur));
            cur = next;
        }
    }

    private long NextLocalMidnightUtc(DateTime localDate)
    {
        var next = localDate.AddDays(1);
        while (_zone.IsInvalidTime(next)) next = next.AddHours(1);
        var offset = _zone.IsAmbiguousTime(next)
            ? _zone.GetAmbiguousTimeOffsets(next).Max() // earlier instant
            : _zone.GetUtcOffset(next);
        return new DateTimeOffset(next, offset).ToUnixTimeSeconds();
    }
}
