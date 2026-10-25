using Microsoft.Data.Sqlite;

namespace TimeSink.Core.Data.Migrations;

/// <summary>Adds <c>apps.is_background</c>: apps that run all day (Steam, Wallpaper Engine) rank below the rest. Never edit once shipped.</summary>
internal static class M002_BackgroundApps
{
    public const int Version = 2;

    public static void Apply(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "ALTER TABLE apps ADD COLUMN is_background INTEGER NOT NULL DEFAULT 0";
        cmd.ExecuteNonQuery();
    }
}
