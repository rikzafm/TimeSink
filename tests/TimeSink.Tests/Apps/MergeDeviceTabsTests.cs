using FluentAssertions;
using Microsoft.Data.Sqlite;
using TimeSink.Core.Apps;
using TimeSink.Core.Data;
using TimeSink.Core.Devices;
using Xunit;

namespace TimeSink.Tests.Apps;

public sealed class MergeDeviceTabsTests : IDisposable
{
    private readonly string _dir;
    private readonly string _dbPath;
    private const string WinDevId = "dev-win";
    private const string MacDevId = "dev-mac";

    public MergeDeviceTabsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ts-merge-tabs-" + Guid.NewGuid().ToString("N"));
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

        // 2. Apps:
        // App 1: Windows VS Code (Main)
        // App 2: Windows Notepad
        // App 3: Mac VS Code (Suggested match)
        // App 4: Mac Terminal
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO apps (id, device_id, exe_path, bundle_id, display_name, added_utc, state) VALUES
                (1, 'dev-win', 'c:\\tools\\code.exe', NULL, 'VS Code', 1000, 'tracked'),
                (2, 'dev-win', 'c:\\windows\\notepad.exe', NULL, 'Notepad', 1000, 'tracked'),
                (3, 'dev-mac', '/Applications/Visual Studio Code.app', 'com.microsoft.VSCode', 'VS Code', 1000, 'tracked'),
                (4, 'dev-mac', '/Applications/Utilities/Terminal.app', 'com.apple.Terminal', 'Terminal', 1000, 'tracked');
                """;
            cmd.ExecuteNonQuery();
        }

        // 3. Daily usage
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO daily_usage (app_id, day, seconds) VALUES
                (1, '2026-03-10', 10800),
                (2, '2026-03-10', 3600),
                (3, '2026-03-10', 7200),
                (4, '2026-03-10', 1800);
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
    public void MergePlan_device_tabs_filtering_preserves_ticks_across_devices()
    {
        var icons = new AppIconSource(_dbPath, new FakeIcons());
        var desktop = new FakeDesktop();
        var catalogue = new AppCatalogue(_dbPath, desktop, icons);

        var plan = catalogue.OpenMergePlan(1);
        plan.Should().NotBeNull();
        plan!.MainId.Should().Be(1);
        plan.MainDeviceId.Should().Be(WinDevId);
        plan.MainSeconds.Should().Be(10800);

        // Verify known devices
        plan.Devices.Should().Contain([WinDevId, MacDevId]);

        // Suggested match on Mac with same name
        plan.SuggestedMatches.Should().ContainSingle(c => c.AppId == 3 && c.DeviceId == MacDevId && c.Name == "VS Code");

        // Filter by Windows device
        plan.DeviceFilter = WinDevId;
        var winCandidates = plan.Shown();
        winCandidates.Should().OnlyContain(c => c.DeviceId == WinDevId);
        winCandidates.Should().ContainSingle(c => c.AppId == 2 && c.Name == "Notepad");

        // Tick Windows Notepad
        var notepadCandidate = winCandidates.Single(c => c.AppId == 2);
        plan.Toggle(notepadCandidate.Key);
        plan.IsTicked(notepadCandidate.Key).Should().BeTrue();
        plan.TickedCountForDevice(WinDevId).Should().Be(1);
        plan.TickedCountForDevice(MacDevId).Should().Be(0);

        // Switch to Mac device tab
        plan.DeviceFilter = MacDevId;
        var macCandidates = plan.Shown();
        macCandidates.Should().OnlyContain(c => c.DeviceId == MacDevId);
        macCandidates.Should().HaveCount(2);

        // Tick Mac VS Code
        var macVsCode = macCandidates.Single(c => c.AppId == 3);
        plan.Toggle(macVsCode.Key);
        plan.IsTicked(macVsCode.Key).Should().BeTrue();
        plan.TickedCountForDevice(MacDevId).Should().Be(1);

        // Verify Windows tick is still preserved!
        plan.IsTicked(notepadCandidate.Key).Should().BeTrue();
        plan.TickedCountForDevice(WinDevId).Should().Be(1);

        // Confirm merge
        var outcome = plan.Confirm();
        outcome.Should().Be(CatalogueOutcome.Done);

        // Verify merged state in AppLibrary
        using var lib = AppLibrary.Open(_dbPath);
        var tracked = lib.Tracked(DeviceScope.All);
        var mainEntry = tracked.Single(a => a.Id == 1);
        mainEntry.Seconds.Should().Be(10800 + 3600 + 7200);
        mainEntry.Merged.Should().HaveCount(2);
        mainEntry.Merged.Should().Contain(m => m.Id == 2 && m.DeviceId == WinDevId && m.Seconds == 3600);
        mainEntry.Merged.Should().Contain(m => m.Id == 3 && m.DeviceId == MacDevId && m.Seconds == 7200);

        // Unmerge Mac app
        lib.Unmerge(3);
        var trackedAfterUnmerge = lib.Tracked(DeviceScope.All);
        trackedAfterUnmerge.Should().HaveCount(3); // Main VS Code, Notepad still merged into it, Mac VS Code restored, Terminal
        var restoredMac = trackedAfterUnmerge.Single(a => a.Id == 3);
        restoredMac.DisplayName.Should().Be("VS Code");
        restoredMac.DeviceId.Should().Be(MacDevId);
        restoredMac.Seconds.Should().Be(7200);

        var updatedMain = trackedAfterUnmerge.Single(a => a.Id == 1);
        updatedMain.Seconds.Should().Be(10800 + 3600);
        updatedMain.Merged.Should().ContainSingle(m => m.Id == 2);
    }
}
