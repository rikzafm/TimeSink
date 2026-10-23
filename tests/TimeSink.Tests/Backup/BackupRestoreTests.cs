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
}
