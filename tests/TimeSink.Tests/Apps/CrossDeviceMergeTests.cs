using FluentAssertions;
using Microsoft.Data.Sqlite;
using TimeSink.Core.Apps;
using TimeSink.Core.Data;
using TimeSink.Core.Devices;
using TimeSink.Core.Usage;
using Xunit;

namespace TimeSink.Tests.Apps;

public sealed class CrossDeviceMergeTests : IDisposable
{
    private readonly string _dir;
    private readonly string _dbPath;
    private const string WinDevId = "dev-win";
    private const string MacDevId = "dev-mac";

    public CrossDeviceMergeTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ts-cross-merge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "timesink.db");

        using var db = Db.Open(_dbPath);
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();

        // 1. Devices
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO devices (device_id, name, platform, is_local, first_seen_utc, state) VALUES
                ('dev-win', 'Windows PC', 'windows', 1, '2026-01-01T00:00:00Z', 'active'),
                ('dev-mac', 'MacBook Pro', 'macos', 0, '2026-01-01T00:00:00Z', 'active');
                """;
            cmd.ExecuteNonQuery();
        }
        db.SetSetting("device.id", WinDevId);
        db.SetSetting("device.name", "Windows PC");

        // 2. Apps: Windows app and Mac app with same display name
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO apps (id, device_id, exe_path, bundle_id, display_name, added_utc, state) VALUES
                (1, 'dev-win', 'c:\\tools\\code.exe', NULL, 'VS Code', 1000, 'tracked'),
                (2, 'dev-mac', '/Applications/Visual Studio Code.app', 'com.microsoft.VSCode', 'VS Code', 1000, 'tracked');
                """;
            cmd.ExecuteNonQuery();
        }

        // 3. Sessions & Daily Usage: 3h on Windows, 2h on Mac
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO daily_usage (app_id, day, seconds) VALUES
                (1, '2026-03-10', 10800),
                (2, '2026-03-10', 7200);

                INSERT INTO sessions (app_id, start_utc, end_utc, last_seen_utc, end_reason) VALUES
                (1, 1773136800, 1773147600, 1773147600, 'exit'),
                (2, 1773144000, 1773151200, 1773151200, 'exit');
                """;
            cmd.ExecuteNonQuery();
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed class FakeDesktop : IDesktop
    {
        public WindowCapture CaptureWindows() => new([], new Dictionary<string, string>());
        public IReadOnlyList<InstalledApp> ScanInstalled() => [];
    }

    [Fact]
    public void MergePlan_discovers_other_device_candidates_and_suggests_same_name_matches()
    {
        var icons = new AppIconSource(_dbPath, new FakeIcons());
        var desktop = new FakeDesktop();
        var catalogue = new AppCatalogue(_dbPath, desktop, icons);

        var plan = catalogue.OpenMergePlan(1);
        plan.Should().NotBeNull();
        plan!.MainId.Should().Be(1);
        plan.MainDeviceId.Should().Be(WinDevId);

        // Candidates include Mac app
        plan.Candidates.Should().Contain(c => c.AppId == 2 && c.DeviceId == MacDevId && c.Name == "VS Code");

        // Known devices include both
        plan.Devices.Should().Contain([WinDevId, MacDevId]);

        // SuggestedMatches finds same name on Mac
        plan.SuggestedMatches.Should().ContainSingle(c => c.AppId == 2 && c.DeviceId == MacDevId);

        // Ticked count per device
        plan.TickedCountForDevice(MacDevId).Should().Be(0);

        // Toggle candidate on Mac
        var macCandidate = plan.Candidates.Single(c => c.AppId == 2);
        plan.Toggle(macCandidate.Key);
        plan.TickedCountForDevice(MacDevId).Should().Be(1);
        plan.IsTicked(macCandidate.Key).Should().BeTrue();

        // Confirm merge
        var outcome = plan.Confirm();
        outcome.Should().Be(CatalogueOutcome.Done);

        // Verify in AppLibrary
        using var lib = AppLibrary.Open(_dbPath);
        var tracked = lib.Tracked(DeviceScope.All);
        tracked.Should().ContainSingle();
        var mainEntry = tracked.Single();
        mainEntry.Id.Should().Be(1);
        mainEntry.DeviceId.Should().Be(WinDevId);
        mainEntry.Seconds.Should().Be(18000); // 10800 + 7200
        mainEntry.Merged.Should().ContainSingle(m => m.Id == 2 && m.DeviceId == MacDevId && m.Seconds == 7200);

        // Verify queries see the combined totals and per-device split
        var ctx = new UsageContext(DateTimeOffset.FromUnixTimeSeconds(1773165600), TimeZoneInfo.Utc, null,
            () => null, () => WeekStart.Mon, gamesOnly: false, dbPath: _dbPath, deviceScope: DeviceScope.All, localDeviceId: WinDevId);

        var appDetail = AppDetailQuery.Run(ctx, 1, 2026);
        appDetail.Should().NotBeNull();
        appDetail!.TotalSeconds.Should().Be(18000);
        appDetail.DeviceSeconds.Should().NotBeNull();
        appDetail.DeviceSeconds![WinDevId].Should().Be(10800);
        appDetail.DeviceSeconds[MacDevId].Should().Be(7200);

        var allResult = AllQuery.Run(ctx);
        var appRow = allResult.Apps.Single(a => a.AppId == 1);
        appRow.Seconds.Should().Be(18000);
        appRow.Devices.Should().Contain([WinDevId, MacDevId]);
    }

    [Fact]
    public void Unmerge_across_devices_restores_independent_tracked_apps_and_splits()
    {
        // First merge them
        using (var lib = AppLibrary.Open(_dbPath))
        {
            lib.Merge(2, 1).Should().BeTrue();
        }

        // Now unmerge
        using (var lib = AppLibrary.Open(_dbPath))
        {
            lib.Unmerge(2);
            var tracked = lib.Tracked(DeviceScope.All);
            tracked.Should().HaveCount(2);

            var winApp = tracked.Single(a => a.Id == 1);
            winApp.DeviceId.Should().Be(WinDevId);
            winApp.Seconds.Should().Be(10800);
            winApp.Merged.Should().BeEmpty();

            var macApp = tracked.Single(a => a.Id == 2);
            macApp.DeviceId.Should().Be(MacDevId);
            macApp.Seconds.Should().Be(7200);
            macApp.Merged.Should().BeEmpty();
        }

        // Verify query splits after unmerge
        var ctx = new UsageContext(DateTimeOffset.FromUnixTimeSeconds(1773165600), TimeZoneInfo.Utc, null,
            () => null, () => WeekStart.Mon, gamesOnly: false, dbPath: _dbPath, deviceScope: DeviceScope.All, localDeviceId: WinDevId);

        var winDetail = AppDetailQuery.Run(ctx, 1, 2026);
        winDetail!.TotalSeconds.Should().Be(10800);
        winDetail.DeviceSeconds![WinDevId].Should().Be(10800);
        winDetail.DeviceSeconds.Should().NotContainKey(MacDevId);

        var macDetail = AppDetailQuery.Run(ctx, 2, 2026);
        macDetail!.TotalSeconds.Should().Be(7200);
        macDetail.DeviceSeconds![MacDevId].Should().Be(7200);
        macDetail.DeviceSeconds.Should().NotContainKey(WinDevId);
    }

    [Fact]
    public void MakeMain_across_devices_swaps_canonical_app_preserving_device_identities()
    {
        // Merge Mac (2) into Windows (1)
        using (var lib = AppLibrary.Open(_dbPath))
        {
            lib.Merge(2, 1).Should().BeTrue();
        }

        // Now make Mac app (2) the Main app
        using (var lib = AppLibrary.Open(_dbPath))
        {
            lib.MakeMain(2).Should().BeTrue();

            var tracked = lib.Tracked(DeviceScope.All);
            tracked.Should().ContainSingle();

            var main = tracked.Single();
            main.Id.Should().Be(2);
            main.DeviceId.Should().Be(MacDevId);
            main.Seconds.Should().Be(18000);
            main.Merged.Should().ContainSingle(m => m.Id == 1 && m.DeviceId == WinDevId && m.Seconds == 10800);
        }

        // Verify queries under new Main app
        var ctx = new UsageContext(DateTimeOffset.FromUnixTimeSeconds(1773165600), TimeZoneInfo.Utc, null,
            () => null, () => WeekStart.Mon, gamesOnly: false, dbPath: _dbPath, deviceScope: DeviceScope.All, localDeviceId: WinDevId);

        var appDetail = AppDetailQuery.Run(ctx, 2, 2026);
        appDetail.Should().NotBeNull();
        appDetail!.TotalSeconds.Should().Be(18000);
        appDetail.DeviceSeconds![MacDevId].Should().Be(7200);
        appDetail.DeviceSeconds[WinDevId].Should().Be(10800);
    }

    [Fact]
    public void Retiring_Main_app_retires_alias_and_preserves_history_per_device()
    {
        // Merge Mac (2) into Windows (1)
        using (var lib = AppLibrary.Open(_dbPath))
        {
            lib.Merge(2, 1).Should().BeTrue();
            lib.Retire(1, 1773165600);

            // Tracked is empty
            lib.Tracked(DeviceScope.All).Should().BeEmpty();

            // Retired list has the retired app
            lib.Retired().Should().Contain(r => r.Id == 1 && r.DeviceId == WinDevId);
        }

        // Sessions and daily usage are preserved in the DB
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM daily_usage";
            ((long)cmd.ExecuteScalar()!).Should().Be(2);
        }

        // Restore main app
        using (var lib = AppLibrary.Open(_dbPath))
        {
            lib.Restore(1, 1773165600);
            var tracked = lib.Tracked(DeviceScope.All);
            tracked.Should().ContainSingle(t => t.Id == 1 && t.Merged.Count == 1);
        }
    }
}
