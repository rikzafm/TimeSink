using Microsoft.Data.Sqlite;

namespace TimeSink.Core.Usage;

/// <summary>A tracked app as the usage queries see it. <c>Group</c> is the canonical app id (merged apps share it).</summary>
internal sealed record UsageApp(long Id, long Group, string Name, long AddedUtc);

/// <summary>One session clipped to what the queries need: its group, its real app, bounds, whether it is still open and whether it ended by sleep.</summary>
internal readonly record struct UsageSeg(long Group, long AppId, long Start, long End, bool Open, bool Sleep);

/// <summary>
/// The apps and sessions loaders shared by the Today, Month, Year and Wrapped queries (decision 56): retired and unknown
/// apps are excluded, merged apps share their canonical group, and an open session counts to <c>now</c> when the Agent
/// reports its app running (or when <c>running</c> is null) and otherwise only to its last heartbeat.
/// </summary>
internal static class UsageLoader
{
    public static Dictionary<long, UsageApp> LoadApps(SqliteConnection c)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, COALESCE(canonical_id, id), display_name, added_utc FROM apps WHERE state = 'tracked'";
        using var r = cmd.ExecuteReader();
        var d = new Dictionary<long, UsageApp>();
        while (r.Read()) d[r.GetInt64(0)] = new UsageApp(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetInt64(3));
        // A tracked alias whose canonical row is retired/absent still names its own group.
        foreach (var a in d.Values.ToList())
            if (!d.ContainsKey(a.Group) && a.Group != a.Id) d[a.Id] = a with { Group = a.Id };
        return d;
    }

    /// <param name="to">Optional exclusive upper bound on a session's start (Month reads a bounded window).</param>
    public static List<UsageSeg> LoadSegments(SqliteConnection c, long from, long nowUtc, Dictionary<long, UsageApp> apps,
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
            var sleep = !r.IsDBNull(4) && r.GetString(4) == "sleep";
            list.Add(new UsageSeg(app.Group, appId, start, end, open, sleep && !open));
        }
        return list;
    }
}
