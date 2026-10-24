using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace TimeSink.Core.Backup;

public sealed record CsvExportFiles(string SessionsCsv, string DailyCsv);

/// <summary>Export of all data (implementation.md section 10): sessions and daily totals as CSV, or everything as JSON.</summary>
public static class DataExport
{
    private const string SessionsSql =
        "SELECT a.exe_path, a.display_name, a.state, s.start_utc, COALESCE(s.end_utc, s.last_seen_utc) " +
        "FROM sessions s JOIN apps a ON a.id = s.app_id ORDER BY s.start_utc, a.exe_path";
    private const string DailySql =
        "SELECT a.exe_path, a.display_name, d.day, d.seconds FROM daily_usage d JOIN apps a ON a.id = d.app_id ORDER BY d.day, a.exe_path";

    public static CsvExportFiles ExportCsv(string dbPath, string folder)
    {
        Directory.CreateDirectory(folder);
        var sessions = Path.Combine(folder, "timesink-sessions.csv");
        var daily = Path.Combine(folder, "timesink-daily.csv");
        using var c = Open(dbPath);

        using (var w = new StreamWriter(sessions, false, new UTF8Encoding(false)))
        {
            w.WriteLine("app,start_utc,end_utc,seconds");
            foreach (var s in ReadSessions(c))
                w.WriteLine($"{Csv(s.Name)},{Iso(s.Start)},{Iso(s.End)},{s.End - s.Start}");
        }
        using (var w = new StreamWriter(daily, false, new UTF8Encoding(false)))
        {
            w.WriteLine("app,day,seconds");
            using var cmd = c.CreateCommand();
            cmd.CommandText = DailySql;
            using var r = cmd.ExecuteReader();
            while (r.Read())
                w.WriteLine($"{Csv(r.GetString(1))},{r.GetString(2)},{r.GetInt64(3)}");
        }
        return new CsvExportFiles(sessions, daily);
    }

    public static void ExportJson(string dbPath, string file)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(file));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using var c = Open(dbPath);
        using var fs = File.Create(file);
        using var w = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = true });
        w.WriteStartObject();

        w.WriteStartArray("apps");
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT exe_path, display_name, category, state, added_utc, retired_utc FROM apps ORDER BY id";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                w.WriteStartObject();
                w.WriteString("exePath", r.GetString(0));
                w.WriteString("name", r.GetString(1));
                w.WriteString("category", r.GetString(2));
                w.WriteString("state", r.GetString(3));
                w.WriteString("addedUtc", Iso(r.GetInt64(4)));
                if (r.IsDBNull(5)) w.WriteNull("retiredUtc"); else w.WriteString("retiredUtc", Iso(r.GetInt64(5)));
                w.WriteEndObject();
            }
        }
        w.WriteEndArray();

        w.WriteStartArray("sessions");
        foreach (var s in ReadSessions(c))
        {
            w.WriteStartObject();
            w.WriteString("exePath", s.Exe);
            w.WriteString("app", s.Name);
            w.WriteString("startUtc", Iso(s.Start));
            w.WriteString("endUtc", Iso(s.End));
            w.WriteNumber("seconds", s.End - s.Start);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteStartArray("dailyUsage");
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = DailySql;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                w.WriteStartObject();
                w.WriteString("exePath", r.GetString(0));
                w.WriteString("app", r.GetString(1));
                w.WriteString("day", r.GetString(2));
                w.WriteNumber("seconds", r.GetInt64(3));
                w.WriteEndObject();
            }
        }
        w.WriteEndArray();

        w.WriteEndObject();
    }

    private sealed record SessionRow(string Exe, string Name, long Start, long End);

    private static List<SessionRow> ReadSessions(SqliteConnection c)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = SessionsSql;
        using var r = cmd.ExecuteReader();
        var list = new List<SessionRow>();
        while (r.Read()) list.Add(new SessionRow(r.GetString(0), r.GetString(1), r.GetInt64(3), r.GetInt64(4)));
        return list;
    }

    private static string Iso(long unix) =>
        DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Csv(string v) =>
        v.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;

    private static SqliteConnection Open(string dbPath)
    {
        var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        c.Open();
        using var p = c.CreateCommand();
        p.CommandText = "PRAGMA busy_timeout=5000";
        p.ExecuteNonQuery();
        return c;
    }
}

/// <summary>Delete all history: sessions, daily usage and icons for all apps. The app list and settings stay.</summary>
public static class HistoryDeleter
{
    public const string Confirmation = "DELETE";

    public static void DeleteAll(string dbPath, string typedConfirmation)
    {
        if (!string.Equals(typedConfirmation, Confirmation, StringComparison.Ordinal))
            throw new ArgumentException($"Type {Confirmation} to confirm.", nameof(typedConfirmation));

        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
        c.Open();
        using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA busy_timeout=5000";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "DELETE FROM sessions; DELETE FROM daily_usage; DELETE FROM icons;";
        cmd.ExecuteNonQuery();
        tx.Commit();
    }
}
