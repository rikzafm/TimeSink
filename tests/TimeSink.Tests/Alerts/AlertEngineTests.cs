using Microsoft.Data.Sqlite;
using TimeSink.Core.Alerts;
using TimeSink.Core.Data;

namespace TimeSink.Tests.Alerts;

public sealed class AlertEngineTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "timesink-alerts-" + Guid.NewGuid().ToString("N"));
    private readonly string _path;
    private readonly Db _db;
    private readonly long _chrome, _code;
    private readonly AlertEngine _engine;

    // Wed 2026-03-11 14:00 UTC; the zone is UTC so local = UTC.
    private static DateTimeOffset At(int d, int h, int mi = 0) => new(2026, 3, d, h, mi, 0, TimeSpan.Zero);

    public AlertEngineTests()
    {
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "t.db");
        _db = Db.Open(_path);
        _chrome = _db.InsertApp(@"c:\chrome.exe", "Chrome", 0);
        _code = _db.InsertApp(@"c:\code.exe", "Code", 0);
        _engine = new AlertEngine(_db, TimeZoneInfo.Utc);
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private void Sql(string sql)
    {
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ToString());
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private void Usage(long app, string day, long seconds) =>
        Sql($"INSERT INTO daily_usage(app_id, day, seconds) VALUES({app}, '{day}', {seconds}) ON CONFLICT(app_id, day) DO UPDATE SET seconds = {seconds}");

    private void Goal(long? app, string kind, long seconds) =>
        Sql($"INSERT OR REPLACE INTO goals(app_id, kind, seconds_per_day) VALUES({(app is null ? "NULL" : app.ToString())}, '{kind}', {seconds})");

    // ---- goals and limits ----

    [Fact]
    public void Nothing_is_due_without_goals()
    {
        Usage(_chrome, "2026-03-11", 99999);
        _engine.Check(At(11, 14)).Should().BeEmpty();
    }

    [Fact]
    public void A_limit_notifies_once_when_the_days_time_reaches_it()
    {
        Goal(_chrome, "limit", 2 * 3600);
        Usage(_chrome, "2026-03-11", 2 * 3600 - 1);
        _engine.Check(At(11, 14)).Should().BeEmpty();

        Usage(_chrome, "2026-03-11", 2 * 3600);
        var n = _engine.Check(At(11, 14, 1)).Should().ContainSingle().Subject;
        n.Kind.Should().Be(AlertKind.Limit);
        n.Body.Should().Contain("Chrome").And.Contain("2h 00m");

        _engine.Check(At(11, 14, 2)).Should().BeEmpty(); // once per day
    }

    [Fact]
    public void The_dedupe_survives_a_restart_and_resets_the_next_day()
    {
        Goal(_chrome, "limit", 3600);
        Usage(_chrome, "2026-03-11", 4000);
        _engine.Check(At(11, 14)).Should().HaveCount(1);

        new AlertEngine(_db, TimeZoneInfo.Utc).Check(At(11, 15)).Should().BeEmpty(); // a new engine, same database

        Usage(_chrome, "2026-03-12", 4000);
        _engine.Check(At(12, 10)).Should().HaveCount(1);
    }

    [Fact]
    public void A_target_says_goal_reached_and_limit_and_target_are_independent()
    {
        Goal(_code, "target", 1800);
        Goal(_code, "limit", 3600);
        Usage(_code, "2026-03-11", 2000);
        var first = _engine.Check(At(11, 14));
        first.Should().ContainSingle().Which.Kind.Should().Be(AlertKind.Goal);

        Usage(_code, "2026-03-11", 3700);
        _engine.Check(At(11, 15)).Should().ContainSingle().Which.Kind.Should().Be(AlertKind.Limit);
    }

    [Fact]
    public void A_goal_without_an_app_watches_the_combined_time_of_tracked_apps_only()
    {
        Goal(null, "limit", 3 * 3600);
        Usage(_chrome, "2026-03-11", 2 * 3600);
        Usage(_code, "2026-03-11", 3599);
        _engine.Check(At(11, 14)).Should().BeEmpty();

        Usage(_code, "2026-03-11", 3600);
        var n = _engine.Check(At(11, 15)).Should().ContainSingle().Subject;
        n.Body.Should().Contain("All apps");
    }

    [Fact]
    public void Merged_apps_count_toward_the_canonical_app_and_retired_apps_are_ignored()
    {
        var alias = _db.InsertApp(@"c:\chrome2.exe", "Chrome Beta", 0);
        Sql($"UPDATE apps SET canonical_id = {_chrome} WHERE id = {alias}");
        Goal(_chrome, "limit", 3600);
        Usage(_chrome, "2026-03-11", 1800);
        Usage(alias, "2026-03-11", 1800);
        _engine.Check(At(11, 14)).Should().ContainSingle();

        Goal(_code, "limit", 60);
        Usage(_code, "2026-03-11", 600);
        _db.SetAppState(_code, "retired", 1);
        _engine.Check(At(11, 15)).Should().BeEmpty();
    }

    [Fact]
    public void A_merged_app_of_a_retired_main_app_is_watched_as_its_own_app()
    {
        var alias = _db.InsertApp(@"c:\chrome2.exe", "Chrome Beta", 0);
        Sql($"UPDATE apps SET canonical_id = {_chrome} WHERE id = {alias}");
        _db.SetAppState(_chrome, "retired", 1);
        Goal(alias, "limit", 3600);
        Usage(alias, "2026-03-11", 3600);

        var n = _engine.Check(At(11, 14)).Should().ContainSingle().Subject;

        n.Body.Should().Contain("Chrome Beta");
    }

    [Fact]
    public void Usage_on_other_days_does_not_count_and_the_switch_turns_alerts_off()
    {
        Goal(_chrome, "limit", 3600);
        Usage(_chrome, "2026-03-10", 99999);
        _engine.Check(At(11, 14)).Should().BeEmpty();

        Usage(_chrome, "2026-03-11", 99999);
        _db.SetSetting("alerts.goals", "false");
        _engine.Check(At(11, 14)).Should().BeEmpty();
        _db.SetSetting("alerts.goals", "true");
        _engine.Check(At(11, 14)).Should().HaveCount(1);
    }

    [Fact]
    public void The_day_is_the_local_day()
    {
        var ny = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Eastern Standard Time" : "America/New_York");
        Goal(_chrome, "limit", 3600);
        Usage(_chrome, "2026-03-11", 7200);
        // 2026-03-12 02:00 UTC is still Mar 11 in New York (22:00 EDT).
        new AlertEngine(_db, ny).Check(At(12, 2)).Should().HaveCount(1);
    }

    // ---- weekly summary ----

    private void LastWeek()
    {
        // Mon 2026-03-02 .. Sun 2026-03-08
        Usage(_chrome, "2026-03-02", 3600);
        Usage(_chrome, "2026-03-04", 7200);
        Usage(_code, "2026-03-04", 1800);
        Usage(_chrome, "2026-03-09", 99999); // the new week: not part of the recap
    }

    [Fact]
    public void The_weekly_summary_is_off_by_default()
    {
        LastWeek();
        _engine.Check(At(9, 9)).Should().BeEmpty();
    }

    [Fact]
    public void The_weekly_summary_recaps_last_week_once_from_eight_on_the_first_day_of_the_week()
    {
        _db.SetSetting("alerts.weekly", "true");
        LastWeek();
        _engine.Check(At(9, 7, 59)).Should().BeEmpty(); // Monday before 08:00
        var n = _engine.Check(At(9, 8)).Should().ContainSingle().Subject;
        n.Kind.Should().Be(AlertKind.Weekly);
        n.Body.Should().Contain("3h 30m").And.Contain("2 apps").And.Contain("Chrome (3h 00m)").And.Contain("Wednesday (2h 30m)");

        _engine.Check(At(9, 12)).Should().BeEmpty();
        _engine.Check(At(12, 9)).Should().BeEmpty(); // same week, later day
        new AlertEngine(_db, TimeZoneInfo.Utc).Check(At(10, 9)).Should().BeEmpty(); // restart
    }

    [Fact]
    public void A_summary_missed_on_the_first_day_arrives_at_the_next_check()
    {
        _db.SetSetting("alerts.weekly", "true");
        LastWeek();
        _engine.Check(At(11, 14)).Should().ContainSingle(); // PC was off Monday and Tuesday
    }

    [Fact]
    public void The_next_week_gets_its_own_summary_and_an_empty_week_says_nothing()
    {
        _db.SetSetting("alerts.weekly", "true");
        LastWeek();
        _engine.Check(At(9, 9)).Should().ContainSingle();
        _engine.Check(At(16, 9)).Should().ContainSingle(); // recap of Mar 9-15 (99999 s on the 9th)
        _engine.Check(At(23, 9)).Should().BeEmpty();       // Mar 16-22: no time
    }

    [Fact]
    public void The_week_start_setting_decides_the_first_day()
    {
        _db.SetSetting("alerts.weekly", "true");
        _db.SetSetting("week.start", "Sun");
        Usage(_chrome, "2026-03-04", 3600); // in the week Sun 03-01 .. Sat 03-07
        _engine.Check(At(7, 9)).Should().BeEmpty(); // Saturday: still inside the week
        var n = _engine.Check(At(8, 9)).Should().ContainSingle().Subject; // Sunday
        n.Body.Should().Contain("1h 00m");
    }

    [Fact]
    public void The_weekly_summary_names_a_merged_app_of_a_retired_main_app_as_its_own_app()
    {
        _db.SetSetting("alerts.weekly", "true");
        var alias = _db.InsertApp(@"c:\chrome2.exe", "Chrome Beta", 0);
        Sql($"UPDATE apps SET canonical_id = {_chrome} WHERE id = {alias}");
        _db.SetAppState(_chrome, "retired", 1);
        Usage(alias, "2026-03-04", 3600);

        var n = _engine.Check(At(9, 9)).Should().ContainSingle().Subject;

        n.Body.Should().Contain("1 app").And.Contain("Chrome Beta (1h 00m)");
    }
}
