using Microsoft.Data.Sqlite;
using TimeSink.Core.Apps;

namespace TimeSink.Core.Usage;

/// <summary>One <c>sessions</c> row as stored: <see cref="EndUtc"/> is null while the session is open.</summary>
internal readonly record struct RawSession(long AppId, long StartUtc, long? EndUtc, long LastSeenUtc, string? EndReason);

/// <summary>
/// Where a <see cref="UsageWindow"/> reads from: the apps and the stored sessions. <see cref="SqliteUsageSource"/> is the
/// real adapter; tests also use an in-memory one.
/// </summary>
internal interface IUsageSource
{
    /// <summary>The tracked apps by group; with <paramref name="gamesOnly"/> only the Game and Gacha groups.</summary>
    AppGroups Apps(bool gamesOnly);

    /// <summary>
    /// The sessions that started at or before <paramref name="nowUtc"/> (and before <paramref name="to"/> when given) and
    /// that are open or ended at or after <paramref name="from"/>, in start order.
    /// </summary>
    IEnumerable<RawSession> Sessions(long from, long nowUtc, long? to);
}

/// <summary>The SQLite adapter of <see cref="IUsageSource"/>. The caller owns the connection.</summary>
internal sealed class SqliteUsageSource(SqliteConnection connection) : IUsageSource
{
    public AppGroups Apps(bool gamesOnly) => AppGroups.Load(connection, gamesOnly: gamesOnly);

    public IEnumerable<RawSession> Sessions(long from, long nowUtc, long? to)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT app_id, start_utc, end_utc, last_seen_utc, end_reason FROM sessions
            WHERE start_utc <= $now{(to is null ? "" : " AND start_utc < $to")} AND (end_utc IS NULL OR end_utc >= $from) ORDER BY start_utc
            """;
        cmd.Parameters.AddWithValue("$now", nowUtc);
        if (to is { } t) cmd.Parameters.AddWithValue("$to", t);
        cmd.Parameters.AddWithValue("$from", from);
        using var r = cmd.ExecuteReader();
        var list = new List<RawSession>();
        while (r.Read())
        {
            var start = r.GetInt64(1);
            list.Add(new RawSession(r.GetInt64(0), start, r.IsDBNull(2) ? null : r.GetInt64(2),
                r.IsDBNull(3) ? start : r.GetInt64(3), r.IsDBNull(4) ? null : r.GetString(4)));
        }
        return list;
    }
}

/// <summary>
/// The apps and the Session segments one view's queries read, loaded once for a <see cref="UsageContext"/> and shared
/// (the Usage window of the glossary). The apps are read on first use. Sessions are read from the earliest instant any
/// caller has asked for and kept, so a later call for a nearer instant is answered from memory; a call for an earlier
/// instant reads again from there. Not thread-safe: one window serves one query run.
/// </summary>
internal sealed class UsageWindow(IUsageSource source, UsageContext context)
{
    private AppGroups? _apps;
    private List<RawSession>? _raw;
    private long _rawFrom;

    public UsageContext Context { get; } = context;

    public long NowUtc => Context.Now.ToUnixTimeSeconds();

    public AppGroups Apps => _apps ??= source.Apps(Context.GamesOnly);

    /// <summary>
    /// The segments that overlap or follow <paramref name="from"/> up to now (and start before <paramref name="to"/> when
    /// given: Month reads a bounded span), by the rules of <see cref="UsageLoader.Resolve"/>.
    /// </summary>
    public List<UsageSeg> Segments(long from, long? to = null)
    {
        if (_raw is null || from < _rawFrom)
        {
            _raw = source.Sessions(from, NowUtc, null).ToList();
            _rawFrom = from;
        }
        return UsageLoader.Resolve(_raw.Where(s => (s.EndUtc is null || s.EndUtc >= from) && (to is null || s.StartUtc < to)), NowUtc, Apps, Context.Running);
    }
}
