using Microsoft.Data.Sqlite;
using TimeSink.Core.Data;

namespace TimeSink.Tests.Usage;

/// <summary>
/// Hand-computed Week fixture (zone UTC, "now" = Wed 2026-03-11 14:20:00Z, so the Mon-first week starts 2026-03-09).
///
/// Chrome (added 2025-01-01): Mon 03-09 09:00-11:00 (7200); Tue 03-10 23:00 -> Wed 03-11 00:30 (3600 + 1800);
///   Wed 11:00 -> open (12000 at now); Sun 03-08 10:00-10:30 (1800); Wed 03-04 10:00-12:00 (7200); Mon 03-02 10:00-11:00 (3600)
/// Code (added 2025-01-01): Mon 03-09 13:00-14:00 (3600); Wed 13:00-13:30 (1800); Sat 03-07 20:00-21:00 (3600)
/// Old (retired): Tue 03-10 09:00-12:00 (ignored everywhere)
/// Fri 03-06 has no time at all, so the streak is Sat..Wed = 5.
/// </summary>
public sealed class WeekFixture : IDisposable
{
    public static readonly DateTimeOffset Now = U(2026, 3, 11, 14, 20);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "timesink-week-" + Guid.NewGuid().ToString("N"));
    public string DbPath { get; }

    public static DateTimeOffset U(int y, int mo, int d, int h, int mi = 0) => new(y, mo, d, h, mi, 0, TimeSpan.Zero);
    public static long Unix(int y, int mo, int d, int h, int mi = 0) => U(y, mo, d, h, mi).ToUnixTimeSeconds();

    public WeekFixture()
    {
        Directory.CreateDirectory(_dir);
        DbPath = Path.Combine(_dir, "t.db");
        using var db = Db.Open(DbPath);
    }

    public long AddApp(string name, int addedY = 2025, int addedM = 1, int addedD = 1)
    {
        using var db = Db.Open(DbPath);
        return db.InsertApp($@"c:\{name.ToLowerInvariant()}.exe", name, Unix(addedY, addedM, addedD, 0));
    }

    public void Retire(long app)
    {
        using var db = Db.Open(DbPath);
        db.SetAppState(app, "retired", Unix(2026, 3, 1, 0));
    }

    public static WeekFixture Standard(out long chrome, out long code)
    {
        var f = new WeekFixture();
        chrome = f.AddApp("Chrome");
        code = f.AddApp("Code");
        var old = f.AddApp("Old");
        f.Retire(old);
        f.Session(chrome, Unix(2026, 3, 9, 9), Unix(2026, 3, 9, 11));
        f.Session(chrome, Unix(2026, 3, 10, 23), Unix(2026, 3, 11, 0, 30));
        f.Session(chrome, Unix(2026, 3, 11, 11), null, lastSeen: Unix(2026, 3, 11, 14, 19));
        f.Session(chrome, Unix(2026, 3, 8, 10), Unix(2026, 3, 8, 10, 30));
        f.Session(chrome, Unix(2026, 3, 4, 10), Unix(2026, 3, 4, 12));
        f.Session(chrome, Unix(2026, 3, 2, 10), Unix(2026, 3, 2, 11));
        f.Session(code, Unix(2026, 3, 9, 13), Unix(2026, 3, 9, 14));
        f.Session(code, Unix(2026, 3, 11, 13), Unix(2026, 3, 11, 13, 30));
        f.Session(code, Unix(2026, 3, 7, 20), Unix(2026, 3, 7, 21));
        f.Session(old, Unix(2026, 3, 10, 9), Unix(2026, 3, 10, 12));
        return f;
    }

    /// <summary>Raw insert (the Agent is the only real writer of sessions).</summary>
    public void Session(long app, long start, long? end, long? lastSeen = null, string? reason = null)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO sessions(app_id,start_utc,end_utc,last_seen_utc,end_reason) VALUES($a,$s,$e,$l,$r)";
        cmd.Parameters.AddWithValue("$a", app);
        cmd.Parameters.AddWithValue("$s", start);
        cmd.Parameters.AddWithValue("$e", (object?)end ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$l", lastSeen ?? end ?? start);
        cmd.Parameters.AddWithValue("$r", (object?)reason ?? (end is null ? DBNull.Value : "exit"));
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
