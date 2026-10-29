namespace TimeSink.Core.Usage;

/// <summary>
/// The segments of a window rolled up by calendar month (decision 146): app-seconds per app group and PC uptime per
/// month, both cut into Local days first so a session over midnight or a month end counts on each side. The two measures
/// stay apart because the views use them differently (decision 125): uptime for the month totals, app-seconds for the
/// per-app shares and for Wrapped's busiest month. Months with no time are empty, not missing.
/// </summary>
internal sealed class MonthRollup
{
    private readonly Dictionary<MonthKey, Dictionary<long, long>> _groups = [];
    private readonly Lazy<Dictionary<MonthKey, long>> _uptime;

    public MonthRollup(List<UsageSeg> segs, TimeZoneInfo zone, long nowUtc, long? onSinceUtc, bool gamesOnly)
    {
        foreach (var slice in UsageDays.Slices(segs, zone))
        {
            var key = MonthKey.Of(slice.Day);
            if (!_groups.TryGetValue(key, out var groups)) _groups[key] = groups = [];
            groups[slice.Seg.Group] = groups.GetValueOrDefault(slice.Seg.Group) + (slice.End - slice.Start);
        }
        _uptime = new Lazy<Dictionary<MonthKey, long>>(() =>
        {
            var byMonth = new Dictionary<MonthKey, long>();
            foreach (var (day, secs) in UsageUptime.PerDay(segs, zone, nowUtc, onSinceUtc, gamesOnly))
            {
                var key = MonthKey.Of(day);
                byMonth[key] = byMonth.GetValueOrDefault(key) + secs;
            }
            return byMonth;
        });
    }

    /// <summary>App-seconds per app group in the month.</summary>
    public IReadOnlyDictionary<long, long> Groups(MonthKey month) =>
        _groups.TryGetValue(month, out var groups) ? groups : new Dictionary<long, long>();

    /// <summary>App-seconds summed over every app in the month.</summary>
    public long AppSeconds(MonthKey month) => _groups.TryGetValue(month, out var groups) ? groups.Values.Sum() : 0;

    /// <summary>PC uptime in the month.</summary>
    public long Uptime(MonthKey month) => _uptime.Value.GetValueOrDefault(month);
}
