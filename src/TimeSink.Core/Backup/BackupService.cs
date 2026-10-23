using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TimeSink.Core.Data;

namespace TimeSink.Core.Backup;

/// <summary>Raised for any backup file that cannot be restored; the message is fit to show the user.</summary>
public sealed class BackupException(string message) : Exception(message);

/// <summary>The <c>manifest.json</c> inside a <c>.timesink</c> archive (implementation.md section 10).</summary>
public sealed record BackupManifest(int Format, int SchemaVersion, string AppVersion, DateTimeOffset CreatedUtc)
{
    public const string EntryName = "manifest.json";
    public const string DbEntryName = "timesink.db";

    public string ToJson()
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteNumber("format", Format);
            w.WriteNumber("schemaVersion", SchemaVersion);
            w.WriteString("appVersion", AppVersion);
            w.WriteString("createdUtc", CreatedUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture));
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    public static BackupManifest Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            return new BackupManifest(
                r.GetProperty("format").GetInt32(),
                r.GetProperty("schemaVersion").GetInt32(),
                r.TryGetProperty("appVersion", out var v) ? v.GetString() ?? "" : "",
                DateTimeOffset.Parse(r.GetProperty("createdUtc").GetString()!, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new BackupException("This is not a TimeSink backup (the manifest is unreadable).");
        }
    }
}

/// <summary>Creates <c>.timesink</c> backups (zip of manifest.json + a consistent snapshot of the database).</summary>
public static class BackupService
{
    public const string Extension = ".timesink";

    /// <summary>
    /// Snapshots <paramref name="dbPath"/> with <c>VACUUM INTO</c> (consistent even while the Agent writes in WAL mode)
    /// and writes <paramref name="destFile"/>. Safe at any time.
    /// </summary>
    public static void Create(string dbPath, string destFile, DateTimeOffset now, string appVersion = "0.0.0")
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(destFile))!;
        Directory.CreateDirectory(dir);
        var tmpDir = Path.Combine(Path.GetTempPath(), "timesink-bk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var partial = destFile + ".partial";
        try
        {
            var snapshot = Path.Combine(tmpDir, BackupManifest.DbEntryName);
            int version;
            using (var c = new SqliteConnection(new SqliteConnectionStringBuilder
                   { DataSource = dbPath, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString()))
            {
                c.Open();
                using (var busy = c.CreateCommand()) { busy.CommandText = "PRAGMA busy_timeout=5000"; busy.ExecuteNonQuery(); }
                version = Migrator.CurrentVersion(c);
                using var cmd = c.CreateCommand();
                cmd.CommandText = "VACUUM INTO $f";
                cmd.Parameters.AddWithValue("$f", snapshot);
                cmd.ExecuteNonQuery();
            }

            if (File.Exists(partial)) File.Delete(partial);
            using (var zip = ZipFile.Open(partial, ZipArchiveMode.Create))
            {
                var m = zip.CreateEntry(BackupManifest.EntryName);
                using (var s = new StreamWriter(m.Open(), new UTF8Encoding(false)))
                    s.Write(new BackupManifest(1, version, appVersion, now).ToJson());
                zip.CreateEntryFromFile(snapshot, BackupManifest.DbEntryName, CompressionLevel.Optimal);
            }
            File.Move(partial, destFile, overwrite: true);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { if (File.Exists(partial)) File.Delete(partial); } catch (IOException) { }
            try { Directory.Delete(tmpDir, true); } catch (IOException) { }
        }
    }
}
