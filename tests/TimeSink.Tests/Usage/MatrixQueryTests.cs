using Microsoft.Data.Sqlite;
using TimeSink.Core.Data;
using TimeSink.Core.Usage;

namespace TimeSink.Tests.Usage;

/// <summary>
/// Hand-computed matrix fixture (zone UTC, now = Wed 2026-05-13 12:00Z). Apps are added 2026-02-10, so the columns are
/// Feb, Mar, Apr, May. Hours per app per month:
///   Chrome  Feb 10 (11th 09-19)   Mar 5 (3rd 10-15) + 1 (alias Chrome2, 6th 10-11) = 6   Apr 20 (2nd 08-18, 20th 08-18)   May 3 (open since 09:00)
///   Code    Feb 2 (11th 09-11)    Mar 8 (4th 09-17)     Apr 4 (9th 09-13) + 2 (30th 22:00 -> May 1 02:00) = 6   May 2 + 1 (5th 10-11) = 3
///   Disc    Feb 0                 Mar 1 (5th 20-21)     Apr 0                              May 2 (6th 14-16)
///   Old (retired) 3 h in March: ignored.
/// Row totals: Chrome 39, Code 19, Disc 3. Column totals: Feb 12, Mar 15, Apr 26, May 8. Grand 61.
/// </summary>
public sealed class MatrixQueryTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(U(2026, 5, 13, 12));
    private const long H = 3600;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "timesink-matrix-" + Guid.NewGuid().ToString("N"));
    private readonly string _db;
    private readonly long _chrome, _code, _disc;

    private static long U(int y, int mo, int d, int h) => new DateTimeOffset(y, mo, d, h, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    public MatrixQueryTests()
    {
        Directory.CreateDirectory(_dir);
        _db = Path.Combine(_dir, "x.db");
        using var db = Db.Open(_db);
        var added = U(2026, 2, 10, 0);
        _chrome = db.InsertApp(@"c:\chrome.exe", "Chrome", added);
        _code = db.InsertApp(@"c:\code.exe", "Code", added);
        _disc = db.InsertApp(@"c:\disc.exe", "Disc", added);
        var old = db.InsertApp(@"c:\old.exe", "Old", added);
        db.SetAppState(old, "retired", U(2026, 4, 1, 0));
        var alias = _chrome + 100;
        Exec($"INSERT INTO apps(id, exe_path, display_name, added_utc, canonical_id) VALUES({alias}, 'c:\\\\chrome2.exe', 'Chrome2', {added}, {_chrome})");

        S(_chrome, U(2026, 2, 11, 9), U(2026, 2, 11, 19));
        S(_code, U(2026, 2, 11, 9), U(2026, 2, 11, 11));
        S(_chrome, U(2026, 3, 3, 10), U(2026, 3, 3, 15));
        S(alias, U(2026, 3, 6, 10), U(2026, 3, 6, 11));
        S(_code, U(2026, 3, 4, 9), U(2026, 3, 4, 17));
        S(_disc, U(2026, 3, 5, 20), U(2026, 3, 5, 21));
        S(old, U(2026, 3, 8, 8), U(2026, 3, 8, 11));
        S(_chrome, U(2026, 4, 2, 8), U(2026, 4, 2, 18));
        S(_chrome, U(2026, 4, 20, 8), U(2026, 4, 20, 18));
        S(_code, U(2026, 4, 9, 9), U(2026, 4, 9, 13));
        S(_code, U(2026, 4, 30, 22), U(2026, 5, 1, 2));
        S(_code, U(2026, 5, 5, 10), U(2026, 5, 5, 11));
        S(_disc, U(2026, 5, 6, 14), U(2026, 5, 6, 16));
        S(_chrome, U(2026, 5, 13, 9), null);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _db, Pooling = false }.ToString());
        c.Open();
        return c;
    }

    private void Exec(string sql)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private void S(long app, long start, long? end) =>
        Exec($"INSERT INTO sessions(app_id,start_utc,end_utc,last_seen_utc,end_reason) VALUES({app},{start},{(end is null ? "NULL" : end.ToString())},{end ?? start},{(end is null ? "NULL" : "'exit'")})");

    private MatrixResult Run(IReadOnlyCollection<long>? running = null, TimeZoneInfo? zone = null) =>
        MatrixQuery.Run(_db, Now, zone ?? TimeZoneInfo.Utc, running);

    private static MatrixRow Row(MatrixResult r, string name) => r.Rows.Single(x => x.Name == name);

    [Fact]
    public void Columns_run_from_the_month_tracking_began_to_the_current_month()
    {
        var r = Run();
        r.FirstMonth.Should().Be(2);
        r.Months.Select(m => m.Month).Should().Equal(2, 3, 4, 5);
    }

    [Fact]
    public void Hours_per_app_per_month_match_the_hand_computed_fixture()
    {
        var r = Run();
        r.Rows.Select(x => x.Name).Should().Equal("Chrome", "Code", "Disc"); // by total, retired Old excluded
        Row(r, "Chrome").Cells.Select(c => c.Seconds).Should().Equal(10 * H, 6 * H, 20 * H, 3 * H); // alias merged in March
        Row(r, "Code").Cells.Select(c => c.Seconds).Should().Equal(2 * H, 8 * H, 6 * H, 3 * H);     // Apr 30 -> May 1 split
        Row(r, "Disc").Cells.Select(c => c.Seconds).Should().Equal(0, 1 * H, 0, 2 * H);
    }

    [Fact]
    public void Row_and_column_totals_are_correct()
    {
        var r = Run();
        Row(r, "Chrome").TotalSeconds.Should().Be(39 * H);
        Row(r, "Code").TotalSeconds.Should().Be(19 * H);
        Row(r, "Disc").TotalSeconds.Should().Be(3 * H);
        r.Months.Select(m => m.TotalSeconds).Should().Equal(10 * H, 15 * H, 26 * H, 8 * H); // PC uptime: overlapping apps count once
        r.GrandSeconds.Should().Be(59 * H);
    }

    [Fact]
    public void Colour_level_is_relative_to_each_apps_own_best_month()
    {
        var r = Run();
        // Chrome best 20 h: 10 h = 50%, 6 h = 30% (boundary, level 1), 20 h = 100%, 3 h = 15%.
        Row(r, "Chrome").Cells.Select(c => c.Level).Should().Equal(1, 1, 4, 0);
        // Code best 8 h: 2 h = 25%, 8 h, 6 h = 75% (boundary, level 3), 3 h = 37.5%.
        Row(r, "Code").Cells.Select(c => c.Level).Should().Equal(0, 4, 3, 1);
        // Disc best 2 h: 0 -> 0, 1 h = 50%, 0, 2 h.
        Row(r, "Disc").Cells.Select(c => c.Level).Should().Equal(0, 1, 0, 4);
    }

    [Theory]
    [InlineData(0, 100, 0)]
    [InlineData(29, 100, 0)]
    [InlineData(30, 100, 1)]
    [InlineData(54, 100, 1)]
    [InlineData(55, 100, 2)]
    [InlineData(74, 100, 2)]
    [InlineData(75, 100, 3)]
    [InlineData(89, 100, 3)]
    [InlineData(90, 100, 4)]
    [InlineData(100, 100, 4)]
    [InlineData(5, 0, 0)]
    public void Level_thresholds(long seconds, long best, int level) => MatrixQuery.Level(seconds, best).Should().Be(level);

    [Fact]
    public void Row_tap_summary_has_peak_share_and_trend()
    {
        var r = Run();
        var chrome = Row(r, "Chrome");
        chrome.PeakMonth.Should().Be(4);
        chrome.PeakSeconds.Should().Be(20 * H);
        chrome.SharePercent.Should().Be(66);                    // 39 / 59 uptime
        chrome.TrendPercent.Should().BeApproximately(190, 1e-9); // Mar-May 29 h vs Feb 10 h
        Row(r, "Code").TrendPercent.Should().BeApproximately(750, 1e-9); // 17 h vs 2 h
        Row(r, "Disc").TrendPercent.Should().BeNull();               // earlier months have no time
    }

    [Fact]
    public void Month_tap_summary_lists_the_top_apps_with_time()
    {
        var r = Run();
        r.Months[2].TopApps.Select(a => (a.Name, a.Seconds)).Should().Equal(("Chrome", 20 * H), ("Code", 6 * H));
        r.Months[3].TopApps.Select(a => a.Name).Should().Equal("Chrome", "Code", "Disc");
    }

    [Fact]
    public void An_open_session_of_a_stopped_app_counts_only_to_its_heartbeat()
    {
        // Chrome's open session started 09:00 with heartbeat = start (fixture): not running -> 0 h in May.
        Row(Run(running: []), "Chrome").Cells[3].Seconds.Should().Be(0);
        Row(Run(running: [_chrome]), "Chrome").Cells[3].Seconds.Should().Be(3 * H);
    }

    [Fact]
    public void Nothing_tracked_gives_an_empty_matrix()
    {
        var dir = Path.Combine(_dir, "empty");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "e.db");
        using (Db.Open(path)) { }
        var r = MatrixQuery.Run(path, Now, TimeZoneInfo.Utc);
        r.Rows.Should().BeEmpty();
        r.Months.Should().BeEmpty();
        r.GrandSeconds.Should().Be(0);
    }

    [Fact]
    public void Months_follow_the_local_zone()
    {
        // At UTC+10 the Apr 30 22:00Z-May 2:00Z session is May 1 08:00 - 12:00 local: all 4 h in May (Apr 4 h, May 4 + 1 h).
        var plus10 = TimeZoneInfo.CreateCustomTimeZone("t+10", TimeSpan.FromHours(10), "t", "t");
        Row(Run(zone: plus10), "Code").Cells.Select(c => c.Seconds).Should().Equal(2 * H, 8 * H, 4 * H, 5 * H);
    }
}
