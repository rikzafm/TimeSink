using Microsoft.Data.Sqlite;
using TimeSink.Core.Apps;
using TimeSink.Core.Tracking;

namespace TimeSink.Core.Usage;

/// <summary>
/// The Ranking rule every usage query lists apps by: Background apps (Steam, Wallpaper Engine) last, then most time first,
/// then name A to Z ignoring case. The Background flag lives on the canonical app of a merged group.
/// </summary>
internal static class UsageRank
{
    /// <summary>The name comparison Ranking breaks ties with, for the one list (Today's live day) that ranks by its own keys first.</summary>
    public static StringComparer ByName => StringComparer.CurrentCultureIgnoreCase;

    public static AppKind KindOf(AppGroups apps, long group) => apps.TryGetValue(group, out var a) ? a.Kind : AppKind.Other;

    public static bool IsBackground(AppGroups apps, long group) => apps.TryGetValue(group, out var a) && a.Background;

    public static string NameOf(AppGroups apps, long group) => apps.TryGetValue(group, out var a) ? a.Name : group.ToString();

    /// <summary>Ranks <paramref name="items"/> by their app group and amount. Ties keep the input order once the name is equal.</summary>
    public static List<T> Rank<T>(IEnumerable<T> items, AppGroups apps, Func<T, long> groupOf, Func<T, long> amountOf) =>
        items.OrderBy(i => IsBackground(apps, groupOf(i)))
            .ThenByDescending(amountOf)
            .ThenBy(i => NameOf(apps, groupOf(i)), ByName)
            .ToList();
}

/// <summary>One session clipped to what the queries need: its group, its real app, bounds, whether it is still open and whether it ended by sleep.</summary>
internal readonly record struct UsageSeg(long Group, long AppId, long Start, long End, bool Open, bool Sleep);

/// <summary>
/// The apps and sessions loaders shared by every usage query (decision 56): unknown apps are excluded, and retired apps
/// too unless the caller asks for them (the All tab), merged apps share their canonical group, and an open session counts
/// to <c>now</c> when the Agent reports its app running (or when <c>running</c> is null) and otherwise only to its last heartbeat.
/// </summary>
internal static class UsageLoader
{
    /// <param name="to">Optional exclusive upper bound on a session's start (Month reads a bounded window).</param>
    public static List<UsageSeg> LoadSegments(SqliteConnection c, long from, long nowUtc, AppGroups apps,
        IReadOnlyCollection<long>? running, long? to = null)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"""
            SELECT app_id, start_utc, end_utc, last_seen_utc, end_reason FROM sessions
            WHERE start_utc <= $now{(to is null ? "" : " AND start_utc < $to")} AND (end_utc IS NULL OR end_utc >= $from) ORDER BY start_utc
            """;
        cmd.Parameters.AddWithValue("$now", nowUtc);
        if (to is { } t) cmd.Parameters.AddWithValue("$to", t);
        cmd.Parameters.AddWithValue("$from", from);
        using var r = cmd.ExecuteReader();
        var list = new List<UsageSeg>();
        while (r.Read())
        {
            var appId = r.GetInt64(0);
            if (!apps.TryGetValue(appId, out var app)) continue; // retired or unknown
            var start = r.GetInt64(1);
            long end;
            var open = r.IsDBNull(2);
            if (open)
            {
                if (running is null || running.Contains(appId)) end = Math.Max(nowUtc, start);
                else { end = Math.Clamp(r.GetInt64(3), start, nowUtc); open = false; }
            }
            else end = r.GetInt64(2);
            // A shutdown close is the PC being off, like a sleep close: the gap to the next session is not uptime.
            var sleep = !r.IsDBNull(4) && r.GetString(4) is EndReasons.Sleep or EndReasons.Shutdown;
            list.Add(new UsageSeg(app.Group, appId, start, end, open, sleep && !open));
        }
        return list;
    }
}
