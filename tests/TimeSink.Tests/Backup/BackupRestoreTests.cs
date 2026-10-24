using TimeSink.Core.Backup;

namespace TimeSink.Tests.Backup;

public sealed class BackupRestoreTests : IDisposable
{
    private readonly BackupTestRig _rig = new();
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    public void Dispose() => _rig.Dispose();

    [Fact]
    public void Backup_of_a_live_wal_database_then_replace_restore_round_trips()
    {
        var live = _rig.NewDb("live.db", (@"c:\a\one.exe", 1_700_000_000, 1_700_003_600), (@"c:\a\two.exe", 1_700_010_000, 1_700_010_600));
        // A writer stays connected (WAL, uncheckpointed) while the backup runs.
        using var writer = _rig.Open(live);
        BackupTestRig.Exec(writer, "PRAGMA journal_mode=WAL");
        BackupTestRig.Exec(writer, "UPDATE apps SET category = 'Games' WHERE exe_path = 'c:\\a\\one.exe'");
        var file = _rig.P("snap.timesink");
        BackupService.Create(live, file, Now, "1.0.0");

        // The live database then changes (history deleted), and we restore.
        BackupTestRig.Exec(writer, "DELETE FROM sessions");
        BackupTestRig.Exec(writer, "DELETE FROM apps WHERE exe_path = 'c:\\a\\two.exe'");

        var result = RestoreService.Replace(file, live, _rig.P("backups"), Now, TimeZoneInfo.Utc);

        _rig.Rows(live, "SELECT exe_path, category FROM apps ORDER BY exe_path")
            .Should().Equal(@"c:\a\one.exe|Games", @"c:\a\two.exe|Other");
        _rig.Scalar<long>(live, "SELECT COUNT(*) FROM sessions").Should().Be(2);
        _rig.Scalar<long>(live, "SELECT SUM(seconds) FROM daily_usage").Should().Be(3600 + 600); // rebuilt
        result.SafetyCopy.Should().NotBeNull();
        File.Exists(result.SafetyCopy!).Should().BeTrue();
        Path.GetFileName(result.SafetyCopy!).Should().StartWith("pre-restore-");
    }

    private string MakeBackup(string name, params (string Exe, long Start, long? End)[] sessions)
    {
        var db = _rig.NewDb(name + ".db", sessions);
        var file = _rig.P(name + ".timesink");
        BackupService.Create(db, file, Now, "1.0.0");
        return file;
    }

    [Fact]
    public void Merging_the_same_backup_twice_changes_nothing_the_second_time()
    {
        var other = MakeBackup("other",
            (@"c:\a\one.exe", 1_700_000_000, 1_700_003_600),   // also in live: same start, ignored
            (@"C:\A\Two.exe", 1_700_020_000, 1_700_020_300),   // exe differs only by case: matches live app
            (@"c:\b\new.exe", 1_700_030_000, 1_700_030_100));  // unknown app: inserted
        var live = _rig.NewDb("live.db", (@"c:\a\one.exe", 1_700_000_000, 1_700_003_600), (@"c:\a\two.exe", 1_700_010_000, 1_700_010_600));
        _rig.Exec(live, "INSERT INTO settings VALUES ('ui.theme', 'Light')");

        var first = RestoreService.Merge(other, live, TimeZoneInfo.Utc);
        var afterFirst = _rig.Fingerprint(live);
        var second = RestoreService.Merge(other, live, TimeZoneInfo.Utc);

        first.Should().Be(new MergeResult(AppsAdded: 1, SessionsAdded: 2, SessionsSkipped: 1));
        second.Should().Be(new MergeResult(AppsAdded: 0, SessionsAdded: 0, SessionsSkipped: 3));
        _rig.Fingerprint(live).Should().Be(afterFirst);
        _rig.Scalar<long>(live, "SELECT COUNT(*) FROM apps").Should().Be(3);
        _rig.Scalar<long>(live, "SELECT COUNT(*) FROM sessions").Should().Be(4);
        _rig.Scalar<long>(live, "SELECT SUM(seconds) FROM daily_usage").Should().Be(3600 + 600 + 300 + 100);
        _rig.Rows(live, "SELECT value FROM settings WHERE key = 'ui.theme'").Should().Equal("Light");
    }

    [Fact]
    public void Merge_keeps_state_and_remaps_canonical_ids_for_inserted_apps()
    {
        var src = _rig.NewDb("src.db", (@"c:\x\chrome.exe", 1_700_000_000, 1_700_000_100), (@"c:\x\beta.exe", 1_700_001_000, 1_700_001_100));
        _rig.Exec(src, "UPDATE apps SET canonical_id = (SELECT id FROM apps WHERE exe_path = 'c:\\x\\chrome.exe'), state = 'retired', display_name = 'Chrome Beta' WHERE exe_path = 'c:\\x\\beta.exe'");
        var file = _rig.P("src.timesink");
        BackupService.Create(src, file, Now);
        var live = _rig.NewDb("live.db", (@"c:\other.exe", 1_600_000_000, 1_600_000_100)); // ids differ from the backup's

        RestoreService.Merge(file, live, TimeZoneInfo.Utc);

        _rig.Rows(live, "SELECT b.display_name, b.state, c.exe_path FROM apps b JOIN apps c ON c.id = b.canonical_id")
            .Should().Equal(@"Chrome Beta|retired|c:\x\chrome.exe");
    }

    [Fact]
    public void A_backup_from_a_newer_schema_is_refused_and_leaves_the_database_untouched()
    {
        var src = _rig.NewDb("future.db", (@"c:\a\one.exe", 1_700_000_000, 1_700_000_100));
        _rig.Exec(src, "PRAGMA user_version = 99");
        var file = _rig.P("future.timesink");
        BackupService.Create(src, file, Now);
        var live = _rig.NewDb("live.db", (@"c:\a\two.exe", 1_700_010_000, 1_700_010_600));
        var before = _rig.Fingerprint(live);

        var inspect = () => RestoreService.Inspect(file);
        var replace = () => RestoreService.Replace(file, live, _rig.P("bk"), Now, TimeZoneInfo.Utc);
        var merge = () => RestoreService.Merge(file, live, TimeZoneInfo.Utc);

        inspect.Should().Throw<BackupException>().WithMessage("*newer version*");
        replace.Should().Throw<BackupException>();
        merge.Should().Throw<BackupException>();
        _rig.Fingerprint(live).Should().Be(before);
        Directory.Exists(_rig.P("bk")).Should().BeFalse(); // refused before any safety copy
    }

    [Fact]
    public void A_backup_from_an_older_schema_is_migrated_forward_before_use()
    {
        var file = MakeBackup("old", (@"c:\a\one.exe", 1_700_000_000, 1_700_000_600));
        var live = _rig.NewDb("live.db");
        // Pretend this build ships a second migration.
        IReadOnlyList<TimeSink.Core.Data.Migration> v2 = [.. TimeSink.Core.Data.Migrator.Shipped,
            new(2, c => BackupTestRig.Exec(c, "ALTER TABLE apps ADD COLUMN note TEXT"))];

        var info = RestoreService.Inspect(file, v2);
        var merged = RestoreService.Merge(file, live, TimeZoneInfo.Utc, v2);

        info.NeedsMigration.Should().BeTrue();
        info.Manifest.SchemaVersion.Should().Be(1);
        merged.SessionsAdded.Should().Be(1);
        _rig.Scalar<long>(live, "SELECT SUM(seconds) FROM daily_usage").Should().Be(600);
    }

    [Fact]
    public void Files_that_are_not_backups_are_refused()
    {
        var junk = _rig.P("junk.timesink");
        File.WriteAllText(junk, "hello");
        var missingDb = _rig.P("nodb.timesink");
        using (var z = System.IO.Compression.ZipFile.Open(missingDb, System.IO.Compression.ZipArchiveMode.Create))
            using (var w = new StreamWriter(z.CreateEntry("manifest.json").Open()))
                w.Write("""{"format":1,"schemaVersion":1,"appVersion":"1","createdUtc":"2026-01-01T00:00:00Z"}""");

        ((Action)(() => RestoreService.Inspect(junk))).Should().Throw<BackupException>();
        ((Action)(() => RestoreService.Inspect(missingDb))).Should().Throw<BackupException>().WithMessage("*no database*");
    }
}
