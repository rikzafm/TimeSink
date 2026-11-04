using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using TimeSink.Core.Backup;

namespace TimeSink.Core.Data.Migrations;

/// <summary>
/// Migration M004: creates the devices table, adds apps.device_id and apps.bundle_id,
/// and changes the exe_path uniqueness constraint to (device_id, exe_path) by rebuilding the apps table.
/// Forward-only, idempotent, never edited once shipped.
/// </summary>
internal static class M004_Devices
{
    public const int Version = 4;

    public static void Apply(SqliteConnection connection)
    {
        // 1. Safety backup if physical file exists and version < 4
        CreateSafetyBackup(connection);

        // 2. Create devices table
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS devices (
                    device_id       TEXT PRIMARY KEY,
                    name            TEXT NOT NULL,
                    platform        TEXT NOT NULL,
                    is_local        INTEGER NOT NULL,
                    first_seen_utc  TEXT NOT NULL,
                    last_import_utc TEXT,
                    last_file_utc   TEXT,
                    state           TEXT NOT NULL DEFAULT 'active' CHECK (state IN ('active','forgotten'))
                );
                """;
            cmd.ExecuteNonQuery();
        }

        // 3. Resolve or create local device id and name
        string deviceId;
        string deviceName;
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT value FROM settings WHERE key = 'device.id'";
            deviceId = cmd.ExecuteScalar() as string ?? "";
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT value FROM settings WHERE key = 'device.name'";
            deviceName = cmd.ExecuteScalar() as string ?? "";
        }

        if (string.IsNullOrEmpty(deviceId))
        {
            deviceId = Guid.NewGuid().ToString();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO settings (key, value) VALUES ('device.id', $id) ON CONFLICT(key) DO UPDATE SET value = $id";
            cmd.Parameters.AddWithValue("$id", deviceId);
            cmd.ExecuteNonQuery();
        }

        if (string.IsNullOrEmpty(deviceName))
        {
            try { deviceName = Environment.MachineName; } catch { deviceName = "Unknown Device"; }
            if (string.IsNullOrWhiteSpace(deviceName)) deviceName = "Unknown Device";

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO settings (key, value) VALUES ('device.name', $n) ON CONFLICT(key) DO UPDATE SET value = $n";
            cmd.Parameters.AddWithValue("$n", deviceName);
            cmd.ExecuteNonQuery();
        }

        var platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" : "macos";
        var nowUtc = DateTimeOffset.UtcNow.ToString("O");

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO devices (device_id, name, platform, is_local, first_seen_utc, state)
                VALUES ($id, $name, $platform, 1, $now, 'active')
                ON CONFLICT(device_id) DO UPDATE SET is_local = 1, state = 'active';
                """;
            cmd.Parameters.AddWithValue("$id", deviceId);
            cmd.Parameters.AddWithValue("$name", deviceName);
            cmd.Parameters.AddWithValue("$platform", platform);
            cmd.Parameters.AddWithValue("$now", nowUtc);
            cmd.ExecuteNonQuery();
        }

        // 4. Rebuild apps table if device_id column does not exist yet
        bool hasDeviceId;
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('apps') WHERE name = 'device_id'";
            hasDeviceId = ((long?)cmd.ExecuteScalar() ?? 0) > 0;
        }

        if (!hasDeviceId)
        {
            RebuildAppsTable(connection, deviceId);
        }

        bool hasUpdatedUtc;
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('apps') WHERE name = 'updated_utc'";
            hasUpdatedUtc = ((long?)cmd.ExecuteScalar() ?? 0) > 0;
        }
        if (!hasUpdatedUtc)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "ALTER TABLE apps ADD COLUMN updated_utc INTEGER NOT NULL DEFAULT 0";
            cmd.ExecuteNonQuery();
        }
    }

    private static void RebuildAppsTable(SqliteConnection connection, string defaultDeviceId)
    {
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA foreign_keys = OFF;";
            cmd.ExecuteNonQuery();
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE apps_new (
                    id             INTEGER PRIMARY KEY,
                    canonical_id   INTEGER NULL REFERENCES apps_new(id),
                    exe_path       TEXT NOT NULL COLLATE NOCASE,
                    bundle_id      TEXT NULL,
                    device_id      TEXT NOT NULL REFERENCES devices(device_id),
                    package_family TEXT NULL,
                    display_name   TEXT NOT NULL,
                    category       TEXT NOT NULL DEFAULT 'Other',
                    state          TEXT NOT NULL DEFAULT 'tracked' CHECK (state IN ('tracked','retired')),
                    added_utc      INTEGER NOT NULL,
                    retired_utc    INTEGER NULL,
                    is_background  INTEGER NOT NULL DEFAULT 0,
                    kind           INTEGER NULL,
                    updated_utc    INTEGER NOT NULL DEFAULT 0
                );

                CREATE UNIQUE INDEX ux_apps_device_exe ON apps_new(device_id, exe_path COLLATE NOCASE);
                CREATE INDEX ix_apps_device_bundle ON apps_new(device_id, bundle_id);
                CREATE INDEX ix_apps_canonical ON apps_new(canonical_id);
                """;
            cmd.ExecuteNonQuery();
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO apps_new (
                    id, canonical_id, exe_path, bundle_id, device_id,
                    package_family, display_name, category, state,
                    added_utc, retired_utc, is_background, kind
                )
                SELECT
                    id, canonical_id, exe_path, NULL, $devId,
                    package_family, display_name, category, state,
                    added_utc, retired_utc, is_background, kind
                FROM apps;
                """;
            cmd.Parameters.AddWithValue("$devId", defaultDeviceId);
            cmd.ExecuteNonQuery();
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                DROP TABLE apps;
                ALTER TABLE apps_new RENAME TO apps;
                PRAGMA foreign_keys = ON;
                """;
            cmd.ExecuteNonQuery();
        }
    }

    private static void CreateSafetyBackup(SqliteConnection connection)
    {
        try
        {
            var dbPath = connection.DataSource;
            if (!string.IsNullOrEmpty(dbPath) && File.Exists(dbPath))
            {
                var dir = Path.GetDirectoryName(dbPath) ?? "";
                var backupFolder = Path.Combine(dir, "backups");
                Directory.CreateDirectory(backupFolder);
                var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
                var destFile = Path.Combine(backupFolder, $"pre-migrate-{timestamp}{BackupService.Extension}");
                BackupService.Create(dbPath, destFile, DateTimeOffset.UtcNow);
            }
        }
        catch
        {
            // Non-fatal if safety backup cannot be created (e.g. read-only folder or in-memory)
        }
    }
}
