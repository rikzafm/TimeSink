using Microsoft.Data.Sqlite;
using TimeSink.Core.Data;

namespace TimeSink.Tests.Usage;

/// <summary>
/// Hand-computed Today fixture (zone UTC, "now" = Wed 2026-03-11 14:20:00Z).
///
/// Apps: Chrome (added 2026-02-20), Code (added 2026-03-08), Old (retired, ignored).
///
/// Wed 2026-03-11 (today):
///   Chrome  Tue 23:00 -> 00:30 (exit; 1800 s today), 09:00-09:30 (sleep), 11:00 -> open (12000 s at now)
///   Code    08:30-09:30 (sleep, 3600 s), 13:00-13:30 (exit, 1800 s)
///   Old     09:00-11:00 (retired: excluded everywhere)
/// Tue 2026-03-10: Chrome 08:00-10:00, Code 14:00-16:00 (by 14:20: 1200 s)
/// Wed 2026-03-04: Chrome 10:00-12:00;  Wed 2026-02-25: Chrome 10:00-11:00
/// </summary>
public sealed class TodayFixture : IDisposable
{
    public static readonly DateTimeOffset Now = Utc(2026, 3, 11, 14, 20);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "timesink-today-" + Guid.NewGuid().ToString("N"));
    public string DbPath { get; }
    public long Chrome { get; }
    public long Code { get; }
    public long Old { get; }

    public static DateTimeOffset Utc(int y, int mo, int d, int h, int mi = 0) => new(y, mo, d, h, mi, 0, TimeSpan.Zero);
    public static long U(int y, int mo, int d, int h, int mi = 0) => Utc(y, mo, d, h, mi).ToUnixTimeSeconds();

    public TodayFixture()
    {
        Directory.CreateDirectory(_dir);
        DbPath = Path.Combine(_dir, "t.db");
        using var db = Db.Open(DbPath);
        Chrome = db.InsertApp(@"c:\chrome.exe", "Chrome", U(2026, 2, 20, 0));
        Code = db.InsertApp(@"c:\code.exe", "Code", U(2026, 3, 8, 0));
        Old = db.InsertApp(@"c:\old.exe", "Old", U(2026, 2, 20, 0));
        db.SetAppState(Old, "retired", U(2026, 3, 1, 0));

        Session(Chrome, U(2026, 3, 10, 23), U(2026, 3, 11, 0, 30), "exit");
        Session(Chrome, U(2026, 3, 11, 9), U(2026, 3, 11, 9, 30), "sleep");
        Session(Chrome, U(2026, 3, 11, 11), null, null, lastSeen: U(2026, 3, 11, 14, 19));
        Session(Code, U(2026, 3, 11, 8, 30), U(2026, 3, 11, 9, 30), "sleep");
        Session(Code, U(2026, 3, 11, 13), U(2026, 3, 11, 13, 30), "exit");
        Session(Old, U(2026, 3, 11, 9), U(2026, 3, 11, 11), "exit");
        Session(Chrome, U(2026, 3, 10, 8), U(2026, 3, 10, 10), "exit");
        Session(Code, U(2026, 3, 10, 14), U(2026, 3, 10, 16), "exit");
        Session(Chrome, U(2026, 3, 4, 10), U(2026, 3, 4, 12), "exit");
        Session(Chrome, U(2026, 2, 25, 10), U(2026, 2, 25, 11), "exit");
    }

    /// <summary>Raw insert (the Agent is the only real writer of sessions).</summary>
    public void Session(long app, long start, long? end, string? reason, long? lastSeen = null)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO sessions(app_id,start_utc,end_utc,last_seen_utc,end_reason) VALUES($a,$s,$e,$l,$r)";
        cmd.Parameters.AddWithValue("$a", app);
        cmd.Parameters.AddWithValue("$s", start);
        cmd.Parameters.AddWithValue("$e", (object?)end ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$l", lastSeen ?? end ?? start);
        cmd.Parameters.AddWithValue("$r", (object?)reason ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void Exec(string sql)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbPath, Pooling = false }.ToString());
        c.Open();
        return c;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }
}
