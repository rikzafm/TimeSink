using Microsoft.Data.Sqlite;
using TimeSink.Core.Data;

namespace TimeSink.Tests.Usage;

/// <summary>
/// Hand-computed Month fixture (zone UTC, "now" = Wed 2026-03-11 14:20:00Z, so March has 11 days counted).
/// Apps: Chrome, Code, Disc, Zed ("Zed, "Editor"" to exercise CSV quoting), Mus, Old (retired: ignored).
///
/// March (seconds):
///   Sun 1   Code 12:00-12:30 (1800)
///   Mon 2   Chrome 09-12 (10800), Code 10-12 (7200), Disc 20-21 (3600), Old all day (ignored)      = 21600
///   Tue 3   Chrome 22:00 -> Wed 02:00 (7200 on the 3rd, 7200 on the 4th), Code 09-17 (28800)       = 36000
///   Wed 4   Chrome carry-over (7200)                                                               = 7200
///   Thu 5   Zed 10:00-10:30 (1800)                                                                 = 1800
///   Tue 10  Code 13-18 (18000), Disc 08:00-09:30 (5400)                                            = 23400
///   Wed 11  Chrome 11:00 -> open (12000 at now), Mus 08:00-08:10 (600)                             = 12600
///   Total 104400; per app Code 55800, Chrome 37200, Disc 9000, Zed 1800, Mus 600.
/// February: Chrome 2nd 09-13 (14400), 11th 10-11 (3600), 12th 10-20 (36000), 28th 10-12 (7200);
///           Code 5th 09-11 (7200); Disc 3rd 10-12 (7200).  Feb 1-11 = 32400; whole month = 75600.
/// </summary>
public sealed class MonthFixture : IDisposable
{
    public static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(U(2026, 3, 11, 14, 20));
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "timesink-month-" + Guid.NewGuid().ToString("N"));
    public string DbPath { get; }
    public long Chrome { get; }
    public long Code { get; }
    public long Disc { get; }
    public long Zed { get; }
    public long Mus { get; }

    public const string ZedName = "Zed, \"Editor\"";

    public static long U(int y, int mo, int d, int h, int mi = 0) =>
        new DateTimeOffset(y, mo, d, h, mi, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    public MonthFixture()
    {
        Directory.CreateDirectory(_dir);
        DbPath = Path.Combine(_dir, "m.db");
        using var db = Db.Open(DbPath);
        Chrome = db.InsertApp(@"c:\chrome.exe", "Chrome", U(2026, 1, 1, 0));
        Code = db.InsertApp(@"c:\code.exe", "Code", U(2026, 1, 1, 0));
        Disc = db.InsertApp(@"c:\disc.exe", "Disc", U(2026, 1, 1, 0));
        Zed = db.InsertApp(@"c:\zed.exe", ZedName, U(2026, 1, 1, 0));
        Mus = db.InsertApp(@"c:\mus.exe", "Mus", U(2026, 1, 1, 0));
        var old = db.InsertApp(@"c:\old.exe", "Old", U(2026, 1, 1, 0));
        db.SetAppState(old, "retired", U(2026, 3, 1, 0));

        Session(Code, U(2026, 3, 1, 12), U(2026, 3, 1, 12, 30));
        Session(Chrome, U(2026, 3, 2, 9), U(2026, 3, 2, 12));
        Session(Code, U(2026, 3, 2, 10), U(2026, 3, 2, 12));
        Session(Disc, U(2026, 3, 2, 20), U(2026, 3, 2, 21));
        Session(old, U(2026, 3, 2, 0), U(2026, 3, 2, 23));
        Session(Chrome, U(2026, 3, 3, 22), U(2026, 3, 4, 2));
        Session(Code, U(2026, 3, 3, 9), U(2026, 3, 3, 17));
        Session(Zed, U(2026, 3, 5, 10), U(2026, 3, 5, 10, 30));
        Session(Code, U(2026, 3, 10, 13), U(2026, 3, 10, 18));
        Session(Disc, U(2026, 3, 10, 8), U(2026, 3, 10, 9, 30));
        Session(Chrome, U(2026, 3, 11, 11), null, lastSeen: U(2026, 3, 11, 14, 19));
        Session(Mus, U(2026, 3, 11, 8), U(2026, 3, 11, 8, 10));

        Session(Chrome, U(2026, 2, 2, 9), U(2026, 2, 2, 13));
        Session(Chrome, U(2026, 2, 11, 10), U(2026, 2, 11, 11));
        Session(Chrome, U(2026, 2, 12, 10), U(2026, 2, 12, 20));
        Session(Chrome, U(2026, 2, 28, 10), U(2026, 2, 28, 12));
        Session(Code, U(2026, 2, 5, 9), U(2026, 2, 5, 11));
        Session(Disc, U(2026, 2, 3, 10), U(2026, 2, 3, 12));
    }

    public void Session(long app, long start, long? end, long? lastSeen = null)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO sessions(app_id,start_utc,end_utc,last_seen_utc,end_reason) VALUES($a,$s,$e,$l,$r)";
        cmd.Parameters.AddWithValue("$a", app);
        cmd.Parameters.AddWithValue("$s", start);
        cmd.Parameters.AddWithValue("$e", (object?)end ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$l", lastSeen ?? end ?? start);
        cmd.Parameters.AddWithValue("$r", end is null ? DBNull.Value : "exit");
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
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }
}
