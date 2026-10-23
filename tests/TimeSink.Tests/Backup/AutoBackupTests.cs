using TimeSink.Core.Backup;
using TimeSink.Core.Data;

namespace TimeSink.Tests.Backup;

public sealed class AutoBackupTests : IDisposable
{
    private readonly BackupTestRig _rig = new();
    public void Dispose() => _rig.Dispose();

    private static string Daily(DateTime d) => $"daily-{d:yyyyMMdd}.timesink";

    [Fact]
    public void Retention_keeps_the_last_7_backups_plus_the_newest_of_each_older_month()
    {
        var now = new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.Zero);
        var names = new List<string>();
        for (var d = new DateTime(2026, 7, 1); d <= new DateTime(2026, 10, 23); d = d.AddDays(1)) names.Add(Daily(d));
        names.Add("weekly-20250101.timesink");     // older than 12 months: not kept
        names.Add("manual-20260101-120000.timesink"); // not automatic: never touched
        names.Add("notes.txt");

        var doomed = BackupRetention.FilesToDelete(names, now).ToHashSet();
        var kept = names.Where(n => !doomed.Contains(n)).ToList();

        kept.Should().BeEquivalentTo([
            Daily(new DateTime(2026, 10, 17)), Daily(new DateTime(2026, 10, 18)), Daily(new DateTime(2026, 10, 19)),
            Daily(new DateTime(2026, 10, 20)), Daily(new DateTime(2026, 10, 21)), Daily(new DateTime(2026, 10, 22)),
            Daily(new DateTime(2026, 10, 23)),                         // 7 newest
            Daily(new DateTime(2026, 9, 30)), Daily(new DateTime(2026, 8, 31)), Daily(new DateTime(2026, 7, 31)), // newest of older months
            "manual-20260101-120000.timesink", "notes.txt"]);
        doomed.Should().Contain("weekly-20250101.timesink");
    }

    [Fact]
    public void Retention_with_few_files_deletes_nothing()
    {
        var now = new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.Zero);
        BackupRetention.FilesToDelete([Daily(new DateTime(2026, 10, 22)), Daily(new DateTime(2026, 10, 23))], now).Should().BeEmpty();
    }

    [Fact]
    public void Due_backup_is_written_to_the_configured_folder_and_records_last_backup()
    {
        var db = _rig.NewDb("live.db", (@"c:\a\one.exe", 1_700_000_000, 1_700_000_600));
        var folder = _rig.P("bk");
        using (var d = Db.Open(db)) { d.SetSetting("backup.mode", "Daily"); d.SetSetting("backup.folder", folder); }
        var now = new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.Zero);

        var first = AutoBackup.RunIfDue(db, now, _rig.P("default"));
        var again = AutoBackup.RunIfDue(db, now.AddHours(2), _rig.P("default"));
        var nextDay = AutoBackup.RunIfDue(db, now.AddDays(1), _rig.P("default"));

        first.Should().Be(Path.Combine(folder, "daily-20261023.timesink"));
        File.Exists(first!).Should().BeTrue();
        again.Should().BeNull();
        nextDay.Should().Be(Path.Combine(folder, "daily-20261024.timesink"));
        using var check = Db.Open(db);
        check.GetSetting("backup.lastUtc").Should().Be(now.AddDays(1).ToUnixTimeSeconds().ToString());
        RestoreService.Inspect(first!).Apps.Should().Be(1);
    }

    [Fact]
    public void Weekly_mode_waits_a_week_and_Off_never_backs_up()
    {
        var db = _rig.NewDb("live.db", (@"c:\a\one.exe", 1_700_000_000, 1_700_000_600));
        var folder = _rig.P("bk");
        var now = new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.Zero);
        using (var d = Db.Open(db)) { d.SetSetting("backup.mode", "Weekly"); d.SetSetting("backup.folder", folder); }

        AutoBackup.RunIfDue(db, now, folder).Should().EndWith("weekly-20261023.timesink");
        AutoBackup.RunIfDue(db, now.AddDays(3), folder).Should().BeNull();
        AutoBackup.RunIfDue(db, now.AddDays(7), folder).Should().EndWith("weekly-20261030.timesink");

        using (var d = Db.Open(db)) d.SetSetting("backup.mode", "Off");
        AutoBackup.RunIfDue(db, now.AddDays(30), folder).Should().BeNull();
    }

    [Fact]
    public void Running_a_backup_prunes_old_automatic_backups()
    {
        var db = _rig.NewDb("live.db", (@"c:\a\one.exe", 1_700_000_000, 1_700_000_600));
        var folder = _rig.P("bk");
        Directory.CreateDirectory(folder);
        for (var i = 1; i <= 12; i++) File.WriteAllText(Path.Combine(folder, Daily(new DateTime(2026, 10, i))), "x");
        using (var d = Db.Open(db)) { d.SetSetting("backup.mode", "Daily"); d.SetSetting("backup.folder", folder); }

        AutoBackup.RunIfDue(db, new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.Zero), folder);

        var left = Directory.GetFiles(folder).Select(Path.GetFileName).Order().ToList();
        left.Should().Equal(Daily(new DateTime(2026, 10, 7)), Daily(new DateTime(2026, 10, 8)), Daily(new DateTime(2026, 10, 9)),
            Daily(new DateTime(2026, 10, 10)), Daily(new DateTime(2026, 10, 11)), Daily(new DateTime(2026, 10, 12)), Daily(new DateTime(2026, 10, 23)));
    }
}
