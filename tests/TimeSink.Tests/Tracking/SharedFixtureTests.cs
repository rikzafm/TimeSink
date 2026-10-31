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

    [Theory]
    [InlineData("sync-import.json", "write-then-read")]
    [InlineData("sync-import.json", "import-twice-idempotent")]
    [InlineData("sync-import.json", "open-session-closed-then-expanded")]
    [InlineData("sync-import.json", "bad-hash-ignored")]
    [InlineData("sync-import.json", "placeholder-file-pending")]
    [InlineData("sync-import.json", "conflict-copy-ignored")]
    [InlineData("sync-import.json", "third-device-ignored")]
    [InlineData("sync-import.json", "clock-skew")]
    public void Sync_import_fixtures_pass(string filename, string caseName)
    {
        var doc = FixtureLoader.Load<SyncImportDocument>(filename);
        var c = doc.Cases.Single(x => x.Name == caseName);

        using var rig = new Backup.BackupTestRig();
        var now = DateTimeOffset.Parse(doc.BaseTimeUtc);
        var syncFolder = rig.P("sync");

        if (caseName == "write-then-read")
        {
            var srcDb = rig.NewDb("src.db");
            rig.Exec(srcDb, "PRAGMA foreign_keys = OFF; " +
                            $"INSERT OR REPLACE INTO settings (key, value) VALUES ('device.id', '{c.RemoteDeviceId}'), ('device.name', '{c.RemoteDeviceName}'); " +
                            $"UPDATE devices SET device_id = '{c.RemoteDeviceId}', name = '{c.RemoteDeviceName}', platform = '{c.RemotePlatform}' WHERE is_local = 1; " +
                            "PRAGMA foreign_keys = ON;");

            foreach (var app in c.Apps!)
            {
                rig.Exec(srcDb, "INSERT INTO apps (display_name, exe_path, bundle_id, device_id, category, state, added_utc) " +
                                $"VALUES ('{app.Name}', '{app.ExePath}', '{app.BundleId}', '{c.RemoteDeviceId}', '{app.Category}', 'tracked', 1761980000);");
            }
            foreach (var s in c.Sessions!)
            {
                rig.Exec(srcDb, "INSERT INTO sessions (app_id, start_utc, end_utc, last_seen_utc, end_reason) " +
                                $"SELECT id, {s.StartUtc}, {s.EndUtc}, {s.LastSeenUtc}, 'exit' FROM apps WHERE bundle_id = '{s.BundleId}';");
            }

            var file = TimeSink.Core.Sync.SyncWriter.Write(srcDb, syncFolder, now);

            var dstDb = rig.NewDb("dst.db");
            rig.Exec(dstDb, "PRAGMA foreign_keys = OFF; " +
                            "INSERT OR REPLACE INTO settings (key, value) VALUES ('device.id', 'dev-win-001'), ('device.name', 'Windows PC'); " +
                            "UPDATE devices SET device_id = 'dev-win-001', name = 'Windows PC', platform = 'windows' WHERE is_local = 1; " +
                            "PRAGMA foreign_keys = ON;");

            var result = TimeSink.Core.Sync.SyncReader.Import(file, dstDb, TimeZoneInfo.Utc);
            result.Status.Should().Be(TimeSink.Core.Sync.SyncStatus.Success);
            result.AppsAdded.Should().Be(c.ExpectedAppsAdded!.Value);
            result.SessionsAdded.Should().Be(c.ExpectedSessionsAdded!.Value);
        }
        else if (caseName == "import-twice-idempotent")
        {
            var srcDb = rig.NewDb("src.db", (@"/Applications/Safari.app", 1761984000, 1761987600), (@"/Applications/Xcode.app", 1761988000, 1761989800));
            rig.Exec(srcDb, "PRAGMA foreign_keys = OFF; " +
                            "INSERT OR REPLACE INTO settings (key, value) VALUES ('device.id', 'dev-mac-001'), ('device.name', 'MacBook Air'); " +
                            "UPDATE devices SET device_id = 'dev-mac-001', name = 'MacBook Air', platform = 'macos' WHERE is_local = 1; " +
                            "UPDATE apps SET device_id = 'dev-mac-001'; " +
                            "PRAGMA foreign_keys = ON;");

            var file = TimeSink.Core.Sync.SyncWriter.Write(srcDb, syncFolder, now);

            var dstDb = rig.NewDb("dst.db");
            rig.Exec(dstDb, "PRAGMA foreign_keys = OFF; " +
                            "INSERT OR REPLACE INTO settings (key, value) VALUES ('device.id', 'dev-win-001'), ('device.name', 'Windows PC'); " +
                            "UPDATE devices SET device_id = 'dev-win-001', name = 'Windows PC', platform = 'windows' WHERE is_local = 1; " +
                            "PRAGMA foreign_keys = ON;");

            var res1 = TimeSink.Core.Sync.SyncReader.Import(file, dstDb, TimeZoneInfo.Utc);
            res1.Status.Should().Be(TimeSink.Core.Sync.SyncStatus.Success);
            res1.AppsAdded.Should().Be(2);
            res1.SessionsAdded.Should().Be(2);

            var fingerprintAfterFirst = rig.Fingerprint(dstDb);

            var res2 = TimeSink.Core.Sync.SyncReader.Import(file, dstDb, TimeZoneInfo.Utc);
            res2.Status.Should().Be(TimeSink.Core.Sync.SyncStatus.Success);
            res2.AppsAdded.Should().Be(0);
            res2.SessionsAdded.Should().Be(0);
            res2.SessionsSkipped.Should().Be(2);

            rig.Fingerprint(dstDb).Should().Be(fingerprintAfterFirst);
        }
        else if (caseName == "open-session-closed-then-expanded")
        {
            var srcDb = rig.NewDb("src.db");
            rig.Exec(srcDb, "PRAGMA foreign_keys = OFF; " +
                            "INSERT OR REPLACE INTO settings (key, value) VALUES ('device.id', 'dev-mac-001'), ('device.name', 'MacBook Air'); " +
                            "UPDATE devices SET device_id = 'dev-mac-001', name = 'MacBook Air', platform = 'macos' WHERE is_local = 1; " +
                            "INSERT INTO apps (exe_path, bundle_id, device_id, display_name, category, state, added_utc) " +
                            "VALUES ('/Applications/VSCode.app', 'com.microsoft.VSCode', 'dev-mac-001', 'VS Code', 'Development', 'tracked', 1761980000); " +
                            "INSERT INTO sessions (app_id, start_utc, end_utc, last_seen_utc, end_reason) " +
                            "VALUES ((SELECT id FROM apps WHERE device_id = 'dev-mac-001'), 1761990000, NULL, 1761991000, NULL); " +
                            "PRAGMA foreign_keys = ON;");

            var snap1 = TimeSink.Core.Sync.SyncWriter.Write(srcDb, syncFolder, now);

            var dstDb = rig.NewDb("dst.db");
            rig.Exec(dstDb, "PRAGMA foreign_keys = OFF; " +
                            "INSERT OR REPLACE INTO settings (key, value) VALUES ('device.id', 'dev-win-001'), ('device.name', 'Windows PC'); " +
                            "UPDATE devices SET device_id = 'dev-win-001', name = 'Windows PC', platform = 'windows' WHERE is_local = 1; " +
                            "PRAGMA foreign_keys = ON;");

            var res1 = TimeSink.Core.Sync.SyncReader.Import(snap1, dstDb, TimeZoneInfo.Utc);
            res1.Status.Should().Be(TimeSink.Core.Sync.SyncStatus.Success);
            rig.Scalar<long>(dstDb, "SELECT end_utc FROM sessions WHERE start_utc = 1761990000").Should().Be(1761991000);

            // Update source
            rig.Exec(srcDb, "UPDATE sessions SET end_utc = 1761993600, last_seen_utc = 1761993600, end_reason = 'exit' WHERE start_utc = 1761990000");
            var snap2 = TimeSink.Core.Sync.SyncWriter.Write(srcDb, syncFolder, now.AddHours(1));

            var res2 = TimeSink.Core.Sync.SyncReader.Import(snap2, dstDb, TimeZoneInfo.Utc);
            res2.Status.Should().Be(TimeSink.Core.Sync.SyncStatus.Success);
            res2.SessionsUpdated.Should().Be(1);
            rig.Scalar<long>(dstDb, "SELECT end_utc FROM sessions WHERE start_utc = 1761990000").Should().Be(1761993600);
        }
        else if (caseName == "bad-hash-ignored")
        {
            var badFile = rig.P("bad.tssync");
            File.WriteAllBytes(badFile, [0x50, 0x4B, 0x03, 0x04]); // Truncated zip
            var dstDb = rig.NewDb("dst.db");
            var res = TimeSink.Core.Sync.SyncReader.Import(badFile, dstDb, TimeZoneInfo.Utc);
            res.Status.Should().Be(TimeSink.Core.Sync.SyncStatus.BadHash);
        }
        else if (caseName == "placeholder-file-pending")
        {
            var placeholderFile = rig.P("dev-mac-001.tssync");
            File.WriteAllBytes(placeholderFile, []);
            var dstDb = rig.NewDb("dst.db");
            var res = TimeSink.Core.Sync.SyncReader.Import(placeholderFile, dstDb, TimeZoneInfo.Utc);
            res.Status.Should().Be(TimeSink.Core.Sync.SyncStatus.Pending);
        }
        else if (caseName == "conflict-copy-ignored")
        {
            var dstDb = rig.NewDb("dst.db");
            foreach (var name in c.ConflictFileNames!)
            {
                var file = rig.P(name);
                File.WriteAllText(file, "test");
                var res = TimeSink.Core.Sync.SyncReader.Import(file, dstDb, TimeZoneInfo.Utc);
                res.Status.Should().Be(TimeSink.Core.Sync.SyncStatus.ConflictCopy);
            }
        }
        else if (caseName == "third-device-ignored")
        {
            var dstDb = rig.NewDb("dst.db");
            rig.Exec(dstDb, "PRAGMA foreign_keys = OFF; " +
                            "INSERT OR REPLACE INTO settings (key, value) VALUES ('device.id', 'dev-win-001'), ('device.name', 'Desktop PC'); " +
                            "UPDATE devices SET device_id = 'dev-win-001', name = 'Desktop PC', platform = 'windows' WHERE is_local = 1; " +
                            "INSERT INTO devices (device_id, name, platform, is_local, first_seen_utc, state) VALUES ('dev-mac-001', 'MacBook Air', 'macos', 0, '2026-11-01T00:00:00Z', 'active'); " +
                            "PRAGMA foreign_keys = ON;");

            var thirdDb = rig.NewDb("third.db", (@"/usr/bin/top", 1761984000, 1761987600));
            rig.Exec(thirdDb, "PRAGMA foreign_keys = OFF; " +
                              "INSERT OR REPLACE INTO settings (key, value) VALUES ('device.id', 'dev-linux-001'), ('device.name', 'Linux'); " +
                              "UPDATE devices SET device_id = 'dev-linux-001', name = 'Linux', platform = 'macos' WHERE is_local = 1; " +
                              "UPDATE apps SET device_id = 'dev-linux-001'; " +
                              "PRAGMA foreign_keys = ON;");

            var file = TimeSink.Core.Sync.SyncWriter.Write(thirdDb, syncFolder, now);
            var res = TimeSink.Core.Sync.SyncReader.Import(file, dstDb, TimeZoneInfo.Utc);
            res.Status.Should().Be(TimeSink.Core.Sync.SyncStatus.ThirdDeviceIgnored);
        }
        else if (caseName == "clock-skew")
        {
            var srcDb = rig.NewDb("src.db", (@"/Applications/Safari.app", 1761900000, 1761901800), (@"/Applications/Safari.app", 1762070000, 1762073600));
            rig.Exec(srcDb, "PRAGMA foreign_keys = OFF; " +
                            "INSERT OR REPLACE INTO settings (key, value) VALUES ('device.id', 'dev-mac-001'), ('device.name', 'MacBook Air'); " +
                            "UPDATE devices SET device_id = 'dev-mac-001', name = 'MacBook Air', platform = 'macos' WHERE is_local = 1; " +
                            "UPDATE apps SET device_id = 'dev-mac-001'; " +
                            "PRAGMA foreign_keys = ON;");

            var file = TimeSink.Core.Sync.SyncWriter.Write(srcDb, syncFolder, now);
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-10));

            var dstDb = rig.NewDb("dst.db");
            rig.Exec(dstDb, "PRAGMA foreign_keys = OFF; " +
                            "INSERT OR REPLACE INTO settings (key, value) VALUES ('device.id', 'dev-win-001'), ('device.name', 'Windows PC'); " +
                            "UPDATE devices SET device_id = 'dev-win-001', name = 'Windows PC', platform = 'windows' WHERE is_local = 1; " +
                            "PRAGMA foreign_keys = ON;");

            var res = TimeSink.Core.Sync.SyncReader.Import(file, dstDb, TimeZoneInfo.Utc);
            res.Status.Should().Be(TimeSink.Core.Sync.SyncStatus.Success);
            res.SessionsAdded.Should().Be(2);
            rig.Scalar<long>(dstDb, "SELECT MIN(start_utc) FROM sessions WHERE app_id IN (SELECT id FROM apps WHERE device_id = 'dev-mac-001')").Should().Be(1761900000);
            rig.Scalar<long>(dstDb, "SELECT MAX(start_utc) FROM sessions WHERE app_id IN (SELECT id FROM apps WHERE device_id = 'dev-mac-001')").Should().Be(1762070000);
        }
    }
}
