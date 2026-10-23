using System.Text.Json;
using TimeSink.Core.Backup;

namespace TimeSink.Tests.Backup;

public sealed class ExportAndDeleteTests : IDisposable
{
    private readonly BackupTestRig _rig = new();
    public void Dispose() => _rig.Dispose();

    // 2023-11-14T22:13:20Z
    private const long T0 = 1_700_000_000;

    private string Seed()
    {
        var db = _rig.NewDb("live.db", (@"c:\a\one.exe", T0, T0 + 3600), (@"c:\a\one.exe", T0 + 7200, null), (@"c:\b\two, the app.exe", T0 + 100, T0 + 160));
        _rig.Exec(db, "UPDATE apps SET display_name = 'Two, \"the\" app', state = 'retired' WHERE exe_path LIKE '%two%'");
        _rig.Exec(db, "INSERT INTO daily_usage SELECT id, '2023-11-14', 3660 FROM apps WHERE exe_path LIKE '%one%'");
        _rig.Exec(db, "INSERT INTO icons SELECT id, x'89504E47', 'h' FROM apps");
        return db;
    }

    [Fact]
    public void Csv_export_contains_every_session_and_daily_total_including_retired_apps()
    {
        var db = Seed();

        var files = DataExport.ExportCsv(db, _rig.P("out"));

        File.ReadAllLines(files.SessionsCsv).Should().Equal(
            "app,start_utc,end_utc,seconds",
            "one,2023-11-14T22:13:20Z,2023-11-14T23:13:20Z,3600",
            "\"Two, \"\"the\"\" app\",2023-11-14T22:15:00Z,2023-11-14T22:16:00Z,60",
            "one,2023-11-15T00:13:20Z,2023-11-15T00:14:20Z,60"); // open session counts up to last_seen
        File.ReadAllLines(files.DailyCsv).Should().Equal("app,day,seconds", "one,2023-11-14,3660");
    }

    [Fact]
    public void Json_export_has_apps_sessions_and_daily_usage()
    {
        var db = Seed();
        var file = _rig.P("all.json");

        DataExport.ExportJson(db, file);

        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        var root = doc.RootElement;
        root.GetProperty("apps").GetArrayLength().Should().Be(2);
        var sessions = root.GetProperty("sessions");
        sessions.GetArrayLength().Should().Be(3);
        sessions[0].GetProperty("exePath").GetString().Should().Be(@"c:\a\one.exe");
        sessions[0].GetProperty("seconds").GetInt64().Should().Be(3600);
        root.GetProperty("dailyUsage")[0].GetProperty("day").GetString().Should().Be("2023-11-14");
    }

    [Fact]
    public void Delete_all_history_needs_the_exact_word_DELETE()
    {
        var db = Seed();
        var before = _rig.Fingerprint(db);

        foreach (var wrong in new[] { "", "delete", "DELETE ", "yes" })
            ((Action)(() => HistoryDeleter.DeleteAll(db, wrong))).Should().Throw<ArgumentException>();

        _rig.Fingerprint(db).Should().Be(before);
    }

    [Fact]
    public void Delete_all_history_removes_sessions_usage_and_icons_but_keeps_apps_and_settings()
    {
        var db = Seed();
        _rig.Exec(db, "INSERT INTO settings VALUES ('ui.theme', 'Dark')");

        HistoryDeleter.DeleteAll(db, "DELETE");

        _rig.Scalar<long>(db, "SELECT COUNT(*) FROM sessions").Should().Be(0);
        _rig.Scalar<long>(db, "SELECT COUNT(*) FROM daily_usage").Should().Be(0);
        _rig.Scalar<long>(db, "SELECT COUNT(*) FROM icons").Should().Be(0);
        _rig.Scalar<long>(db, "SELECT COUNT(*) FROM apps").Should().Be(2);
        _rig.Rows(db, "SELECT value FROM settings WHERE key = 'ui.theme'").Should().Equal("Dark");
    }
}
