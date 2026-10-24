using Microsoft.Data.Sqlite;
using TimeSink.Core.Data;

namespace TimeSink.Tests.Usage;

/// <summary>
/// Hand-computed Year fixture (zone UTC, "now" = Sun 2026-05-10 12:00:00Z, day 10 of 31).
///
/// Chrome added 2026-02-15, Code added 2026-03-05, Old (retired, ignored everywhere).
/// Hours per month (Chrome / Code):
///   Feb  Chrome 6 + 4                                    = 10
///   Mar  Chrome 12 + 8, Code 10                          = 30
///   Apr  Chrome 14 + 14 + 2 (Apr 30 22:00 -> May 1 02:00), Code 6 = 36   (Old 5 h excluded)
///   May  Chrome 2 (the carry-over) + 10, Code 8          = 20 so far
/// </summary>
public sealed class YearFixture : IDisposable
{
    public static readonly DateTimeOffset Now = new(2026, 5, 10, 12, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "timesink-year-" + Guid.NewGuid().ToString("N"));
    public string DbPath { get; }
    public long Chrome { get; }
    public long Code { get; }

    public static long U(int mo, int d, int h) => new DateTimeOffset(2026, mo, d, h, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    public YearFixture(bool seed = true)
    {
        Directory.CreateDirectory(_dir);
        DbPath = Path.Combine(_dir, "y.db");
        using var db = Db.Open(DbPath);
        if (!seed) return;
        Chrome = db.InsertApp(@"c:\chrome.exe", "Chrome", U(2, 15, 0));
        Code = db.InsertApp(@"c:\code.exe", "Code", U(3, 5, 0));
        var old = db.InsertApp(@"c:\old.exe", "Old", U(2, 1, 0));
        db.SetAppState(old, "retired", U(4, 20, 0));

        Session(Chrome, U(2, 3, 8), U(2, 3, 14));
        Session(Chrome, U(2, 20, 10), U(2, 20, 14));
        Session(Chrome, U(3, 5, 0), U(3, 5, 12));
        Session(Chrome, U(3, 20, 0), U(3, 20, 8));
        Session(Code, U(3, 6, 0), U(3, 6, 10));
        Session(Chrome, U(4, 2, 0), U(4, 2, 14));
        Session(Chrome, U(4, 15, 0), U(4, 15, 14));
        Session(Chrome, U(4, 30, 22), U(5, 1, 2));
        Session(Code, U(4, 10, 0), U(4, 10, 6));
        Session(old, U(4, 3, 0), U(4, 3, 5));
        Session(Chrome, U(5, 5, 8), U(5, 5, 18));
        Session(Code, U(5, 6, 0), U(5, 6, 8));
    }

    /// <summary>Adds a tracked app to an unseeded fixture and returns its id.</summary>
    public long AddApp(string exe, string name, long addedUtc)
    {
        using var db = Db.Open(DbPath);
        return db.InsertApp(exe, name, addedUtc);
    }

    public void Session(long app, long start, long end)
    {
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbPath, Pooling = false }.ToString());
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO sessions(app_id,start_utc,end_utc,last_seen_utc,end_reason) VALUES($a,$s,$e,$e,'exit')";
        cmd.Parameters.AddWithValue("$a", app);
        cmd.Parameters.AddWithValue("$s", start);
        cmd.Parameters.AddWithValue("$e", end);
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }
}
