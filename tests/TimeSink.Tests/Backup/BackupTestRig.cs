using Microsoft.Data.Sqlite;
using TimeSink.Core.Data;

namespace TimeSink.Tests.Backup;

/// <summary>Temp directory plus helpers to seed and read databases through plain SQL (the data, not the code under test).</summary>
public sealed class BackupTestRig : IDisposable
{
    public string Dir { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "timesink-backup-" + Guid.NewGuid().ToString("N"));

    public BackupTestRig() => Directory.CreateDirectory(Dir);

    public string P(string name) => System.IO.Path.Combine(Dir, name);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Dir, true); } catch (IOException) { }
    }

    /// <summary>Creates a database with the given apps and sessions. Sessions: (exe, startUtc, endUtc).</summary>
    public string NewDb(string name, params (string Exe, long Start, long? End)[] sessions)
    {
        var path = P(name);
        using (var db = Db.Open(path))
        {
            foreach (var exe in sessions.Select(s => s.Exe).Distinct(StringComparer.OrdinalIgnoreCase))
                db.InsertApp(exe, System.IO.Path.GetFileNameWithoutExtension(exe), 1_700_000_000);
        }
        using var c = Open(path);
        foreach (var (exe, start, end) in sessions)
            Exec(c, "INSERT INTO sessions (app_id, start_utc, end_utc, last_seen_utc, end_reason) " +
                    "SELECT id, $s, $e, $l, CASE WHEN $e IS NULL THEN NULL ELSE 'exit' END FROM apps WHERE exe_path = $x",
                ("$s", start), ("$e", end ?? (object)DBNull.Value), ("$l", end ?? start + 60), ("$x", exe));
        return path;
    }

    public SqliteConnection Open(string path)
    {
        var c = new SqliteConnection($"Data Source={path};Pooling=False");
        c.Open();
        return c;
    }

    public static void Exec(SqliteConnection c, string sql, params (string, object)[] args)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    public void Exec(string dbPath, string sql)
    {
        using var c = Open(dbPath);
        Exec(c, sql);
    }

    public T Scalar<T>(string dbPath, string sql)
    {
        using var c = Open(dbPath);
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return (T)Convert.ChangeType(cmd.ExecuteScalar()!, typeof(T));
    }

    public List<string> Rows(string dbPath, string sql)
    {
        using var c = Open(dbPath);
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        var rows = new List<string>();
        while (r.Read())
            rows.Add(string.Join("|", Enumerable.Range(0, r.FieldCount).Select(i => Convert.ToString(r.GetValue(i), System.Globalization.CultureInfo.InvariantCulture))));
        return rows;
    }

    /// <summary>Everything the user would notice: apps, sessions and derived daily usage, in a stable order.</summary>
    public string Fingerprint(string dbPath) => string.Join("\n",
        Rows(dbPath, "SELECT exe_path, display_name, state, category, canonical_id FROM apps ORDER BY exe_path")
        .Concat(Rows(dbPath, "SELECT a.exe_path, s.start_utc, s.end_utc, s.end_reason FROM sessions s JOIN apps a ON a.id = s.app_id ORDER BY a.exe_path, s.start_utc"))
        .Concat(Rows(dbPath, "SELECT a.exe_path, d.day, d.seconds FROM daily_usage d JOIN apps a ON a.id = d.app_id ORDER BY a.exe_path, d.day")));
}
