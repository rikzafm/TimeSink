using Microsoft.Data.Sqlite;

namespace TimeSink.Core.Data;

/// <summary>
/// Tiny key/value access to the <c>settings</c> table for the UI process, which must not run migrations.
/// Everything is best effort: a missing or locked database yields null / a no-op.
/// </summary>
public static class SettingsStore
{
    public static string? Get(string dbPath, string key)
    {
        if (!File.Exists(dbPath)) return null;
        try
        {
            using var c = Open(dbPath, SqliteOpenMode.ReadOnly);
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT value FROM settings WHERE key = $k";
            cmd.Parameters.AddWithValue("$k", key);
            return cmd.ExecuteScalar() as string;
        }
        catch (SqliteException) { return null; }
    }

    public static void Set(string dbPath, string key, string value)
    {
        if (!File.Exists(dbPath)) return;
        try
        {
            using var c = Open(dbPath, SqliteOpenMode.ReadWrite);
            using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO settings(key,value) VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
            cmd.Parameters.AddWithValue("$k", key);
            cmd.Parameters.AddWithValue("$v", value);
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException) { }
    }

    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, Pooling = false }.ToString());
        c.Open();
        using var p = c.CreateCommand();
        p.CommandText = "PRAGMA busy_timeout=2000";
        p.ExecuteNonQuery();
        return c;
    }
}
