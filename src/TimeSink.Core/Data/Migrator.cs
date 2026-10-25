using Microsoft.Data.Sqlite;
using TimeSink.Core.Data.Migrations;

namespace TimeSink.Core.Data;

internal sealed record Migration(int Version, Action<SqliteConnection> Apply);

/// <summary>Forward-only schema migrations keyed by <c>PRAGMA user_version</c>.</summary>
internal static class Migrator
{
    public static readonly IReadOnlyList<Migration> Shipped = 
        [new(M001_Initial.Version, M001_Initial.Apply), new(M002_BackgroundApps.Version, M002_BackgroundApps.Apply), new(M003_AppKind.Version, M003_AppKind.Apply)];

    public static int LatestVersion => Shipped[^1].Version;

    public static int CurrentVersion(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA user_version";
        return (int)(long)cmd.ExecuteScalar()!;
    }

    public static void Migrate(SqliteConnection connection, IReadOnlyList<Migration>? migrations = null)
    {
        migrations ??= Shipped;
        foreach (var m in migrations.OrderBy(m => m.Version))
        {
            if (CurrentVersion(connection) >= m.Version) continue;
            using var tx = connection.BeginTransaction();
            m.Apply(connection);
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"PRAGMA user_version = {m.Version}";
            cmd.ExecuteNonQuery();
            tx.Commit();
        }
    }
}
