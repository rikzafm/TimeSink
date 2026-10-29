using Microsoft.Data.Sqlite;
using TimeSink.Core.Data;
using TimeSink.Core.Usage;
using static TimeSink.Tests.Usage.TodayFixture;

namespace TimeSink.Tests.Usage;

/// <summary>
/// Decision 146: Today, Month, Matrix and Wrapped cut a Session through the same <c>UsageDays</c> module, so on a DST
/// day (America/New_York, 2026-03-08 = 23 h, 2026-11-01 = 25 h) they agree on the day's seconds and on its hours.
/// </summary>
public sealed class UsageDstAgreementTests : IDisposable
{
    private static readonly TimeZoneInfo Ny = FindNewYork();
    private static readonly long[] None = [];
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "timesink-dst-agree-" + Guid.NewGuid().ToString("N"));
    private readonly string _db;
    private readonly long _app;

    public UsageDstAgreementTests()
    {
        Directory.CreateDirectory(_dir);
        _db = Path.Combine(_dir, "t.db");
        using var db = Db.Open(_db);
        _app = db.InsertApp(@"c:\chrome.exe", "Chrome", U(2026, 1, 1, 0));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static TimeZoneInfo FindNewYork()
    {
        foreach (var id in new[] { "America/New_York", "Eastern Standard Time" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { }
        throw new InvalidOperationException("no New York time zone on this machine");
    }

    private void Session(DateTimeOffset start, DateTimeOffset end)
    {
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _db, Pooling = false }.ToString());
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO sessions(app_id,start_utc,end_utc,last_seen_utc,end_reason) VALUES($a,$s,$e,$e,'exit')";
        cmd.Parameters.AddWithValue("$a", _app);
        cmd.Parameters.AddWithValue("$s", start.ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("$e", end.ToUnixTimeSeconds());
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void Fall_back_day_agrees_across_Today_Month_Matrix_and_Wrapped()
    {
        // 01:30 EDT -> 02:30 EST (05:30Z-07:30Z): 1:30-2:00 EDT (1800) + 1:00-2:00 EST (3600) in column 1, 2:00-2:30 EST (1800) in column 2.
        Session(Utc(2026, 11, 1, 5, 30), Utc(2026, 11, 1, 7, 30));
        var now = Utc(2026, 11, 2, 12);
        var nov1 = new DateOnly(2026, 11, 1);

        var today = Q.Today(_db, now, Ny, None, nov1);
        today.Hours[1].AppSeconds.Should().Be(5400);
        today.Hours[2].AppSeconds.Should().Be(1800);

        var month = MonthQuery.Run(_db, now, Ny, 2026, 11, DayOfWeek.Monday, None);
        month.Days[0].Apps.Sum(a => a.Seconds).Should().Be(7200);

        var matrix = MatrixQuery.Run(_db, now, Ny, None);
        matrix.Rows.Single().Cells[10].Seconds.Should().Be(7200);

        var wrapped = WrappedQuery.Run(_db, now, Ny, None);
        wrapped.AppSeconds.Should().Be(7200);
        wrapped.PeakHour.Should().Be(1);
        wrapped.PeakHourSeconds.Should().Be(5400);
        wrapped.BeforeNoonPercent.Should().Be(100);
    }

    [Fact]
    public void Spring_forward_day_agrees_across_Today_Month_and_Wrapped()
    {
        // 01:30 EST -> 04:30 EDT (06:30Z-08:30Z): 1800 in column 1, nothing in 2, 3600 in column 3, 1800 in column 4.
        Session(Utc(2026, 3, 8, 6, 30), Utc(2026, 3, 8, 8, 30));
        var now = Utc(2026, 3, 9, 12);
        var mar8 = new DateOnly(2026, 3, 8);

        var today = Q.Today(_db, now, Ny, None, mar8);
        today.Hours.Select(h => h.AppSeconds).Take(5).Should().Equal(0, 1800, 0, 3600, 1800);

        var month = MonthQuery.Run(_db, now, Ny, 2026, 3, DayOfWeek.Monday, None);
        month.Days[7].Apps.Sum(a => a.Seconds).Should().Be(7200);

        var wrapped = WrappedQuery.Run(_db, now, Ny, None);
        wrapped.AppSeconds.Should().Be(7200);
        wrapped.PeakHour.Should().Be(3);
        wrapped.PeakHourSeconds.Should().Be(3600);
    }
}
