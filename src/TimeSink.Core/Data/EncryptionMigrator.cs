using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;
using TimeSink.Core.Backup;

namespace TimeSink.Core.Data;

/// <summary>
/// Handles one-time migration from unencrypted SQLite 3 database to SQLCipher-encrypted database,
/// with pre-migration safety backup, WAL flushing, schema version preservation, and crash resilience.
/// </summary>
public static class EncryptionMigrator
{
    private static readonly byte[] SqliteMagicHeader = "SQLite format 3\0"u8.ToArray();

    /// <summary>
    /// Checks whether the given database file exists and starts with the standard unencrypted SQLite 3 header.
    /// </summary>
    public static bool IsPlaintextDatabase(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length < 16) return false;
            Span<byte> header = stackalloc byte[16];
            var bytesRead = fs.Read(header);
            return bytesRead == 16 && header.SequenceEqual(SqliteMagicHeader);
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Cleans up any stale temporary files from an interrupted migration.
    /// </summary>
    public static void CleanStaleMigrationFiles(string path)
    {
        var tempDbPath = path + ".migrating";
        var backupPath = path + ".bak";

        try
        {
            if (File.Exists(tempDbPath))
            {
                File.Delete(tempDbPath);
            }
        }
        catch (IOException) { }

        try
        {
            // If the original database is missing but the backup file exists, recover it
            if (!File.Exists(path) && File.Exists(backupPath))
            {
                File.Move(backupPath, path);
            }
            else if (File.Exists(path) && File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }
        }
        catch (IOException) { }
    }

    /// <summary>
    /// Migrates an unencrypted SQLite 3 database to SQLCipher encryption using the provided key.
    /// </summary>
    public static void Migrate(string path, byte[] key, string? backupFolder = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(key);

        CleanStaleMigrationFiles(path);

        if (!IsPlaintextDatabase(path))
        {
            return;
        }

        // 1. Create safety backup before touching anything
        CreateSafetyBackup(path, backupFolder);

        var tempDbPath = path + ".migrating";
        var backupPath = path + ".bak";
        var hexKey = Convert.ToHexString(key);
        int originalVersion;

        // 2. Open plaintext database without key, flush WAL, and export to encrypted temp database
        using (var plainConn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString()))
        {
            plainConn.Open();

            // Checkpoint WAL frames into main file
            using (var cmd = plainConn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                cmd.ExecuteNonQuery();
            }

            // Capture original user_version
            using (var cmd = plainConn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA user_version;";
                originalVersion = Convert.ToInt32(cmd.ExecuteScalar());
            }

            if (File.Exists(tempDbPath)) File.Delete(tempDbPath);

            var quotedTempPath = tempDbPath.Replace('\\', '/').Replace("'", "''");
            using (var cmd = plainConn.CreateCommand())
            {
                cmd.CommandText = $"ATTACH DATABASE '{quotedTempPath}' AS encrypted KEY 'x''{hexKey}''';";
                cmd.ExecuteNonQuery();
            }

            try
            {
                // Export schema, tables, and data to encrypted database
                using (var cmd = plainConn.CreateCommand())
                {
                    cmd.CommandText = "SELECT sqlcipher_export('encrypted');";
                    cmd.ExecuteNonQuery();
                }

                // Preserve schema user_version so future migrations do not re-run
                using (var cmd = plainConn.CreateCommand())
                {
                    cmd.CommandText = $"PRAGMA encrypted.user_version = {originalVersion};";
                    cmd.ExecuteNonQuery();
                }
            }
            finally
            {
                using var cmd = plainConn.CreateCommand();
                cmd.CommandText = "DETACH DATABASE encrypted;";
                cmd.ExecuteNonQuery();
            }
        }

        SqliteConnection.ClearAllPools();

        // 3. Verify integrity of the newly encrypted database
        using (var encConn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = tempDbPath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString()))
        {
            encConn.Open();
            using (var cmd = encConn.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA key = \"x'{hexKey}'\";";
                cmd.ExecuteNonQuery();
            }

            using (var cmd = encConn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA integrity_check;";
                var integrity = cmd.ExecuteScalar() as string;
                if (integrity != "ok")
                {
                    throw new InvalidOperationException($"Integrity check failed for encrypted database: {integrity}");
                }
            }

            using (var cmd = encConn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA user_version;";
                var version = Convert.ToInt32(cmd.ExecuteScalar());
                if (version != originalVersion)
                {
                    throw new InvalidOperationException($"User version mismatch: expected {originalVersion}, got {version}");
                }
            }
        }

        SqliteConnection.ClearAllPools();

        // 4. Remove old WAL and SHM files
        var walPath = path + "-wal";
        var shmPath = path + "-shm";
        if (File.Exists(walPath)) try { File.Delete(walPath); } catch (IOException) { }
        if (File.Exists(shmPath)) try { File.Delete(shmPath); } catch (IOException) { }

        // 5. Atomic file replacement
        try
        {
            File.Replace(tempDbPath, path, backupPath, ignoreMetadataErrors: true);
            if (File.Exists(backupPath))
            {
                try { File.Delete(backupPath); } catch (IOException) { }
            }
        }
        catch (PlatformNotSupportedException)
        {
            File.Move(path, backupPath, overwrite: true);
            File.Move(tempDbPath, path, overwrite: true);
            if (File.Exists(backupPath))
            {
                try { File.Delete(backupPath); } catch (IOException) { }
            }
        }
    }

    private static void CreateSafetyBackup(string path, string? backupFolder)
    {
        try
        {
            var folder = backupFolder;
            if (string.IsNullOrEmpty(folder))
            {
                var dir = Path.GetDirectoryName(path);
                folder = !string.IsNullOrEmpty(dir)
                    ? Path.Combine(dir, "backups")
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeSink", "backups");
            }

            Directory.CreateDirectory(folder);
            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
            var destFile = Path.Combine(folder, $"pre-encrypt-{timestamp}{BackupService.Extension}");
            BackupService.Create(path, destFile, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to create pre-encryption safety backup: {ex.Message}", ex);
        }
    }
}
