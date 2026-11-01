using FluentAssertions;
using TimeSink.Core.Devices;
using TimeSink.Core.Usage;
using TimeSink.Tests.Tracking;
using Xunit;

namespace TimeSink.Tests.Usage;

public sealed class TodayMultiDeviceTests
{
    private static DeviceOverlapFixtureTests.DeviceOverlapDoc LoadFixture() =>
        FixtureLoader.Load<DeviceOverlapFixtureTests.DeviceOverlapDoc>("device-overlap.json");

    [Fact]
    public void Today_InThisScope_FiltersRemoteDeviceAndMaintainsSingleDeviceContract()
    {
        var doc = LoadFixture();
        var c = doc.Cases.First(x => x.Name == "overlap-counted-once");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(c.TimeZoneId);

        var (dbPath, cleanup) = DeviceOverlapFixtureTests.CreateTestDb(doc, c);
        try
        {
            var localDevId = "dev-win";
            var now = DateTimeOffset.FromUnixTimeSeconds(c.NowUtc);
            var ctxThis = new UsageContext(now, zone, null, () => null, () => WeekStart.Mon,
                gamesOnly: false, dbPath: dbPath, deviceScope: DeviceScope.This, localDeviceId: localDevId);

            var todayThis = TodayQuery.Run(ctxThis);

            // In This scope, uptime reflects only the local machine (dev-win)
            todayThis.UptimeSeconds.Should().Be(c.Expected.DeviceUptimes[localDevId]);
            todayThis.OnBothSeconds.Should().Be(0);
            todayThis.DeviceUptimes.Should().ContainSingle().Which.Key.Should().Be(localDevId);
            // Apps should only include local apps
            todayThis.Apps.Should().OnlyContain(a => a.Sessions.All(s => s.DeviceId == localDevId));
        }
        finally
        {
            cleanup();
        }
    }

    [Fact]
    public void Today_InAllScope_CalculatesUnionUptimeAndPerDeviceBreakdown()
    {
        var doc = LoadFixture();
        var c = doc.Cases.First(x => x.Name == "overlap-counted-once");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(c.TimeZoneId);

        var (dbPath, cleanup) = DeviceOverlapFixtureTests.CreateTestDb(doc, c);
        try
        {
            var localDevId = "dev-win";
            var now = DateTimeOffset.FromUnixTimeSeconds(c.NowUtc);
            var ctxAll = new UsageContext(now, zone, null, () => null, () => WeekStart.Mon,
                gamesOnly: false, dbPath: dbPath, deviceScope: DeviceScope.All, localDeviceId: localDevId);

            var todayAll = TodayQuery.Run(ctxAll);

            // Headline uptime is union uptime with overlap counted once
            todayAll.UptimeSeconds.Should().Be(c.Expected.HeadlineUptime);
            todayAll.OnBothSeconds.Should().Be(c.Expected.OnBothSeconds);

            // Device uptimes breakdown contains each device
            todayAll.DeviceUptimes.Should().NotBeNull();
            foreach (var (devId, expectedUptime) in c.Expected.DeviceUptimes)
            {
                todayAll.DeviceUptimes.Should().ContainKey(devId);
                todayAll.DeviceUptimes![devId].UptimeSeconds.Should().Be(expectedUptime);
            }

            // Sessions track device ID
            todayAll.Apps.SelectMany(a => a.Sessions).Should().Contain(s => s.DeviceId != "");

            // Hourly breakdown has device breakdown
            todayAll.Hours.Should().Contain(h => h.ByDevice != null && h.ByDevice.Count > 0);
        }
        finally
        {
            cleanup();
        }
    }
}
