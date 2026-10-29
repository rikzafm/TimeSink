using Microsoft.Data.Sqlite;
using TimeSink.Core.Data;
using TimeSink.Core.Usage;

namespace TimeSink.Tests.Usage;

/// <summary>Decision 148: the Week day panel agrees with the Week column and uses the clicked day's own week.</summary>
public sealed class DayDetailQueryTests : IDisposable
{
    private readonly TodayFixture _f = new();
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static DateTimeOffset Now => TodayFixture.Now; // Wed 2026-03-11 14:20Z
    private static readonly DateOnly Today = new(2026, 3, 11);

    public void Dispose() => _f.Dispose();

    [Fact]
    public void The_live_day_total_is_the_week_column_with_the_sign_in_time()
    {
        var dir = Path.Combine(Path.GetTempPath(), "timesink-daydetail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var db = Path.Combine(dir, "t.db");
        try
        {
            using (var d = Db.Open(db))
            {
                var app = d.InsertApp(@"c:\chrome.exe", "Chrome", TodayFixture.U(2026, 3, 1, 0));
                using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db, Pooling = false }.ToString());
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "INSERT INTO sessions(app_id,start_utc,end_utc,last_seen_utc,end_reason) VALUES($a,$s,$e,$e,'exit')";
                cmd.Parameters.AddWithValue("$a", app);
                cmd.Parameters.AddWithValue("$s", TodayFixture.U(2026, 3, 11, 10));
                cmd.Parameters.AddWithValue("$e", TodayFixture.U(2026, 3, 11, 11));
                cmd.ExecuteNonQuery();
            }
            var signIn = TodayFixture.U(2026, 3, 11, 7);

            var column = WeekQuery.Run(db, Now, Utc, WeekStart.Mon, [], signIn).Days.Single(d => d.IsToday);
            var panel = DayDetailQuery.Run(db, Today, Now, Utc, WeekStart.Mon, [], signIn);

            column.Seconds.Should().Be(TodayFixture.U(2026, 3, 11, 14, 20) - signIn); // 07:00 to now
            panel.TotalSeconds.Should().Be(column.Seconds);
            // Without the live context the day starts at its first tracked session: a different number.
            TodayQuery.Run(db, Now, Utc, null, Today).UptimeSeconds.Should().NotBe(column.Seconds);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void A_day_of_an_earlier_week_compares_with_that_weeks_average()
    {
        var wed4 = new DateOnly(2026, 3, 4); // the week of Mon 2026-03-02: only this day has time (Chrome 10:00-12:00)
        var panel = DayDetailQuery.Run(_f.DbPath, wed4, Now, Utc, WeekStart.Mon, [], null);

        var lastWeek = WeekQuery.Run(_f.DbPath, Now, Utc, WeekStart.Mon, [], null, weeksBack: 1);
        var thisWeek = WeekQuery.Run(_f.DbPath, Now, Utc, WeekStart.Mon, [], null);
        panel.TotalSeconds.Should().Be(7200);
        panel.WeekAverageSeconds.Should().Be(lastWeek.AverageSeconds).And.Be(7200);
        panel.WeekAverageSeconds.Should().NotBe(thisWeek.AverageSeconds);
    }

    [Fact]
    public void The_panel_lists_the_days_apps_and_the_span_of_activity()
    {
        var tue10 = new DateOnly(2026, 3, 10);
        var panel = DayDetailQuery.Run(_f.DbPath, tue10, Now, Utc, WeekStart.Mon, [], null);

        panel.Apps.Select(a => a.Name).Should().Contain("Chrome");
        panel.FirstStartUtc.Should().Be(TodayFixture.U(2026, 3, 10, 8));
        panel.LastEndUtc.Should().Be(TodayFixture.U(2026, 3, 11, 0)); // Chrome 23:00 runs on past midnight, clipped to the day
    }

    [Fact]
    public void The_week_average_counts_only_days_with_time()
    {
        var week = WeekQuery.Run(_f.DbPath, Now, Utc, WeekStart.Mon, [], null);
        var withTime = week.Days.Where(d => !d.IsFuture && d.Seconds > 0).ToList();
        week.AverageSeconds.Should().BeApproximately(withTime.Sum(d => d.Seconds) / (double)withTime.Count, 1e-9);
    }
}
