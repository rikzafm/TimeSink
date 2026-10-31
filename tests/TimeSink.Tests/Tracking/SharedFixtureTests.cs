using TimeSink.Core.Backup;

namespace TimeSink.Tests.Tracking;

public sealed class SharedFixtureTests
{
    [Theory]
    [InlineData("single-session.json", "single-session-open-and-close")]
    [InlineData("single-session.json", "multi-process-coalescing")]
    public void Single_session_and_multi_process_fixtures_pass(string filename, string caseName)
    {
        var doc = FixtureLoader.Load<TrackingFixtureDocument>(filename);
        var c = doc.Cases.Single(x => x.Name == caseName);
        FixtureLoader.RunTrackingCase(doc, c);
    }

    [Theory]
    [InlineData("midnight-and-dst.json", "midnight-split-utc")]
    [InlineData("midnight-and-dst.json", "midnight-split-dst-america-new-york")]
    [InlineData("midnight-and-dst.json", "dst-spring-forward-23h-day")]
    [InlineData("midnight-and-dst.json", "dst-fall-back-25h-day")]
    public void Midnight_and_dst_fixtures_pass(string filename, string caseName)
    {
        var doc = FixtureLoader.Load<TrackingFixtureDocument>(filename);
        var c = doc.Cases.Single(x => x.Name == caseName);
        FixtureLoader.RunTrackingCase(doc, c);
    }

    [Theory]
    [InlineData("suspend-resume-and-power.json", "suspend-and-resume")]
    [InlineData("suspend-resume-and-power.json", "tick-gap-fallback")]
    [InlineData("suspend-resume-and-power.json", "pause-on-sleep-disabled")]
    [InlineData("suspend-resume-and-power.json", "shutdown-event")]
    public void Suspend_resume_and_power_fixtures_pass(string filename, string caseName)
    {
        var doc = FixtureLoader.Load<TrackingFixtureDocument>(filename);
        var c = doc.Cases.Single(x => x.Name == caseName);
        FixtureLoader.RunTrackingCase(doc, c);
    }

    [Theory]
    [InlineData("lock-and-unlock.json", "lock-splits-when-not-counting-locked")]
    [InlineData("lock-and-unlock.json", "lock-ignored-when-counting-locked")]
    public void Lock_and_unlock_fixtures_pass(string filename, string caseName)
    {
        var doc = FixtureLoader.Load<TrackingFixtureDocument>(filename);
        var c = doc.Cases.Single(x => x.Name == caseName);
        FixtureLoader.RunTrackingCase(doc, c);
    }

    [Theory]
    [InlineData("crash-recovery.json", "recovery-continues-fresh-heartbeat")]
    [InlineData("crash-recovery.json", "recovery-closes-stale-heartbeat")]
    [InlineData("crash-recovery.json", "recovery-closes-on-pid-reuse")]
    [InlineData("crash-recovery.json", "recovery-removes-short-session")]
    public void Crash_recovery_fixtures_pass(string filename, string caseName)
    {
        var doc = FixtureLoader.Load<TrackingFixtureDocument>(filename);
        var c = doc.Cases.Single(x => x.Name == caseName);
        FixtureLoader.RunTrackingCase(doc, c);
    }

    [Theory]
    [InlineData("min-session-and-clock.json", "short-session-removed-and-subtracted")]
    [InlineData("min-session-and-clock.json", "exact-minimum-session-kept")]
    [InlineData("min-session-and-clock.json", "clock-moving-backwards-safe")]
    public void Min_session_and_clock_fixtures_pass(string filename, string caseName)
    {
        var doc = FixtureLoader.Load<TrackingFixtureDocument>(filename);
        var c = doc.Cases.Single(x => x.Name == caseName);
        FixtureLoader.RunTrackingCase(doc, c);
    }

    [Fact]
    public void Accumulator_rebuild_fixture_passes()
    {
        var doc = FixtureLoader.Load<AccumulatorRebuildDocument>("accumulator-rebuild.json");
        using var rig = new Rig(TimeZoneInfo.Utc);

        var appMap = new Dictionary<long, long>();
        foreach (var app in doc.Apps)
        {
            if (app.Path.Equals(Rig.NotepadPath, StringComparison.OrdinalIgnoreCase))
            {
                appMap[app.Id] = rig.NotepadId;
            }
            else
            {
                var id = rig.Db.InsertApp(app.Path, app.Name, app.AddedUtc);
                appMap[app.Id] = id;
            }
        }

        foreach (var s in doc.Sessions)
        {
            var mappedAppId = appMap[s.AppId];
            rig.Store.OpenSession(mappedAppId, s.StartUtc, 1, s.StartUtc);
            rig.Usage.Add(mappedAppId, s.StartUtc, s.EndUtc);
            rig.Store.CloseSession(rig.Sessions.Last().Id, s.EndUtc, s.EndReason);
        }

        // Verify incremental equals expected
        var daily = rig.Store.GetDailyUsage();
        foreach (var exp in doc.ExpectedDailyUsage)
        {
            var mappedAppId = appMap[exp.AppId];
            daily.Should().Contain(r => r.AppId == mappedAppId && r.Day == exp.Day && r.Seconds == exp.Seconds);
        }

        // Verify full RebuildAll equals incremental
        rig.Usage.RebuildAll();
        var rebuilt = rig.Store.GetDailyUsage();
        rebuilt.Should().Equal(daily);
    }

    [Theory]
    [InlineData("backup-and-restore.json", "replace-restore-round-trip")]
    [InlineData("backup-and-restore.json", "merge-twice-is-idempotent")]
    [InlineData("backup-and-restore.json", "newer-schema-refused")]
    public void Backup_and_restore_fixtures_pass(string filename, string caseName)
    {
        var doc = FixtureLoader.Load<BackupRestoreDocument>(filename);
        var c = doc.Cases.Single(x => x.Name == caseName);

        using var rig = new Backup.BackupTestRig();
        var now = DateTimeOffset.Parse(doc.BaseTimeUtc);

        if (caseName == "replace-restore-round-trip")
        {
            var live = rig.NewDb("live.db", c.LiveSessions!.Select(s => (s.ExePath, s.StartUtc, (long?)s.EndUtc)).ToArray());
            var file = rig.P("snap.timesink");
            BackupService.Create(live, file, now, "1.0.0");

            rig.Exec(live, "DELETE FROM sessions");
            var result = RestoreService.Replace(file, live, rig.P("backups"), now, TimeZoneInfo.Utc);

            rig.Scalar<long>(live, "SELECT COUNT(*) FROM apps").Should().Be(c.ExpectedAppsCount!.Value);
            rig.Scalar<long>(live, "SELECT COUNT(*) FROM sessions").Should().Be(c.ExpectedSessionsCount!.Value);
            rig.Scalar<long>(live, "SELECT SUM(seconds) FROM daily_usage").Should().Be(c.ExpectedTotalDailySeconds!.Value);
        }
        else if (caseName == "merge-twice-is-idempotent")
        {
            var otherDb = rig.NewDb("other.db", c.IncomingSessions!.Select(s => (s.ExePath, s.StartUtc, (long?)s.EndUtc)).ToArray());
            var otherFile = rig.P("other.timesink");
            BackupService.Create(otherDb, otherFile, now, "1.0.0");

            var live = rig.NewDb("live.db", c.LiveSessions!.Select(s => (s.ExePath, s.StartUtc, (long?)s.EndUtc)).ToArray());

            var first = RestoreService.Merge(otherFile, live, TimeZoneInfo.Utc);
            var afterFirst = rig.Fingerprint(live);
            var second = RestoreService.Merge(otherFile, live, TimeZoneInfo.Utc);

            first.Should().Be(new MergeResult(c.FirstMergeResult!.AppsAdded, c.FirstMergeResult.SessionsAdded, c.FirstMergeResult.SessionsSkipped));
            second.Should().Be(new MergeResult(c.SecondMergeResult!.AppsAdded, c.SecondMergeResult.SessionsAdded, c.SecondMergeResult.SessionsSkipped));
            rig.Fingerprint(live).Should().Be(afterFirst);
            rig.Scalar<long>(live, "SELECT SUM(seconds) FROM daily_usage").Should().Be(c.ExpectedFinalTotalDailySeconds!.Value);
        }
        else if (caseName == "newer-schema-refused")
        {
            var src = rig.NewDb("newer.db", (@"c:\x.exe", 1_700_000_000, (long?)1_700_000_100));
            rig.Exec(src, $"PRAGMA user_version = {c.IncomingSchemaVersion}");
            var file = rig.P("newer.timesink");
            BackupService.Create(src, file, now);

            var live = rig.NewDb("live.db");
            var before = rig.Fingerprint(live);

            var act = () => RestoreService.Replace(file, live, rig.P("bk"), now, TimeZoneInfo.Utc);
            act.Should().Throw<BackupException>().WithMessage($"*{c.ExpectedError}*");
            rig.Fingerprint(live).Should().Be(before);
        }
    }
}
