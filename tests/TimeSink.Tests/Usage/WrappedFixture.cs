using Microsoft.Data.Sqlite;
using TimeSink.Core.Data;

namespace TimeSink.Tests.Usage;

/// <summary>
/// Hand-computed Wrapped fixture. Zone is a fixed UTC+05:30 (so local days/hours differ from UTC ones), "now" is
/// Wed 2026-06-10 15:30 local. All times below are LOCAL. Tracking began Mar 3 (earliest app added).
///
/// Alpha (64800 s): Mar 3 09-11 | Mar 4 11-13 | Mar 5 23:00 -> Mar 6 01:00 | Apr 10 14:00-16:30 | Apr 11 14-15 |
///                  May 20 10-11 | Jun 10 08:00 -> still open (counts to now = 7.5 h = 27000)
/// Beta  (10800 s): Mar 4 14-15 | Apr 10 14:30-15:30 | Apr 12 09-10
/// Gamma  (1800 s): Apr 12 20:00-20:30
/// Delta   (600 s): Jun 10 07:00-07:10
/// Old (retired, ignored): Mar 20 10-12
///
/// Total 78000 s. Days with time: Mar 3,4,5,6 | Apr 10,11,12 | May 20 | Jun 10 = 9 days; longest streak Mar 3-6 (4).
/// Months: Mar 25200, Apr 21600, May 3600, Jun 27600 (busiest; +667% vs May).
/// Hour 14 = Alpha Apr 10 (3600) + Apr 11 (3600) + Jun 10 (3600) + Beta Mar 4 (3600) + Apr 10 14:30-15:00 (1800) = 16200 (peak).
/// Before noon: 36600 / 78000 = 46.9% -> 47.
/// </summary>
public sealed class WrappedFixture : IDisposable
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("test+5:30", TimeSpan.FromMinutes(330), "t", "t");
    public static readonly DateTimeOffset Now = L(2026, 6, 10, 15, 30);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "timesink-wrapped-" + Guid.NewGuid().ToString("N"));
    public string DbPath { get; }
    public long Alpha { get; }
    public long Beta { get; }
    public long Gamma { get; }
    public long Delta { get; }

    public static DateTimeOffset L(int y, int mo, int d, int h, int mi = 0) =>
        new(new DateTime(y, mo, d, h, mi, 0), TimeSpan.FromMinutes(330));

    private static long U(int y, int mo, int d, int h, int mi = 0) => L(y, mo, d, h, mi).ToUnixTimeSeconds();

    public WrappedFixture()
    {
        Directory.CreateDirectory(_dir);
        DbPath = Path.Combine(_dir, "w.db");
        using var db = Db.Open(DbPath);
        Alpha = db.InsertApp(@"c:\alpha.exe", "Alpha", U(2026, 3, 3, 0));
        Beta = db.InsertApp(@"c:\beta.exe", "Beta", U(2026, 3, 3, 0));
        Gamma = db.InsertApp(@"c:\gamma.exe", "Gamma", U(2026, 3, 3, 0));
        Delta = db.InsertApp(@"c:\delta.exe", "Delta", U(2026, 3, 3, 0));
        var old = db.InsertApp(@"c:\old.exe", "Old", U(2026, 3, 3, 0));
        db.SetAppState(old, "retired", U(2026, 4, 1, 0));

        Session(Alpha, U(2026, 3, 3, 9), U(2026, 3, 3, 11));
        Session(Alpha, U(2026, 3, 4, 11), U(2026, 3, 4, 13));
        Session(Alpha, U(2026, 3, 5, 23), U(2026, 3, 6, 1));
        Session(Alpha, U(2026, 4, 10, 14), U(2026, 4, 10, 16, 30));
        Session(Alpha, U(2026, 4, 11, 14), U(2026, 4, 11, 15));
        Session(Alpha, U(2026, 5, 20, 10), U(2026, 5, 20, 11));
        Session(Alpha, U(2026, 6, 10, 8), null, lastSeen: U(2026, 6, 10, 15, 25));
        Session(Beta, U(2026, 3, 4, 14), U(2026, 3, 4, 15));
        Session(Beta, U(2026, 4, 10, 14, 30), U(2026, 4, 10, 15, 30));
        Session(Beta, U(2026, 4, 12, 9), U(2026, 4, 12, 10));
        Session(Gamma, U(2026, 4, 12, 20), U(2026, 4, 12, 20, 30));
        Session(Delta, U(2026, 6, 10, 7), U(2026, 6, 10, 7, 10));
        Session(old, U(2026, 3, 20, 10), U(2026, 3, 20, 12));
    }

    public void Session(long app, long start, long? end, long? lastSeen = null)
    {
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbPath, Pooling = false }.ToString());
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO sessions(app_id,start_utc,end_utc,last_seen_utc,end_reason) VALUES($a,$s,$e,$l,$r)";
        cmd.Parameters.AddWithValue("$a", app);
        cmd.Parameters.AddWithValue("$s", start);
        cmd.Parameters.AddWithValue("$e", (object?)end ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$l", lastSeen ?? end ?? start);
        cmd.Parameters.AddWithValue("$r", end is null ? DBNull.Value : "exit");
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }
}
