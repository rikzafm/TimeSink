path_tests = 'tests/TimeSink.Tests/Sync/SyncWriterReaderTests.cs'
with open(path_tests, 'rb') as f:
    raw = f.read()

nl = b'\r\n' if b'\r\n' in raw else b'\n'

target = b"""        using (var lib = TimeSink.Core.Apps.AppLibrary.Open(dstDb))
        {
            lib.Tracked().Single(t => t.Id == winAppId).Kind.Should().Be(TimeSink.Core.Apps.AppKind.Coding);
        }
    }
}""".replace(b'\n', nl)

new_tests = b"""        using (var lib = TimeSink.Core.Apps.AppLibrary.Open(dstDb))
        {
            lib.Tracked().Single(t => t.Id == winAppId).Kind.Should().Be(TimeSink.Core.Apps.AppKind.Coding);
        }
    }

    [Fact]
    public void Import_preserves_locally_customized_kind_on_remote_device_app()
    {
        using var rig = new BackupTestRig();
        var syncFolder = rig.P("sync");
        var now = DateTimeOffset.Parse("2026-11-01T12:00:00Z");

        // Remote device has a tracked app with default kind 0 (Other)
        var srcDb = rig.NewDb("source.db", (@"/Applications/Safari.app", 1761984000, 1761987600));
        SetDevice(rig, srcDb, "dev-mac-001", "MacBook Air", "macos");

        // Destination device imports the remote app
        var dstDb = rig.NewDb("dest.db");
        SetDevice(rig, dstDb, "dev-win-001", "Desktop PC", "windows");

        var snapshotPath = SyncWriter.Write(srcDb, syncFolder, now, "1.0.0");
        var res1 = SyncReader.Import(snapshotPath, dstDb, TimeZoneInfo.Utc);
        res1.Status.Should().Be(SyncStatus.Success);

        // User on destination customizes the remote app's kind to Browsing (4)
        long macAppId;
        using (var lib = TimeSink.Core.Apps.AppLibrary.Open(dstDb))
        {
            var tracked = lib.Tracked();
            macAppId = tracked.Single(t => t.ExePath.Contains("Safari")).Id;
            lib.SetKind(macAppId, TimeSink.Core.Apps.AppKind.Browsing);
        }

        // Another snapshot arrives from remote device (where Safari still has kind 0)
        var snapshotPath2 = SyncWriter.Write(srcDb, syncFolder, now.AddMinutes(5), "1.0.0");
        var res2 = SyncReader.Import(snapshotPath2, dstDb, TimeZoneInfo.Utc);
        res2.Status.Should().Be(SyncStatus.Success);

        // Verify that the customized category was NOT reverted to Other (0)
        using (var lib = TimeSink.Core.Apps.AppLibrary.Open(dstDb))
        {
            lib.Tracked().Single(t => t.Id == macAppId).Kind.Should().Be(TimeSink.Core.Apps.AppKind.Browsing);
        }
    }

    [Fact]
    public void Import_preserves_customized_kind_when_remote_app_is_canonical_in_cross_device_merge()
    {
        using var rig = new BackupTestRig();
        var syncFolder = rig.P("sync");
        var now = DateTimeOffset.Parse("2026-11-01T12:00:00Z");

        var srcDb = rig.NewDb("source.db", (@"/Applications/VSCode.app", 1761984000, 1761987600));
        SetDevice(rig, srcDb, "dev-mac-001", "MacBook Air", "macos");

        var dstDb = rig.NewDb("dest.db", (@"C:\Tools\Code.exe", 1761984000, 1761987600));
        SetDevice(rig, dstDb, "dev-win-001", "Desktop PC", "windows");

        // 1. Initial import so both apps exist on dstDb
        var snap1 = SyncWriter.Write(srcDb, syncFolder, now, "1.0.0");
        SyncReader.Import(snap1, dstDb, TimeZoneInfo.Utc);

        long macAppId, winAppId;
        using (var lib = TimeSink.Core.Apps.AppLibrary.Open(dstDb))
        {
            macAppId = lib.Tracked().Single(t => t.ExePath.Contains("VSCode")).Id;
            winAppId = lib.Tracked().Single(t => t.ExePath.Contains("Code.exe")).Id;
            // Merge where remote mac app is canonical (or local merged into it)
            lib.Merge(winAppId, macAppId);
            // Set kind to Coding
            lib.SetKind(macAppId, TimeSink.Core.Apps.AppKind.Coding);
        }

        // 2. Subsequent snapshot arrives from remote device
        var snap2 = SyncWriter.Write(srcDb, syncFolder, now.AddMinutes(2), "1.0.0");
        var res = SyncReader.Import(snap2, dstDb, TimeZoneInfo.Utc);
        res.Status.Should().Be(SyncStatus.Success);

        // 3. Verify neither app reverted to Other
        using (var lib = TimeSink.Core.Apps.AppLibrary.Open(dstDb))
        {
            var tracked = lib.Tracked();
            var main = tracked.Single(t => t.Id == macAppId);
            main.Kind.Should().Be(TimeSink.Core.Apps.AppKind.Coding);
        }
    }
}""".replace(b'\n', nl)

assert target in raw, "target not found in SyncWriterReaderTests.cs"
raw = raw.replace(target, new_tests)

with open(path_tests, 'wb') as f:
    f.write(raw)
print("Tests added successfully")

