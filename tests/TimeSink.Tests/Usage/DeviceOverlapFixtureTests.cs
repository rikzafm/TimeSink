using FluentAssertions;
using Microsoft.Data.Sqlite;
using TimeSink.Core.Data;
using TimeSink.Core.Data.Migrations;
using TimeSink.Core.Devices;
using TimeSink.Core.Usage;
using TimeSink.Tests.Tracking;
using Xunit;

namespace TimeSink.Tests.Usage;

public sealed class DeviceOverlapFixtureTests
{
    public sealed class DeviceItem
    {
        public required string Id { get; set; }
        public required string Name { get; set; }
        public required string Platform { get; set; }
        public bool IsLocal { get; set; }
        public string State { get; set; } = "active";
    }

    public sealed class AppItem
    {
        public long Id { get; set; }
        public required string DeviceId { get; set; }
        public required string Name { get; set; }
        public required string Path { get; set; }
        public string? BundleId { get; set; }
        public long AddedUtc { get; set; }
    }

    public sealed class SessionItem
    {
        public long AppId { get; set; }
        public long StartUtc { get; set; }
        public long? EndUtc { get; set; }
        public long LastSeenUtc { get; set; }
        public string? EndReason { get; set; }
    }

    public sealed class ExpectedUptime
    {
        public long HeadlineUptime { get; set; }
        public Dictionary<string, long> DeviceUptimes { get; set; } = [];
        public long OnBothSeconds { get; set; }
    }

    public sealed class DeviceOverlapCase
    {
        public required string Name { get; set; }
        public required string Description { get; set; }
        public required string TimeZoneId { get; set; }
        public long NowUtc { get; set; }
        public string DeviceScope { get; set; } = "All";
        public string? ForgottenDeviceId { get; set; }
        public List<SessionItem> Sessions { get; set; } = [];
        public required ExpectedUptime Expected { get; set; }
    }

    public sealed class DeviceOverlapDoc
    {
        public required string Name { get; set; }
        public required string Description { get; set; }
        public List<DeviceItem> Devices { get; set; } = [];
        public List<AppItem> Apps { get; set; } = [];
        public List<DeviceOverlapCase> Cases { get; set; } = [];
    }

    private static TimeZoneInfo ResolveZone(string zoneId)
    {
        if (zoneId == "UTC") return TimeZoneInfo.Utc;
        try { return TimeZoneInfo.FindSystemTimeZoneById(zoneId); }
        catch
        {
            // Fallback for Windows/IANA mappings
            if (zoneId == "America/New_York")
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); } catch { }
            }
            return TimeZoneInfo.Utc;
        }
    }

    internal static (string DbPath, Action Dispose) CreateTestDb(DeviceOverlapDoc doc, DeviceOverlapCase c)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ts-dev-overlap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "timesink.db");

        using (var db = Db.Open(dbPath))
        {
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            // Populate devices
            foreach (var dev in doc.Devices)
            {
                var state = dev.Id == c.ForgottenDeviceId ? "forgotten" : dev.State;
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO devices (device_id, name, platform, is_local, first_seen_utc, state)
                    VALUES ($id, $name, $platform, $isLocal, $now, $state)
                    ON CONFLICT(device_id) DO UPDATE SET is_local = $isLocal, state = $state;
                    """;
                cmd.Parameters.AddWithValue("$id", dev.Id);
                cmd.Parameters.AddWithValue("$name", dev.Name);
                cmd.Parameters.AddWithValue("$platform", dev.Platform);
                cmd.Parameters.AddWithValue("$isLocal", dev.IsLocal ? 1 : 0);
                cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
                cmd.Parameters.AddWithValue("$state", state);
                cmd.ExecuteNonQuery();

                if (dev.IsLocal)
                {
                    db.SetSetting("device.id", dev.Id);
                    db.SetSetting("device.name", dev.Name);
                }
            }

            // Populate apps
            foreach (var app in doc.Apps)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO apps (id, device_id, exe_path, bundle_id, display_name, added_utc, state)
                    VALUES ($id, $devId, $path, $bundleId, $name, $added, 'tracked');
                    """;
                cmd.Parameters.AddWithValue("$id", app.Id);
                cmd.Parameters.AddWithValue("$devId", app.DeviceId);
                cmd.Parameters.AddWithValue("$path", app.Path);
                cmd.Parameters.AddWithValue("$bundleId", (object?)app.BundleId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$name", app.Name);
                cmd.Parameters.AddWithValue("$added", app.AddedUtc);
                cmd.ExecuteNonQuery();
            }

            // Populate sessions
            foreach (var s in c.Sessions)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO sessions (app_id, start_utc, end_utc, last_seen_utc, end_reason)
                    VALUES ($appId, $start, $end, $lastSeen, $reason);
                    """;
                cmd.Parameters.AddWithValue("$appId", s.AppId);
                cmd.Parameters.AddWithValue("$start", s.StartUtc);
                cmd.Parameters.AddWithValue("$end", (object?)s.EndUtc ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$lastSeen", s.LastSeenUtc);
                cmd.Parameters.AddWithValue("$reason", (object?)s.EndReason ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
        }

        return (dbPath, () =>
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch { }
        });
    }

    [Fact]
    public void Verify_All_Cases_From_Device_Overlap_Fixture()
    {
        var doc = FixtureLoader.Load<DeviceOverlapDoc>("device-overlap.json");
        doc.Cases.Should().HaveCount(5);

        foreach (var c in doc.Cases)
        {
            var (dbPath, cleanup) = CreateTestDb(doc, c);
            try
            {
                var zone = ResolveZone(c.TimeZoneId);
                var now = DateTimeOffset.FromUnixTimeSeconds(c.NowUtc);
                var scope = c.DeviceScope == "This" ? DeviceScope.This : DeviceScope.All;

                var ctx = new UsageContext(now, zone, null, () => null, () => WeekStart.Mon,
                    gamesOnly: false, dbPath: dbPath, deviceScope: scope, localDeviceId: "dev-win");

                var todayResult = TodayQuery.Run(ctx);

                // 1. Headline Uptime matches expected
                todayResult.UptimeSeconds.Should().Be(c.Expected.HeadlineUptime, $"case '{c.Name}' headline uptime mismatch");

                // 2. On both seconds matches expected
                todayResult.OnBothSeconds.Should().Be(c.Expected.OnBothSeconds, $"case '{c.Name}' onBoth mismatch");

                // 3. Headline equation holds: headline = devA + devB - onBoth
                if (todayResult.DeviceUptimes != null && todayResult.DeviceUptimes.Count > 0)
                {
                    var sumDev = todayResult.DeviceUptimes.Values.Sum(d => d.UptimeSeconds);
                    todayResult.UptimeSeconds.Should().Be(sumDev - todayResult.OnBothSeconds,
                        $"case '{c.Name}' headline must equal sum(devices) - onBoth");

                    foreach (var (devId, expectedSecs) in c.Expected.DeviceUptimes)
                    {
                        todayResult.DeviceUptimes.Should().ContainKey(devId, $"case '{c.Name}' missing device {devId}");
                        todayResult.DeviceUptimes[devId].UptimeSeconds.Should().Be(expectedSecs, $"case '{c.Name}' device {devId} uptime mismatch");
                    }
                }
            }
            finally
            {
                cleanup();
            }
        }
    }

    [Fact]
    public void Headline_Reconciles_With_Device_Figures_Across_Every_Query_Result()
    {
        var doc = FixtureLoader.Load<DeviceOverlapDoc>("device-overlap.json");
        var overlapCase = doc.Cases.Single(c => c.Name == "overlap-counted-once");

        var (dbPath, cleanup) = CreateTestDb(doc, overlapCase);
        try
        {
            var zone = TimeZoneInfo.Utc;
            var now = DateTimeOffset.FromUnixTimeSeconds(overlapCase.NowUtc);
            var ctx = new UsageContext(now, zone, null, () => null, () => WeekStart.Mon,
                gamesOnly: false, dbPath: dbPath, deviceScope: DeviceScope.All, localDeviceId: "dev-win");

            // Today
            var today = TodayQuery.Run(ctx);
            today.UptimeSeconds.Should().Be(21600);
            today.OnBothSeconds.Should().Be(7200);
            today.DeviceUptimes.Should().NotBeNull();
            var todaySum = today.DeviceUptimes!.Values.Sum(d => d.UptimeSeconds);
            today.UptimeSeconds.Should().Be(todaySum - today.OnBothSeconds);

            // Week
            var week = WeekQuery.Run(ctx);
            week.TotalSeconds.Should().Be(21600);
            week.OnBothSeconds.Should().Be(7200);
            week.DeviceTotals.Should().NotBeNull();
            var weekSum = week.DeviceTotals!.Values.Sum();
            week.TotalSeconds.Should().Be(weekSum - week.OnBothSeconds);

            // Month
            var month = MonthQuery.Run(ctx, 2026, 3);
            month.TotalSeconds.Should().Be(21600);
            month.OnBothSeconds.Should().Be(7200);
            month.DeviceTotals.Should().NotBeNull();
            var monthSum = month.DeviceTotals!.Values.Sum();
            month.TotalSeconds.Should().Be(monthSum - month.OnBothSeconds);

            // Year
            var year = YearQuery.Run(ctx);
            year.TotalSeconds.Should().Be(21600);
            year.OnBothSeconds.Should().Be(7200);
            year.DeviceTotals.Should().NotBeNull();
            var yearSum = year.DeviceTotals!.Values.Sum();
            year.TotalSeconds.Should().Be(yearSum - year.OnBothSeconds);

            // All
            var all = AllQuery.Run(ctx);
            all.Totals.Seconds.Should().Be(21600);
            all.Totals.OnBothSeconds.Should().Be(7200);
            all.Totals.DeviceTotals.Should().NotBeNull();
            var allSum = all.Totals.DeviceTotals!.Values.Sum();
            all.Totals.Seconds.Should().Be(allSum - all.Totals.OnBothSeconds);
            all.Apps.Should().Contain(a => a.Name == "VS Code Win" && a.Devices != null && a.Devices.Contains("dev-win"));
            all.Apps.Should().Contain(a => a.Name == "VS Code Mac" && a.Devices != null && a.Devices.Contains("dev-mac"));

            // AppDetail
            var appWin = AppDetailQuery.Run(ctx, 1, 2026);
            appWin.Should().NotBeNull();
            appWin!.TotalSeconds.Should().Be(14400);
            appWin.DeviceSeconds.Should().ContainKey("dev-win");
            appWin.DeviceSeconds!["dev-win"].Should().Be(14400);

            var appMac = AppDetailQuery.Run(ctx, 2, 2026);
            appMac.Should().NotBeNull();
            appMac!.TotalSeconds.Should().Be(14400);
            appMac.DeviceSeconds.Should().ContainKey("dev-mac");
            appMac.DeviceSeconds!["dev-mac"].Should().Be(14400);

            // Wrapped
            var wrapped = WrappedQuery.Run(ctx);
            wrapped.TotalSeconds.Should().Be(21600);
            wrapped.OnBothSeconds.Should().Be(7200);
            wrapped.DeviceTotals.Should().NotBeNull();
            var wrappedSum = wrapped.DeviceTotals!.Values.Sum();
            wrapped.TotalSeconds.Should().Be(wrappedSum - wrapped.OnBothSeconds);
        }
        finally
        {
            cleanup();
        }
    }

    [Fact]
    public void Forgotten_Device_Shows_In_All_Scope_And_Hidden_In_This_Scope()
    {
        var doc = FixtureLoader.Load<DeviceOverlapDoc>("device-overlap.json");
        var forgottenCase = doc.Cases.Single(c => c.Name == "forgotten-device");

        var (dbPath, cleanup) = CreateTestDb(doc, forgottenCase);
        try
        {
            var zone = TimeZoneInfo.Utc;
            var now = DateTimeOffset.FromUnixTimeSeconds(forgottenCase.NowUtc);

            // In All Scope: Both devices count
            var allCtx = new UsageContext(now, zone, null, () => null, () => WeekStart.Mon,
                gamesOnly: false, dbPath: dbPath, deviceScope: DeviceScope.All, localDeviceId: "dev-win");

            var allResult = TodayQuery.Run(allCtx);
            allResult.UptimeSeconds.Should().Be(18000);
            allResult.OnBothSeconds.Should().Be(3600);
            allResult.DeviceUptimes.Should().ContainKey("dev-win");
            allResult.DeviceUptimes.Should().ContainKey("dev-mac");
            allResult.UptimeSeconds.Should().Be(allResult.DeviceUptimes!["dev-win"].UptimeSeconds +
                allResult.DeviceUptimes["dev-mac"].UptimeSeconds - allResult.OnBothSeconds);

            // In This Scope: Only local device (dev-win) counts
            var thisCtx = new UsageContext(now, zone, null, () => null, () => WeekStart.Mon,
                gamesOnly: false, dbPath: dbPath, deviceScope: DeviceScope.This, localDeviceId: "dev-win");

            var thisResult = TodayQuery.Run(thisCtx);
            thisResult.UptimeSeconds.Should().Be(10800);
            thisResult.OnBothSeconds.Should().Be(0);
            thisResult.DeviceUptimes.Should().ContainKey("dev-win");
            thisResult.DeviceUptimes.Should().NotContainKey("dev-mac");
        }
        finally
        {
            cleanup();
        }
    }
}
