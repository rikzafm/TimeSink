using Microsoft.Data.Sqlite;

namespace TimeSink.Core.Data.Migrations;

/// <summary>Adds <c>apps.updated_utc</c>: when an app's classification (kind, category, name, background) last changed, for last-write-wins sync. Never edit once shipped.</summary>
internal static class M005_AppUpdatedUtc
{
    public const int Version = 5;

    public static void Apply(SqliteConnection connection)
    {
        using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('apps') WHERE name = 'updated_utc'";
            if (((long?)check.ExecuteScalar() ?? 0) > 0) return;
        }
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "ALTER TABLE apps ADD COLUMN updated_utc INTEGER NOT NULL DEFAULT 0";
        cmd.ExecuteNonQuery();
    }
}

