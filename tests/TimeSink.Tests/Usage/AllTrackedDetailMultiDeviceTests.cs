using FluentAssertions;
using TimeSink.Core.Devices;
using TimeSink.Core.Usage;
using TimeSink.Tests.Tracking;
using Xunit;

namespace TimeSink.Tests.Usage;

public sealed class AllTrackedDetailMultiDeviceTests
{
    private static DeviceOverlapFixtureTests.DeviceOverlapDoc LoadFixture() =>
        FixtureLoader.Load<DeviceOverlapFixtureTests.DeviceOverlapDoc>("device-overlap.json");

    [Fact]
    public void AllQuery_InThisScope_HidesRemoteOnlyApps_AndInAllScope_ShowsAllWithDeviceList()
    {
        var doc = LoadFixture();
        var c = doc.Cases.First(x => x.Name == "overlap-counted-once");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(c.TimeZoneId);

        var (dbPath, cleanup) = DeviceOverlapFixtureTests.CreateTestDb(doc, c);
        try
        {
            var localDevId = "dev-win";
            var now = DateTimeOffset.FromUnixTimeSeconds(c.NowUtc);

            // This scope: only local apps (VS Code Win), remote app (VS Code Mac) is hidden
            var ctxThis = new UsageContext(now, zone, null, () => null, () => WeekStart.Mon,
                gamesOnly: false, dbPath: dbPath, deviceScope: DeviceScope.This, localDeviceId: localDevId);
            var allThis = AllQuery.Run(ctxThis);

            allThis.Apps.Should().ContainSingle();
            allThis.Apps[0].Name.Should().Be("VS Code Win");
            allThis.Apps[0].Devices.Should().ContainSingle().Which.Should().Be(localDevId);
            allThis.Totals.DeviceTotals.Should().ContainSingle().Which.Key.Should().Be(localDevId);
            allThis.Totals.OnBothSeconds.Should().Be(0);

            // All scope: both apps are shown
            var ctxAll = new UsageContext(now, zone, null, () => null, () => WeekStart.Mon,
                gamesOnly: false, dbPath: dbPath, deviceScope: DeviceScope.All, localDeviceId: localDevId);
            var allAll = AllQuery.Run(ctxAll);

            allAll.Apps.Should().HaveCount(2);
            allAll.Totals.DeviceTotals.Should().NotBeNull();
            allAll.Totals.DeviceTotals!.Count.Should().Be(2);

            var winApp = allAll.Apps.First(a => a.Name == "VS Code Win");
            winApp.Devices.Should().ContainSingle().Which.Should().Be(localDevId);

            var macApp = allAll.Apps.First(a => a.Name == "VS Code Mac");
            macApp.Devices.Should().ContainSingle().Which.Should().Be("dev-mac");
        }
        finally
        {
            cleanup();
        }
    }

    [Fact]
    public void AppDetailQuery_InThisScope_CalculatesForLocalDevice_AndInAllScope_CalculatesPerDeviceBreakdown()
    {
        var doc = LoadFixture();
        var c = doc.Cases.First(x => x.Name == "overlap-counted-once");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(c.TimeZoneId);

        var (dbPath, cleanup) = DeviceOverlapFixtureTests.CreateTestDb(doc, c);
        try
        {
            var localDevId = "dev-win";
            var now = DateTimeOffset.FromUnixTimeSeconds(c.NowUtc);

            // Merge app 2 into app 1 to form a cross-device merged app
            using (var lib = TimeSink.Core.Apps.AppLibrary.Open(dbPath))
            {
                lib.Merge(2, 1);
            }

            // App detail in All scope: merged app has time on both devices
            var ctxAll = new UsageContext(now, zone, null, () => null, () => WeekStart.Mon,
                gamesOnly: false, dbPath: dbPath, deviceScope: DeviceScope.All, localDeviceId: localDevId);
            var detailAll = AppDetailQuery.Run(ctxAll, 1, now.Year);
            detailAll.Should().NotBeNull();
            detailAll!.DeviceSeconds.Should().NotBeNull();
            detailAll.DeviceSeconds!.Count.Should().Be(2);
            detailAll.DeviceSeconds.Should().ContainKey(localDevId);
            detailAll.DeviceSeconds.Should().ContainKey("dev-mac");
            (detailAll.DeviceSeconds[localDevId] + detailAll.DeviceSeconds["dev-mac"]).Should().Be(detailAll.TotalSeconds);

            // App detail in This scope: only local device time
            var ctxThis = new UsageContext(now, zone, null, () => null, () => WeekStart.Mon,
                gamesOnly: false, dbPath: dbPath, deviceScope: DeviceScope.This, localDeviceId: localDevId);
            var detailThis = AppDetailQuery.Run(ctxThis, 1, now.Year);
            detailThis.Should().NotBeNull();
            detailThis!.TotalSeconds.Should().Be(detailAll.DeviceSeconds[localDevId]);
        }
        finally
        {
            cleanup();
        }
    }
}
