using Microsoft.Data.Sqlite;
using TimeSink.Core.Apps;

namespace TimeSink.Core.Usage;

/// <summary>
/// The app detail page's numbers, computed from <c>apps</c> and <c>sessions</c> like <see cref="AllQuery"/> (decision 56):
/// merged apps are one app under the canonical id, sessions are clipped to local days (DST-aware), and an open session
/// counts to <c>now</c> when the Agent reports it running (or when <c>running</c> is null) and otherwise only to its
/// heartbeat. A session is one "opening" however many days it spans. Returns null for an unknown or retired app.
/// </summary>
public static class AppDetailQuery
{
    /// <summary>Heat step 0-4 of a day against the app's busiest day: none, then quarters of the peak.</summary>
    public static int Level(long seconds, long peak)
    {
        if (seconds <= 0 || peak <= 0) return 0;
        var pct = seconds * 100;
        return pct <= 25 * peak ? 1 : pct <= 50 * peak ? 2 : pct <= 75 * peak ? 3 : 4;
    }

    public static AppDetailResult? Run(string dbPath, DateTimeOffset now, TimeZoneInfo zone, long appId, int year, IReadOnlyCollection<long>? running = null)
    {
        using var c = UsageDb.OpenReadOnly(dbPath);
        return Run(c, now, zone, appId, year, running);
    }

    public static AppDetailResult? Run(SqliteConnection c, DateTimeOffset now, TimeZoneInfo zone, long appId, int year, IReadOnlyCollection<long>? running = null)
    {
        var nowUtc = now.ToUnixTimeSeconds();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);

        // Retired apps are loaded so a retired alias of this app still counts, as it does on the All tab.
        var apps = AppGroups.Load(c, includeRetired: true);
        if (!apps.TryGetValue(appId, out var app) || app.Retired || app.Group != appId) return null; // aliases are shown under their canonical app
        var name = app.Name;
        var exe = Path.GetFileName(app.ExePath);
        var background = app.Background;

        var segs = UsageLoader.LoadSegments(c, 0, nowUtc, apps, running).Where(s => s.Group == appId && s.End > s.Start).ToList();

        var daySeconds = new Dictionary<DateOnly, long>();
        var daySessions = new Dictionary<DateOnly, int>();
        long total = 0, longest = 0;
        foreach (var seg in segs) longest = Math.Max(longest, seg.End - seg.Start);
        foreach (var slice in UsageDays.Slices(segs, zone))
        {
            var secs = slice.End - slice.Start;
            daySeconds[slice.Day] = daySeconds.GetValueOrDefault(slice.Day) + secs;
            daySessions[slice.Day] = daySessions.GetValueOrDefault(slice.Day) + 1;
            total += secs;
        }

        DateOnly? first = daySeconds.Count > 0 ? daySeconds.Keys.Min() : null;
        var firstYear = first?.Year ?? today.Year;
        var shown = Math.Clamp(year, Math.Min(firstYear, today.Year), today.Year);

        AppDetailLast? lastOpened = null;
        if (segs.Count > 0)
        {
            var s = segs.MaxBy(x => x.Start);
            lastOpened = new AppDetailLast(s.Start, s.End - s.Start, s.Open);
        }

        long yearSeconds = 0, peak = 0;
        for (var d = new DateOnly(shown, 1, 1); d.Year == shown; d = d.AddDays(1))
        {
            var secs = daySeconds.GetValueOrDefault(d);
            yearSeconds += secs;
            peak = Math.Max(peak, secs);
        }
        var days = new List<AppDetailDay>(366);
        for (var d = new DateOnly(shown, 1, 1); d.Year == shown; d = d.AddDays(1))
        {
            var kind = d > today ? AppDetailDayKind.Future
                : first is null || d < first ? AppDetailDayKind.BeforeTracking
                : AppDetailDayKind.Tracked;
            var secs = daySeconds.GetValueOrDefault(d);
            days.Add(new AppDetailDay(d, secs, daySessions.GetValueOrDefault(d), kind == AppDetailDayKind.Tracked ? Level(secs, peak) : 0, kind));
        }

        return new AppDetailResult(appId, name, exe ?? "", first, lastOpened, total, segs.Count,
            segs.Count > 0 ? total / segs.Count : 0, longest, shown, firstYear, today.Year, yearSeconds, peak, days, background,
            daySeconds,
            segs.Select(s => new AppDetailOpen(LocalDays.DateOf(s.Start, zone), TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(s.Start), zone).Hour)).ToList());
    }
}
