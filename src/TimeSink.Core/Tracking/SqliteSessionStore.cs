using Microsoft.Data.Sqlite;
using TimeSink.Core.Data;

namespace TimeSink.Core.Tracking;

/// <summary>SQLite-backed <see cref="ISessionStore"/> over the shared <see cref="Db"/> connection.</summary>
public sealed class SqliteSessionStore : ISessionStore
{
    private const string SessionCols = "id, app_id, start_utc, end_utc, last_seen_utc, pid, proc_start_utc, end_reason";
    private readonly SqliteConnection _c;

    public SqliteSessionStore(Db db) => _c = db.Connection;

    public IReadOnlyList<TrackedApp> GetTrackedApps()
    {
        using var cmd = Cmd("SELECT id, exe_path, package_family, added_utc FROM apps WHERE state = 'tracked' ORDER BY id");
        using var r = cmd.ExecuteReader();
        var list = new List<TrackedApp>();
        while (r.Read())
            list.Add(new TrackedApp(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetInt64(3)));
        return list;
    }

    public IReadOnlyList<SessionRecord> GetOpenSessions() => Sessions("WHERE end_utc IS NULL");

    public IReadOnlyList<SessionRecord> GetAllSessions() => Sessions("");

    public SessionRecord OpenSession(long appId, long startUtc, int? pid, long? procStartUtc)
    {
        var start = startUtc;
        while (Exists(appId, start)) start++;
        using var cmd = Cmd($"INSERT INTO sessions (app_id, start_utc, last_seen_utc, pid, proc_start_utc) VALUES ($a, $s, $s, $p, $ps) RETURNING {SessionCols}");
        cmd.Parameters.AddWithValue("$a", appId);
        cmd.Parameters.AddWithValue("$s", start);
        cmd.Parameters.AddWithValue("$p", (object?)pid ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ps", (object?)procStartUtc ?? DBNull.Value);
        using var r = cmd.ExecuteReader();
        r.Read();
        return Read(r);
    }

    public void Heartbeat(long sessionId, long lastSeenUtc) =>
        Run("UPDATE sessions SET last_seen_utc = $t WHERE id = $id", ("$t", lastSeenUtc), ("$id", sessionId));

    public void CloseSession(long sessionId, long endUtc, string reason) =>
        Run("UPDATE sessions SET end_utc = $t, last_seen_utc = $t, end_reason = $r WHERE id = $id",
            ("$t", endUtc), ("$r", reason), ("$id", sessionId));

    public void DeleteSession(long sessionId) => Run("DELETE FROM sessions WHERE id = $id", ("$id", sessionId));

    public void AddDailyUsage(long appId, string day, long deltaSeconds)
    {
        if (deltaSeconds == 0) return;
        Run("INSERT INTO daily_usage (app_id, day, seconds) VALUES ($a, $d, $s) ON CONFLICT(app_id, day) DO UPDATE SET seconds = seconds + $s",
            ("$a", appId), ("$d", day), ("$s", deltaSeconds));
        Run("DELETE FROM daily_usage WHERE app_id = $a AND day = $d AND seconds <= 0", ("$a", appId), ("$d", day));
    }

    public IReadOnlyList<DailyUsageRow> GetDailyUsage()
    {
        using var cmd = Cmd("SELECT app_id, day, seconds FROM daily_usage ORDER BY app_id, day");
        using var r = cmd.ExecuteReader();
        var list = new List<DailyUsageRow>();
        while (r.Read()) list.Add(new DailyUsageRow(r.GetInt64(0), r.GetString(1), r.GetInt64(2)));
        return list;
    }

    public void ReplaceDailyUsage(IReadOnlyCollection<DailyUsageRow> rows)
    {
        using var tx = _c.BeginTransaction();
        Run("DELETE FROM daily_usage");
        foreach (var row in rows)
            Run("INSERT INTO daily_usage (app_id, day, seconds) VALUES ($a, $d, $s)",
                ("$a", row.AppId), ("$d", row.Day), ("$s", row.Seconds));
        tx.Commit();
    }

    private bool Exists(long appId, long start)
    {
        using var cmd = Cmd("SELECT 1 FROM sessions WHERE app_id = $a AND start_utc = $s");
        cmd.Parameters.AddWithValue("$a", appId);
        cmd.Parameters.AddWithValue("$s", start);
        return cmd.ExecuteScalar() is not null;
    }

    private IReadOnlyList<SessionRecord> Sessions(string where)
    {
        using var cmd = Cmd($"SELECT {SessionCols} FROM sessions {where} ORDER BY id");
        using var r = cmd.ExecuteReader();
        var list = new List<SessionRecord>();
        while (r.Read()) list.Add(Read(r));
        return list;
    }

    private static SessionRecord Read(SqliteDataReader r) => new(
        r.GetInt64(0), r.GetInt64(1), r.GetInt64(2),
        r.IsDBNull(3) ? null : r.GetInt64(3), r.GetInt64(4),
        r.IsDBNull(5) ? null : r.GetInt32(5), r.IsDBNull(6) ? null : r.GetInt64(6),
        r.IsDBNull(7) ? null : r.GetString(7));

    private SqliteCommand Cmd(string sql)
    {
        var cmd = _c.CreateCommand();
        cmd.CommandText = sql;
        return cmd;
    }

    private void Run(string sql, params (string Name, object Value)[] args)
    {
        using var cmd = Cmd(sql);
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }
}
