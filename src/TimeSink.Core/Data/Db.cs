using Microsoft.Data.Sqlite;
using TimeSink.Core.Data.Migrations;

namespace TimeSink.Core.Data;

public sealed record AppRow(long Id, string ExePath, string DisplayName, string State);

/// <summary>Opens (and migrates) the TimeSink SQLite database.</summary>
public sealed class Db : IDisposable
{
    private readonly SqliteConnection _connection;

    private Db(SqliteConnection connection) => _connection = connection;

    public static Db Open(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        connection.Open();
        try
        {
            Execute(connection, "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;");
            Migrate(connection);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
        return new Db(connection);
    }

    internal SqliteConnection Connection => _connection;

    public int SchemaVersion => (int)(long)Scalar("PRAGMA user_version")!;

    public string JournalMode => (string)Scalar("PRAGMA journal_mode")!;

    public long InsertApp(string exePath, string displayName, long addedUtc)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "INSERT INTO apps (exe_path, display_name, added_utc) VALUES ($p, $n, $t) RETURNING id";
        cmd.Parameters.AddWithValue("$p", exePath);
        cmd.Parameters.AddWithValue("$n", displayName);
        cmd.Parameters.AddWithValue("$t", addedUtc);
        return (long)cmd.ExecuteScalar()!;
    }

    public void SetAppState(long appId, string state, long utc)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE apps SET state = $s, retired_utc = CASE WHEN $s = 'retired' THEN $t ELSE NULL END WHERE id = $id";
        cmd.Parameters.AddWithValue("$s", state);
        cmd.Parameters.AddWithValue("$t", utc);
        cmd.Parameters.AddWithValue("$id", appId);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<AppRow> GetApps()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, exe_path, display_name, state FROM apps ORDER BY id";
        using var reader = cmd.ExecuteReader();
        var rows = new List<AppRow>();
        while (reader.Read())
            rows.Add(new AppRow(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return rows;
    }

    public string? GetSetting(string key)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    public void SetSetting(string key, string value)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "INSERT INTO settings (key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = $v";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _connection.Dispose();

    private static void Migrate(SqliteConnection connection)
    {
        var current = (int)(long)Scalar(connection, "PRAGMA user_version")!;
        if (current < M001_Initial.Version)
        {
            using var tx = connection.BeginTransaction();
            M001_Initial.Apply(connection);
            Execute(connection, $"PRAGMA user_version = {M001_Initial.Version}");
            tx.Commit();
        }
    }

    private object? Scalar(string sql) => Scalar(_connection, sql);

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
