using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;
using TimeSink.Core.Data;
using TimeSink.Core.Tracking;
using TimeSink.Core.Usage;

namespace TimeSink.Core.Backup;

/// <summary>What the Restore dialog shows after picking a file.</summary>
public sealed record BackupInspection(BackupManifest Manifest, int Apps, long Sessions, bool NeedsMigration);

public sealed record ReplaceResult(string? SafetyCopy);

public sealed record MergeResult(int AppsAdded, int SessionsAdded, int SessionsSkipped);

/// <summary>Validates, replaces from and merges from <c>.timesink</c> backups (implementation.md section 10).</summary>
public static class RestoreService
{
    /// <summary>Validates the archive, manifest, schema version and integrity. Throws <see cref="BackupException"/> if unusable.</summary>
    public static BackupInspection Inspect(string backupFile) => Inspect(backupFile, null);

    internal static BackupInspection Inspect(string backupFile, IReadOnlyList<Migration>? migrations)
    {
        using var prepared = Prepare(backupFile, migrations);
        using var c = Open(prepared.DbPath);
        return new BackupInspection(prepared.Manifest, (int)Count(c, "apps"), Count(c, "sessions"), prepared.Migrated);
    }

    /// <summary>
    /// Replace: safety copy of the current database into <paramref name="safetyFolder"/> (<c>pre-restore-*.timesink</c>),
    /// then the live database content is swapped for the backup's inside one transaction, open sessions are closed
    /// as 'restore' and <c>daily_usage</c> is rebuilt. The caller tells the Agent to drop its in-memory sessions.
    /// </summary>
    public static ReplaceResult Replace(string backupFile, string dbPath, string safetyFolder, DateTimeOffset now, TimeZoneInfo zone) =>
        Replace(backupFile, dbPath, safetyFolder, now, zone, null);

    internal static ReplaceResult Replace(string backupFile, string dbPath, string safetyFolder, DateTimeOffset now, TimeZoneInfo zone,
        IReadOnlyList<Migration>? migrations)
    {
        using var prepared = Prepare(backupFile, migrations);

        string? safety = null;
        if (File.Exists(dbPath))
        {
            safety = Path.Combine(safetyFolder, $"pre-restore-{now.UtcDateTime:yyyyMMdd-HHmmss}{BackupService.Extension}");
            BackupService.Create(dbPath, safety, now);
        }

        using var db = Db.Open(dbPath);
        var c = db.Connection;
        Exec(c, $"ATTACH DATABASE '{Quote(prepared.DbPath)}' AS src");
        try
        {
            using (var tx = c.BeginTransaction())
            {
                Exec(c, "PRAGMA defer_foreign_keys = ON");
                foreach (var table in Tables(c, "main"))
                {
                    var cols = string.Join(", ", Columns(c, table).Select(col => $"\"{col}\""));
                    Exec(c, $"DELETE FROM main.\"{table}\"");
                    Exec(c, $"INSERT INTO main.\"{table}\" ({cols}) SELECT {cols} FROM src.\"{table}\"");
                }
                Exec(c, "UPDATE sessions SET end_utc = last_seen_utc, end_reason = 'restore' WHERE end_utc IS NULL");
                tx.Commit();
            }
        }
        finally { Exec(c, "DETACH DATABASE src"); }

        new UsageAccumulator(new SqliteSessionStore(db), zone).RebuildAll();
        return new ReplaceResult(safety);
    }

    /// <summary>
    /// Merge: apps are matched by exe path (case-insensitive) or inserted; sessions are inserted with
    /// <c>INSERT OR IGNORE</c> on <c>(app_id, start_utc)</c>, so merging the same file again changes nothing.
    /// Existing settings are never overwritten. <c>daily_usage</c> is rebuilt afterwards.
    /// </summary>
    public static MergeResult Merge(string backupFile, string dbPath, TimeZoneInfo zone) => Merge(backupFile, dbPath, zone, null);

    internal static MergeResult Merge(string backupFile, string dbPath, TimeZoneInfo zone, IReadOnlyList<Migration>? migrations)
    {
        using var prepared = Prepare(backupFile, migrations);
        using var db = Db.Open(dbPath);
        var c = db.Connection;
        Exec(c, $"ATTACH DATABASE '{Quote(prepared.DbPath)}' AS src");
        int appsAdded, added, skipped;
        try
        {
            using var tx = c.BeginTransaction();
            Exec(c, "CREATE TEMP TABLE idmap (src_id INTEGER PRIMARY KEY, dst_id INTEGER NOT NULL)");

            var inserted = new List<(long Src, long Dst, long? SrcCanonical)>();
            using (var read = c.CreateCommand())
            {
                read.CommandText = "SELECT id, canonical_id, exe_path, package_family, display_name, category, state, added_utc, retired_utc FROM src.apps ORDER BY id";
                using var r = read.ExecuteReader();
                var apps = new List<object?[]>();
                while (r.Read()) { var row = new object?[9]; r.GetValues(row!); apps.Add(row.Select(v => v is DBNull ? null : v).ToArray()); }
                r.Close();
                foreach (var a in apps)
                {
                    var src = (long)a[0]!;
                    using var find = c.CreateCommand();
                    find.CommandText = "SELECT id FROM main.apps WHERE exe_path = $p";
                    find.Parameters.AddWithValue("$p", a[2]);
                    var existing = find.ExecuteScalar();
                    long dst;
                    if (existing is long id) dst = id;
                    else
                    {
                        using var ins = c.CreateCommand();
                        ins.CommandText = "INSERT INTO main.apps (exe_path, package_family, display_name, category, state, added_utc, retired_utc) " +
                                          "VALUES ($p, $f, $n, $c, $s, $a, $r) RETURNING id";
                        ins.Parameters.AddWithValue("$p", a[2]);
                        ins.Parameters.AddWithValue("$f", a[3] ?? DBNull.Value);
                        ins.Parameters.AddWithValue("$n", a[4]);
                        ins.Parameters.AddWithValue("$c", a[5]);
                        ins.Parameters.AddWithValue("$s", a[6]);
                        ins.Parameters.AddWithValue("$a", a[7]);
                        ins.Parameters.AddWithValue("$r", a[8] ?? DBNull.Value);
                        dst = (long)ins.ExecuteScalar()!;
                        inserted.Add((src, dst, a[1] as long?));
                    }
                    Exec(c, $"INSERT INTO temp.idmap VALUES ({src}, {dst})");
                }
            }
            // Canonical (merge-target) links are remapped for the apps we created; existing apps keep their own.
            foreach (var (_, dst, srcCanonical) in inserted)
                if (srcCanonical is { } sc)
                    Exec(c, $"UPDATE main.apps SET canonical_id = (SELECT dst_id FROM temp.idmap WHERE src_id = {sc}) WHERE id = {dst}");

            var total = Count(c, "src.sessions");
            added = (int)ExecCount(c,
                "INSERT OR IGNORE INTO main.sessions (app_id, start_utc, end_utc, last_seen_utc, end_reason) " +
                "SELECT m.dst_id, s.start_utc, COALESCE(s.end_utc, s.last_seen_utc), s.last_seen_utc, COALESCE(s.end_reason, 'restore') " +
                "FROM src.sessions s JOIN temp.idmap m ON m.src_id = s.app_id");
            skipped = (int)total - added;
            Exec(c, "INSERT OR IGNORE INTO main.icons (app_id, png, hash) SELECT m.dst_id, i.png, i.hash FROM src.icons i JOIN temp.idmap m ON m.src_id = i.app_id");
            Exec(c, "INSERT OR IGNORE INTO main.ignored_suggestions (exe_path, utc) SELECT exe_path, utc FROM src.ignored_suggestions");
            Exec(c, "DROP TABLE temp.idmap");
            tx.Commit();
            appsAdded = inserted.Count;
        }
        finally { Exec(c, "DETACH DATABASE src"); }

        new UsageAccumulator(new SqliteSessionStore(db), zone).RebuildAll();
        return new MergeResult(appsAdded, added, skipped);
    }

    // ---- preparation: unzip, validate, migrate a temp copy -------------------------------------------------

    private sealed class Prepared(string dir, string dbPath, BackupManifest manifest, bool migrated) : IDisposable
    {
        public string DbPath { get; } = dbPath;
        public BackupManifest Manifest { get; } = manifest;
        public bool Migrated { get; } = migrated;

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    private static Prepared Prepare(string backupFile, IReadOnlyList<Migration>? migrations)
    {
        migrations ??= Migrator.Shipped;
        var latest = migrations.Max(m => m.Version);
        var dir = Path.Combine(Path.GetTempPath(), "timesink-rs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            BackupManifest manifest;
            var dbPath = Path.Combine(dir, BackupManifest.DbEntryName);
            try
            {
                using var zip = ZipFile.OpenRead(backupFile);
                var m = zip.GetEntry(BackupManifest.EntryName) ?? throw new BackupException("This is not a TimeSink backup (no manifest).");
                using (var s = new StreamReader(m.Open(), Encoding.UTF8)) manifest = BackupManifest.Parse(s.ReadToEnd());
                var d = zip.GetEntry(BackupManifest.DbEntryName) ?? throw new BackupException("This backup has no database inside.");
                d.ExtractToFile(dbPath);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException && ex is not BackupException)
            {
                throw new BackupException("This file is not a readable TimeSink backup.");
            }

            if (manifest.Format != 1) throw new BackupException("This backup uses an unknown format.");
            if (manifest.SchemaVersion > latest)
                throw new BackupException($"This backup was made by a newer version of TimeSink (schema {manifest.SchemaVersion}, this version understands {latest}). Update TimeSink to restore it.");

            var migrated = false;
            using (var c = Open(dbPath))
            {
                string integrity;
                try { integrity = Scalar(c, "PRAGMA integrity_check") as string ?? ""; }
                catch (SqliteException) { throw new BackupException("The database inside this backup is damaged."); }
                if (integrity != "ok") throw new BackupException("The database inside this backup failed its integrity check.");
                var version = Migrator.CurrentVersion(c);
                if (version > latest) throw new BackupException("This backup was made by a newer version of TimeSink.");
                if (version < latest) { Migrator.Migrate(c, migrations); migrated = true; }
            }
            SqliteConnection.ClearAllPools();
            return new Prepared(dir, dbPath, manifest, migrated);
        }
        catch
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch (IOException) { }
            throw;
        }
    }

    // ---- small SQL helpers ---------------------------------------------------------------------------------

    private static SqliteConnection Open(string path)
    {
        var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
        c.Open();
        return c;
    }

    private static string Quote(string path) => path.Replace("'", "''");

    private static object? Scalar(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    private static long Count(SqliteConnection c, string table) => (long)Scalar(c, $"SELECT COUNT(*) FROM {table}")!;

    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static long ExecCount(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteNonQuery();
    }

    private static List<string> Tables(SqliteConnection c, string schema)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT name FROM {schema}.sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'";
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    private static List<string> Columns(SqliteConnection c, string table)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT name FROM pragma_table_info('{table}', 'main')";
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }
}
