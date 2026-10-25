using Microsoft.Data.Sqlite;

namespace TimeSink.Core.Data.Migrations;

/// <summary>Adds <c>apps.kind</c>: 0 other, 1 game, 2 gacha game; NULL means "not chosen, detect it" (Steam folder, known gacha names). Never edit once shipped.</summary>
internal static class M003_AppKind
{
    public const int Version = 3;

    public static void Apply(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "ALTER TABLE apps ADD COLUMN kind INTEGER";
        cmd.ExecuteNonQuery();
    }
}
