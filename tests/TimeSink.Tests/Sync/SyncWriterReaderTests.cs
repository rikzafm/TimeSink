using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using TimeSink.Core.Data;
using TimeSink.Core.Devices;
using TimeSink.Core.Sync;
using TimeSink.Tests.Backup;
using Xunit;
using Xunit.Abstractions;

namespace TimeSink.Tests.Sync;

public sealed class SyncWriterReaderTests(ITestOutputHelper output)
{
    private static void SetDevice(BackupTestRig rig, string dbPath, string deviceId, string deviceName, string platform)
    {
        rig.Exec(dbPath, "PRAGMA foreign_keys = OFF; " +
                         $"INSERT OR REPLACE INTO settings (key, value) VALUES ('device.id', '{deviceId}'), ('device.name', '{deviceName}'); " +
                         $"UPDATE devices SET device_id = '{deviceId}', name = '{deviceName}', platform = '{platform}' WHERE is_local = 1; " +
                         $"UPDATE apps SET device_id = '{deviceId}'; " +
                         "PRAGMA foreign_keys = ON;");
    }

    [Fact]
    public void Write_then_read_round_trip_imports_apps_sessions_and_device()
    {
        using var rig = new BackupTestRig();
        var syncFolder = rig.P("sync");
        var now = DateTimeOffset.Parse("2026-11-01T12:00:00Z");

        // 1. Setup local source DB on device "dev-mac-001"
        var srcDb = rig.NewDb("source.db",
            (@"/Applications/Xcode.app", 1761984000, 1761987600),
            (@"/Applications/Safari.app", 1761988000, 1761989800));

        SetDevice(rig, srcDb, "dev-mac-001", "MacBook Air", "macos");
        rig.Exec(srcDb, "UPDATE apps SET bundle_id = 'com.apple.dt.Xcode' WHERE exe_path = '/Applications/Xcode.app'");
        rig.Exec(srcDb, "UPDATE apps SET bundle_id = 'com.apple.Safari' WHERE exe_path = '/Applications/Safari.app'");

        // 2. Export snapshot
        var snapshotPath = SyncWriter.Write(srcDb, syncFolder, now, "1.0.0");
        File.Exists(snapshotPath).Should().BeTrue();
        Path.GetFileName(snapshotPath).Should().Be("dev-mac-001.tssync");

        // 3. Setup destination DB on device "dev-win-001"
        var dstDb = rig.NewDb("dest.db",
            (@"c:\windows\notepad.exe", 1761900000, 1761903600));
        SetDevice(rig, dstDb, "dev-win-001", "Desktop PC", "windows");

        // 4. Import snapshot into destination DB
        var result = SyncReader.Import(snapshotPath, dstDb, TimeZoneInfo.Utc);

        result.Status.Should().Be(SyncStatus.Success);
        result.AppsAdded.Should().Be(2);
        result.SessionsAdded.Should().Be(2);
        result.SessionsUpdated.Should().Be(0);
        result.SessionsSkipped.Should().Be(0);

        // Verify remote device record
        rig.Scalar<long>(dstDb, "SELECT COUNT(*) FROM devices WHERE device_id = 'dev-mac-001' AND is_local = 0 AND state = 'active'").Should().Be(1);
        rig.Scalar<string>(dstDb, "SELECT name FROM devices WHERE device_id = 'dev-mac-001'").Should().Be("MacBook Air");
        rig.Scalar<string>(dstDb, "SELECT platform FROM devices WHERE device_id = 'dev-mac-001'").Should().Be("macos");

        // Verify sessions and daily usage
        rig.Scalar<long>(dstDb, "SELECT COUNT(*) FROM sessions WHERE app_id IN (SELECT id FROM apps WHERE device_id = 'dev-mac-001')").Should().Be(2);
        rig.Scalar<long>(dstDb, "SELECT SUM(seconds) FROM daily_usage WHERE app_id IN (SELECT id FROM apps WHERE device_id = 'dev-mac-001')").Should().Be(3600 + 1800);
    }

    [Fact]
    public void Import_twice_is_idempotent()
    {
        using var rig = new BackupTestRig();
        var syncFolder = rig.P("sync");
        var now = DateTimeOffset.Parse("2026-11-01T12:00:00Z");

        var srcDb = rig.NewDb("source.db",
            (@"/Applications/Xcode.app", 1761984000, 1761987600),
            (@"/Applications/Safari.app", 1761988000, 1761989800));
        SetDevice(rig, srcDb, "dev-mac-001", "MacBook Air", "macos");

        var snapshotPath = SyncWriter.Write(srcDb, syncFolder, now, "1.0.0");

        var dstDb = rig.NewDb("dest.db");
        SetDevice(rig, dstDb, "dev-win-001", "Desktop PC", "windows");

        // First import
        var first = SyncReader.Import(snapshotPath, dstDb, TimeZoneInfo.Utc);
        first.Status.Should().Be(SyncStatus.Success);
        first.AppsAdded.Should().Be(2);
        first.SessionsAdded.Should().Be(2);

        var snapshotFingerprint = rig.Fingerprint(dstDb);

        // Second import of the identical file
        var second = SyncReader.Import(snapshotPath, dstDb, TimeZoneInfo.Utc);
        second.Status.Should().Be(SyncStatus.Success);
        second.AppsAdded.Should().Be(0);
        second.SessionsAdded.Should().Be(0);
        second.SessionsUpdated.Should().Be(0);
        second.SessionsSkipped.Should().Be(2);

        // Verify fingerprint has not changed
        rig.Fingerprint(dstDb).Should().Be(snapshotFingerprint);
    }

    [Fact]
    public void Open_session_in_snapshot_is_closed_at_last_seen_and_expands_on_subsequent_snapshot()
    {
        using var rig = new BackupTestRig();
        var syncFolder = rig.P("sync");
        var baseTime = DateTimeOffset.Parse("2026-11-01T12:00:00Z");

        // 1. Source DB has an open session (end_utc is NULL, last_seen_utc = 1761991000)
        var srcDb = rig.NewDb("source.db");
        SetDevice(rig, srcDb, "dev-mac-001", "MacBook Air", "macos");
        rig.Exec(srcDb, "INSERT INTO apps (exe_path, bundle_id, device_id, display_name, category, state, added_utc) " +
                       "VALUES ('/Applications/VSCode.app', 'com.microsoft.VSCode', 'dev-mac-001', 'VS Code', 'Development', 'tracked', 1761980000)");
        rig.Exec(srcDb, "INSERT INTO sessions (app_id, start_utc, end_utc, last_seen_utc, end_reason) " +
                       "VALUES ((SELECT id FROM apps WHERE device_id = 'dev-mac-001'), 1761990000, NULL, 1761991000, NULL)");

        // 2. Export first snapshot
        var snap1 = SyncWriter.Write(srcDb, syncFolder, baseTime, "1.0.0");

        var dstDb = rig.NewDb("dest.db");
        SetDevice(rig, dstDb, "dev-win-001", "Desktop PC", "windows");

        // 3. First import: open session closed at last_seen_utc (1761991000)
        var res1 = SyncReader.Import(snap1, dstDb, TimeZoneInfo.Utc);
        res1.Status.Should().Be(SyncStatus.Success);
        res1.SessionsAdded.Should().Be(1);

        rig.Scalar<long>(dstDb, "SELECT end_utc FROM sessions WHERE start_utc = 1761990000").Should().Be(1761991000);

        // 4. Update source DB: the session continued to 1761993600 and exited
        rig.Exec(srcDb, "UPDATE sessions SET end_utc = 1761993600, last_seen_utc = 1761993600, end_reason = 'exit' WHERE start_utc = 1761990000");

        // Export second snapshot
        var snap2 = SyncWriter.Write(srcDb, syncFolder, baseTime.AddHours(1), "1.0.0");

        // 5. Second import: existing session updates and expands end_utc
        var res2 = SyncReader.Import(snap2, dstDb, TimeZoneInfo.Utc);
        res2.Status.Should().Be(SyncStatus.Success);
        res2.SessionsAdded.Should().Be(0);
        res2.SessionsUpdated.Should().Be(1);
        res2.SessionsSkipped.Should().Be(0);

        rig.Scalar<long>(dstDb, "SELECT end_utc FROM sessions WHERE start_utc = 1761990000").Should().Be(1761993600);
        rig.Scalar<string>(dstDb, "SELECT end_reason FROM sessions WHERE start_utc = 1761990000").Should().Be("exit");
    }

    [Fact]
    public void Bad_hash_or_corrupt_file_is_rejected_and_leaves_db_untouched()
    {
        using var rig = new BackupTestRig();
        var syncFolder = rig.P("sync");
        var now = DateTimeOffset.Parse("2026-11-01T12:00:00Z");

        var srcDb = rig.NewDb("source.db", (@"/Applications/Xcode.app", 1761984000, 1761987600));
        SetDevice(rig, srcDb, "dev-mac-001", "MacBook Air", "macos");

        var snapshotPath = SyncWriter.Write(srcDb, syncFolder, now);

        // Tamper with snapshot.db inside zip or manifest
        var corruptFile = rig.P("corrupt.tssync");
        File.Copy(snapshotPath, corruptFile);
        using (var zip = ZipFile.Open(corruptFile, ZipArchiveMode.Update))
        {
            var manifestEntry = zip.GetEntry(SyncManifest.EntryName)!;
            SyncManifest manifest;
            using (var reader = new StreamReader(manifestEntry.Open(), Encoding.UTF8))
            {
                manifest = SyncManifest.Parse(reader.ReadToEnd());
            }
            manifestEntry.Delete();

            // Tamper hash
            var tampered = manifest with { Hash = "0000000000000000000000000000000000000000000000000000000000000000" };
            var newEntry = zip.CreateEntry(SyncManifest.EntryName);
            using var writer = new StreamWriter(newEntry.Open(), new UTF8Encoding(false));
            writer.Write(tampered.ToJson());
        }

        var dstDb = rig.NewDb("dest.db");
        SetDevice(rig, dstDb, "dev-win-001", "Desktop PC", "windows");

        var before = rig.Fingerprint(dstDb);
        var result = SyncReader.Import(corruptFile, dstDb, TimeZoneInfo.Utc);

        result.Status.Should().Be(SyncStatus.BadHash);
        rig.Fingerprint(dstDb).Should().Be(before);
    }

    [Fact]
    public void Newer_format_is_rejected_with_update_required()
    {
        using var rig = new BackupTestRig();
        var syncFolder = rig.P("sync");
        var now = DateTimeOffset.Parse("2026-11-01T12:00:00Z");

        var srcDb = rig.NewDb("source.db", (@"/Applications/Xcode.app", 1761984000, 1761987600));
        SetDevice(rig, srcDb, "dev-mac-001", "MacBook Air", "macos");

        var snapshotPath = SyncWriter.Write(srcDb, syncFolder, now);

        var newerFile = rig.P("newer.tssync");
        File.Copy(snapshotPath, newerFile);
        using (var zip = ZipFile.Open(newerFile, ZipArchiveMode.Update))
        {
            var manifestEntry = zip.GetEntry(SyncManifest.EntryName)!;
            SyncManifest manifest;
            using (var reader = new StreamReader(manifestEntry.Open(), Encoding.UTF8))
            {
                manifest = SyncManifest.Parse(reader.ReadToEnd());
            }
            manifestEntry.Delete();

            var newerManifest = manifest with { Format = 99 };
            var newEntry = zip.CreateEntry(SyncManifest.EntryName);
            using var writer = new StreamWriter(newEntry.Open(), new UTF8Encoding(false));
            writer.Write(newerManifest.ToJson());
        }

        var dstDb = rig.NewDb("dest.db");
        SetDevice(rig, dstDb, "dev-win-001", "Desktop PC", "windows");

        var before = rig.Fingerprint(dstDb);
        var result = SyncReader.Import(newerFile, dstDb, TimeZoneInfo.Utc);

        result.Status.Should().Be(SyncStatus.NewerFormat);
        result.Message.Should().Contain("Update required");
        rig.Fingerprint(dstDb).Should().Be(before);
    }

    [Fact]
    public void Cloud_placeholder_file_reports_pending_without_importing()
    {
        using var rig = new BackupTestRig();
        var dstDb = rig.NewDb("dest.db");

        // 0-byte placeholder
        var placeholderFile = rig.P("dev-mac-001.tssync");
        File.WriteAllBytes(placeholderFile, []);

        var before = rig.Fingerprint(dstDb);
        var result = SyncReader.Import(placeholderFile, dstDb, TimeZoneInfo.Utc);

        result.Status.Should().Be(SyncStatus.Pending);
        rig.Fingerprint(dstDb).Should().Be(before);

        // Marker seam placeholder
        File.WriteAllBytes(placeholderFile, [1, 2, 3]);
        File.WriteAllText(placeholderFile + ".placeholder", "test");

        var resultMarker = SyncReader.Import(placeholderFile, dstDb, TimeZoneInfo.Utc);
        resultMarker.Status.Should().Be(SyncStatus.Pending);
        rig.Fingerprint(dstDb).Should().Be(before);
    }

    [Theory]
    [InlineData("dev-mac-001 (1).tssync")]
    [InlineData("dev-mac-001 - Copy.tssync")]
    [InlineData("dev-mac-001-conflict-20261101.tssync")]
    [InlineData("dev-mac-001.sync-conflict-20261101.tssync")]
    public void Cloud_conflict_copies_are_ignored(string filename)
    {
        using var rig = new BackupTestRig();
        var conflictFile = rig.P(filename);
        File.WriteAllText(conflictFile, "placeholder");

        var dstDb = rig.NewDb("dest.db");
        var before = rig.Fingerprint(dstDb);

        var result = SyncReader.Import(conflictFile, dstDb, TimeZoneInfo.Utc);
        result.Status.Should().Be(SyncStatus.ConflictCopy);
        rig.Fingerprint(dstDb).Should().Be(before);
    }

    [Fact]
    public void Self_snapshot_file_is_ignored()
    {
        using var rig = new BackupTestRig();
        var syncFolder = rig.P("sync");
        var now = DateTimeOffset.Parse("2026-11-01T12:00:00Z");

        var srcDb = rig.NewDb("source.db", (@"/Applications/Xcode.app", 1761984000, 1761987600));
        SetDevice(rig, srcDb, "dev-mac-001", "MacBook Air", "macos");

        var snapshotPath = SyncWriter.Write(srcDb, syncFolder, now);

        // Import on the SAME database
        var before = rig.Fingerprint(srcDb);
        var result = SyncReader.Import(snapshotPath, srcDb, TimeZoneInfo.Utc);

        result.Status.Should().Be(SyncStatus.SelfFile);
        rig.Fingerprint(srcDb).Should().Be(before);
    }

    [Fact]
    public void Third_device_file_is_ignored_when_two_devices_exist()
    {
        using var rig = new BackupTestRig();
        var syncFolder = rig.P("sync");
        var now = DateTimeOffset.Parse("2026-11-01T12:00:00Z");

        // Local device "dev-win-001" already has remote device "dev-mac-001"
        var dstDb = rig.NewDb("dest.db");
        SetDevice(rig, dstDb, "dev-win-001", "Desktop PC", "windows");
        rig.Exec(dstDb, "INSERT INTO devices (device_id, name, platform, is_local, first_seen_utc, state) " +
                       "VALUES ('dev-mac-001', 'MacBook Air', 'macos', 0, '2026-11-01T00:00:00Z', 'active')");

        // Third device "dev-linux-001" produces a snapshot
        var thirdDb = rig.NewDb("third.db", (@"/usr/bin/vim", 1761984000, 1761987600));
        SetDevice(rig, thirdDb, "dev-linux-001", "Linux Laptop", "macos");

        var thirdSnapshot = SyncWriter.Write(thirdDb, syncFolder, now);

        var before = rig.Fingerprint(dstDb);
        var result = SyncReader.Import(thirdSnapshot, dstDb, TimeZoneInfo.Utc);

        result.Status.Should().Be(SyncStatus.ThirdDeviceIgnored);
        result.Message.Should().Be("Only two devices are supported yet");
        rig.Fingerprint(dstDb).Should().Be(before);
    }

    [Fact]
    public void Forgotten_device_snapshot_is_ignored_and_preserves_history()
    {
        using var rig = new BackupTestRig();
        var syncFolder = rig.P("sync");
        var now = DateTimeOffset.Parse("2026-11-01T12:00:00Z");

        var srcDb = rig.NewDb("source.db", (@"/Applications/Xcode.app", 1761984000, 1761987600));
        SetDevice(rig, srcDb, "dev-mac-001", "MacBook Air", "macos");

        var snapshot = SyncWriter.Write(srcDb, syncFolder, now);

        // Destination has "dev-mac-001" marked as forgotten
        var dstDb = rig.NewDb("dest.db");
        SetDevice(rig, dstDb, "dev-win-001", "Desktop PC", "windows");
        rig.Exec(dstDb, "INSERT INTO devices (device_id, name, platform, is_local, first_seen_utc, state) " +
                       "VALUES ('dev-mac-001', 'MacBook Air', 'macos', 0, '2026-11-01T00:00:00Z', 'forgotten')");

        var before = rig.Fingerprint(dstDb);
        var result = SyncReader.Import(snapshot, dstDb, TimeZoneInfo.Utc);

        result.Status.Should().Be(SyncStatus.DeviceForgotten);
        rig.Fingerprint(dstDb).Should().Be(before);
    }

    [Fact]
    public void Clock_skew_imports_by_session_timestamps_not_file_mtime()
    {
        using var rig = new BackupTestRig();
        var syncFolder = rig.P("sync");
        var now = DateTimeOffset.Parse("2026-11-01T12:00:00Z");

        // Sessions with timestamps far in past and future
        var srcDb = rig.NewDb("source.db",
            (@"/Applications/Safari.app", 1761900000, 1761901800), // Past
            (@"/Applications/Safari.app", 1762070000, 1762073600)  // Future
        );
        SetDevice(rig, srcDb, "dev-mac-001", "MacBook Air", "macos");

        var snapshot = SyncWriter.Write(srcDb, syncFolder, now);

        // Artificially change file modified time to simulate filesystem skew
        File.SetLastWriteTimeUtc(snapshot, DateTime.UtcNow.AddDays(-10));

        var dstDb = rig.NewDb("dest.db");
        SetDevice(rig, dstDb, "dev-win-001", "Desktop PC", "windows");

        var result = SyncReader.Import(snapshot, dstDb, TimeZoneInfo.Utc);
        result.Status.Should().Be(SyncStatus.Success);
        result.SessionsAdded.Should().Be(2);

        // Verify session start timestamps are strictly preserved regardless of file mtime
        rig.Scalar<long>(dstDb, "SELECT MIN(start_utc) FROM sessions WHERE app_id IN (SELECT id FROM apps WHERE device_id = 'dev-mac-001')").Should().Be(1761900000);
        rig.Scalar<long>(dstDb, "SELECT MAX(start_utc) FROM sessions WHERE app_id IN (SELECT id FROM apps WHERE device_id = 'dev-mac-001')").Should().Be(1762070000);
    }

    [Fact]
    public void Benchmark_100k_session_snapshot_imports_within_acceptable_time()
    {
        using var rig = new BackupTestRig();
        var syncFolder = rig.P("sync");
        var now = DateTimeOffset.Parse("2026-11-01T12:00:00Z");

        // 1. Create a database with 100,000 sessions across 5 apps
        var srcDb = rig.P("source-100k.db");
        using (var db = Db.Open(srcDb))
        {
            SetDevice(rig, srcDb, "dev-mac-perf", "MacBook Pro Performance", "macos");

            using var tx = db.Connection.BeginTransaction();
            for (int a = 1; a <= 5; a++)
            {
                using var cmd = db.Connection.CreateCommand();
                cmd.CommandText = "INSERT INTO apps (exe_path, bundle_id, device_id, display_name, category, state, added_utc) " +
                                  $"VALUES ('/Applications/App{a}.app', 'com.test.app{a}', 'dev-mac-perf', 'App {a}', 'Work', 'tracked', 1760000000);";
                cmd.ExecuteNonQuery();
            }

            var baseStart = 1760000000L;
            for (int i = 0; i < 100_000; i++)
            {
                var appId = (i % 5) + 1;
                var start = baseStart + (i * 300);
                var end = start + 180;
                using var cmd = db.Connection.CreateCommand();
                cmd.CommandText = $"INSERT INTO sessions (app_id, start_utc, end_utc, last_seen_utc, end_reason) VALUES ({appId}, {start}, {end}, {end}, 'exit');";
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }

        // 2. Export 100k snapshot and measure size
        var exportSw = Stopwatch.StartNew();
        var snapshotPath = SyncWriter.Write(srcDb, syncFolder, now);
        exportSw.Stop();

        var fileInfo = new FileInfo(snapshotPath);
        var fileSizeKb = fileInfo.Length / 1024;
        output.WriteLine($"100k snapshot exported in {exportSw.ElapsedMilliseconds} ms. Size: {fileSizeKb} KB ({fileSizeKb / 1024.0:F2} MB)");

        // 3. Import 100k snapshot into fresh destination DB and measure import time
        var dstDb = rig.P("dest-perf.db");
        using (var db = Db.Open(dstDb))
        {
            SetDevice(rig, dstDb, "dev-win-perf", "Desktop Performance", "windows");
        }

        var importSw = Stopwatch.StartNew();
        var result = SyncReader.Import(snapshotPath, dstDb, TimeZoneInfo.Utc);
        importSw.Stop();

        output.WriteLine($"100k snapshot imported in {importSw.ElapsedMilliseconds} ms. Result: {result.SessionsAdded} sessions added.");

        result.Status.Should().Be(SyncStatus.Success);
        result.SessionsAdded.Should().Be(100_000);
        rig.Scalar<long>(dstDb, "SELECT COUNT(*) FROM sessions WHERE app_id IN (SELECT id FROM apps WHERE device_id = 'dev-mac-perf')").Should().Be(100_000);

        // Acceptance criteria: 100k import must complete in under 15 seconds
        importSw.Elapsed.TotalSeconds.Should().BeLessThan(15.0);
    }
}
