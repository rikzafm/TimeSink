using Microsoft.Data.Sqlite;

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

        string? name = null, exe = null;
        var background = false;
        var members = new HashSet<long>();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT id, canonical_id, display_name, state, exe_path, is_background FROM apps";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var id = r.GetInt64(0);
                if (id == appId)
                {
                    if (!r.IsDBNull(1) || r.GetString(3) != "tracked") return null; // aliases are shown under their canonical app
                    name = r.GetString(2);
                    exe = Path.GetFileName(r.GetString(4));
                    background = r.GetInt64(5) != 0;
                    members.Add(id);
                }
                else if (!r.IsDBNull(1) && r.GetInt64(1) == appId) members.Add(id);
            }
        }
        if (name is null) return null;

        var segs = new List<UsageSeg>();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT app_id, start_utc, end_utc, last_seen_utc FROM sessions WHERE start_utc <= $now ORDER BY start_utc";
            cmd.Parameters.AddWithValue("$now", nowUtc);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var id = r.GetInt64(0);
                if (!members.Contains(id)) continue;
                var start = r.GetInt64(1);
                long end;
                var live = false;
                if (r.IsDBNull(2))
                {
                    live = running is null || running.Contains(id);
                    end = live ? Math.Max(nowUtc, start) : Math.Clamp(r.GetInt64(3), start, nowUtc);
                }
                else end = r.GetInt64(2);
                if (end <= start) continue;
                segs.Add(new UsageSeg(appId, id, start, end, live, false));
            }
        }

        var daySeconds = new Dictionary<DateOnly, long>();
        var daySessions = new Dictionary<DateOnly, int>();
        long total = 0, longest = 0;
        foreach (var seg in segs) longest = Math.Max(longest, seg.End - seg.Start);
        foreach (var slice in UsageWindow.Slices(segs, zone))
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
