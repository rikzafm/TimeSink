using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using TimeSink.Core.Apps;
using TimeSink.Core.Data;
using TimeSink.Core.Devices;
using TimeSink.Core.Security;
using TimeSink.Core.Settings;

namespace TimeSink.Core.Sync;

/// <summary>
/// Writes &lt;deviceId&gt;.tssync snapshot archives atomically (shared/sync-format.md section 2).
/// Contains only this device's apps and sessions.
/// </summary>
public static class SyncWriter
{
    public const string Extension = ".tssync";

    public static string Write(string dbPath, string syncFolder, DateTimeOffset now, string appVersion = "1.0.0", IKeyManager? keyManager = null)
    {
        using var db = Db.Open(dbPath);
        return Write(db.Connection, syncFolder, now, appVersion, keyManager);
    }

    public static string Write(SqliteConnection connection, string syncFolder, DateTimeOffset now, string appVersion = "1.0.0", IKeyManager? keyManager = null)
    {
        Directory.CreateDirectory(syncFolder);
        keyManager ??= Db.KeyManager;

        // Inspect encryption setting and passphrase
        bool isEncrypted = false;
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT value FROM settings WHERE key = $k";
            cmd.Parameters.AddWithValue("$k", Setting.SyncEncrypted.Key);
            isEncrypted = Setting.SyncEncrypted.Parse(cmd.ExecuteScalar() as string);
        }

        string? passphrase = null;
        if (isEncrypted)
        {
            passphrase = keyManager.GetSyncPassphrase();
            if (string.IsNullOrEmpty(passphrase))
            {
                throw new InvalidOperationException("Sync encryption is enabled but no passphrase is configured.");
            }
        }

        var local = DeviceIdentity.GetLocalDevice(connection);
        var localDeviceId = local.DeviceId;
        var localDeviceName = local.Name;
        var platform = local.Platform;

        var targetFile = Path.Combine(syncFolder, $"{localDeviceId}{Extension}");
        var tmpFile = Path.Combine(syncFolder, $"{localDeviceId}{Extension}.tmp");

        var scratchDir = Path.Combine(Path.GetTempPath(), "timesink-sync-out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratchDir);

        try
        {
            var snapshotDbPath = Path.Combine(scratchDir, SyncManifest.DbEntryName);

            // 1. Create stripped snapshot.db (encrypted if enabled)
            CreateSnapshotDatabase(connection, snapshotDbPath, localDeviceId, passphrase);

            // 2. Count sessions and calculate SHA-256 hash
            int sessionCount;
            using (var snapConn = new SqliteConnection(new SqliteConnectionStringBuilder
                   { DataSource = snapshotDbPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            {
                snapConn.Open();
                if (!string.IsNullOrEmpty(passphrase))
                {
                    var escPass = passphrase.Replace("'", "''");
                    using var pragma = snapConn.CreateCommand();
                    pragma.CommandText = $"PRAGMA key = '{escPass}';";
                    pragma.ExecuteNonQuery();
                }

                using var cmd = snapConn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM sessions";
                sessionCount = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
            }

            string hashHex;
            using (var sha256 = SHA256.Create())
            using (var fs = File.OpenRead(snapshotDbPath))
            {
                var hashBytes = sha256.ComputeHash(fs);
                hashHex = Convert.ToHexStringLower(hashBytes);
            }

            // 3. Create manifest (schema 5, Encrypted flag)
            var manifest = new SyncManifest(
                Format: 1,
                SchemaVersion: Math.Max(5, Migrator.Shipped.Max(m => m.Version)),
                AppVersion: appVersion,
                DeviceId: localDeviceId,
                DeviceName: localDeviceName,
                Platform: platform,
                CreatedUtc: now,
                SessionCount: sessionCount,
                Hash: hashHex,
                Encrypted: isEncrypted
            );

            // 4. Create zip archive atomically at .tmp
            if (File.Exists(tmpFile)) File.Delete(tmpFile);
            using (var zip = ZipFile.Open(tmpFile, ZipArchiveMode.Create))
            {
                var manifestEntry = zip.CreateEntry(SyncManifest.EntryName);
                using (var s = new StreamWriter(manifestEntry.Open(), new UTF8Encoding(false)))
                {
                    s.Write(manifest.ToJson());
                }
                zip.CreateEntryFromFile(snapshotDbPath, SyncManifest.DbEntryName, CompressionLevel.Optimal);
            }

            // 5. Atomic rename to target file
            File.Move(tmpFile, targetFile, overwrite: true);
            return targetFile;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { if (File.Exists(tmpFile)) File.Delete(tmpFile); } catch (IOException) { }
            try { Directory.Delete(scratchDir, true); } catch (IOException) { }
        }
    }

    private static void CreateSnapshotDatabase(SqliteConnection sourceConn, string snapshotDbPath, string localDeviceId, string? passphrase)
    {
        // Build empty tables in snapshot.db with SQLCipher keying if encrypted
        using (var snapConn = new SqliteConnection(new SqliteConnectionStringBuilder
               { DataSource = snapshotDbPath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString()))
        {
            snapConn.Open();
            if (!string.IsNullOrEmpty(passphrase))
            {
                var escPass = passphrase.Replace("'", "''");
                using var pragma = snapConn.CreateCommand();
                pragma.CommandText = $"PRAGMA key = '{escPass}'; PRAGMA cipher_page_size = 4096; PRAGMA kdf_iter = 256000;";
                pragma.ExecuteNonQuery();
            }

            using var cmd = snapConn.CreateCommand();
            cmd.CommandText = $"""
                PRAGMA user_version = {Math.Max(5, Migrator.Shipped.Max(m => m.Version))};

                CREATE TABLE apps (
                    id             INTEGER PRIMARY KEY,
                    canonical_id   INTEGER NULL,
                    exe_path       TEXT NOT NULL COLLATE NOCASE,
                    bundle_id      TEXT NULL,
                    device_id      TEXT NOT NULL,
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
                CREATE UNIQUE INDEX ux_apps_device_exe ON apps(device_id, exe_path COLLATE NOCASE);
                CREATE INDEX ix_apps_device_bundle ON apps(device_id, bundle_id);
                CREATE INDEX ix_apps_canonical ON apps(canonical_id);

                CREATE TABLE sessions (
                    id            INTEGER PRIMARY KEY,
                    app_id        INTEGER NOT NULL REFERENCES apps(id),
                    start_utc     INTEGER NOT NULL,
                    end_utc       INTEGER NULL,
                    last_seen_utc INTEGER NOT NULL,
                    pid           INTEGER NULL,
                    proc_start_utc INTEGER NULL,
                    end_reason    TEXT NULL
                );
                CREATE INDEX ix_sessions_app_start ON sessions(app_id, start_utc);
                CREATE UNIQUE INDEX ux_sessions_app_start ON sessions(app_id, start_utc);

                CREATE TABLE icons (
                    app_id INTEGER PRIMARY KEY REFERENCES apps(id),
                    png BLOB NOT NULL,
                    hash TEXT NOT NULL
                );

                CREATE TABLE cross_device_merges (
                    source_device_id TEXT NOT NULL,
                    source_exe_path TEXT NOT NULL DEFAULT '',
                    source_bundle_id TEXT NOT NULL DEFAULT '',
                    target_device_id TEXT NOT NULL,
                    target_exe_path TEXT NOT NULL DEFAULT '',
                    target_bundle_id TEXT NOT NULL DEFAULT ''
                );
                """;
            cmd.ExecuteNonQuery();
        }

        // Attach snapshotDb to sourceConn and copy data for localDeviceId only
        var escapedPath = snapshotDbPath.Replace('\\', '/').Replace("'", "''");
        using (var cmd = sourceConn.CreateCommand())
        {
            if (!string.IsNullOrEmpty(passphrase))
            {
                var escPass = passphrase.Replace("'", "''");
                cmd.CommandText = $"ATTACH DATABASE '{escapedPath}' AS snap KEY '{escPass}';";
            }
            else
            {
                cmd.CommandText = $"ATTACH DATABASE '{escapedPath}' AS snap KEY '';";
            }
            cmd.ExecuteNonQuery();
        }

        try
        {
            using var cmd = sourceConn.CreateCommand();
            cmd.CommandText = """
                INSERT OR IGNORE INTO snap.apps (
                    id, canonical_id, exe_path, bundle_id, device_id,
                    package_family, display_name, category, state,
                    added_utc, retired_utc, is_background, kind, updated_utc
                )
                SELECT DISTINCT
                    id, canonical_id, exe_path, bundle_id, device_id,
                    package_family, display_name, category, state,
                    added_utc, retired_utc, is_background, kind, updated_utc
                FROM main.apps
                WHERE device_id = $devId
                   OR updated_utc > 0
                   OR id IN (SELECT canonical_id FROM main.apps WHERE device_id = $devId AND canonical_id IS NOT NULL)
                   OR canonical_id IN (SELECT id FROM main.apps WHERE device_id = $devId);

                INSERT INTO snap.sessions (
                    id, app_id, start_utc, end_utc, last_seen_utc, pid, proc_start_utc, end_reason
                )
                SELECT
                    s.id, s.app_id, s.start_utc, s.end_utc, s.last_seen_utc, s.pid, s.proc_start_utc, s.end_reason
                FROM main.sessions s
                JOIN main.apps a ON a.id = s.app_id
                WHERE a.device_id = $devId;

                INSERT OR IGNORE INTO snap.icons (app_id, png, hash)
                SELECT
                    i.app_id, i.png, i.hash
                FROM main.icons i
                JOIN snap.apps a ON a.id = i.app_id;

                INSERT INTO snap.cross_device_merges (
                    source_device_id, source_exe_path, source_bundle_id,
                    target_device_id, target_exe_path, target_bundle_id
                )
                SELECT
                    a.device_id, a.exe_path, COALESCE(a.bundle_id, ''),
                    c.device_id, c.exe_path, COALESCE(c.bundle_id, '')
                FROM main.apps a
                JOIN main.apps c ON c.id = a.canonical_id
                WHERE (a.device_id = $devId OR c.device_id = $devId)
                  AND a.device_id != c.device_id;
                """;
            cmd.Parameters.AddWithValue("$devId", localDeviceId);
            cmd.ExecuteNonQuery();

            // Resolve any NULL kind so destination devices receive detected classification (e.g. Gacha/Game)
            using (var selectCmd = sourceConn.CreateCommand())
            {
                selectCmd.CommandText = "SELECT id, exe_path, display_name FROM snap.apps WHERE kind IS NULL;";
                var appsToResolve = new List<(long Id, string ExePath, string DisplayName)>();
                using (var r = selectCmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        appsToResolve.Add((r.GetInt64(0), r.GetString(1), r.GetString(2)));
                    }
                }
                foreach (var app in appsToResolve)
                {
                    var resolvedKind = (int)AppKinds.Resolve(null, app.ExePath, app.DisplayName);
                    using var updCmd = sourceConn.CreateCommand();
                    updCmd.CommandText = "UPDATE snap.apps SET kind = $k WHERE id = $id;";
                    updCmd.Parameters.AddWithValue("$k", resolvedKind);
                    updCmd.Parameters.AddWithValue("$id", app.Id);
                    updCmd.ExecuteNonQuery();
                }
            }
        }
        finally
        {
            using var cmd = sourceConn.CreateCommand();
            cmd.CommandText = "DETACH DATABASE snap;";
            cmd.ExecuteNonQuery();
        }
    }
}
