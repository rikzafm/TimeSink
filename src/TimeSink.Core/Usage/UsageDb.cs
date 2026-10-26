using Microsoft.Data.Sqlite;

namespace TimeSink.Core.Usage;

/// <summary>The one way the usage queries open the database: read-only, unpooled, waiting up to two seconds on a busy writer.</summary>
internal static class UsageDb
{
    public static SqliteConnection OpenReadOnly(string dbPath)
    {
        var c = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false,
        }.ToString());
        try
        {
            c.Open();
            using var p = c.CreateCommand();
            p.CommandText = "PRAGMA busy_timeout=2000";
            p.ExecuteNonQuery();
            return c;
        }
        catch
        {
            c.Dispose();
            throw;
        }
    }
}
