using FluentAssertions;
using TimeSink.Core.Devices;
using TimeSink.Core.Usage;
using TimeSink.Tests.Tracking;
using Xunit;

namespace TimeSink.Tests.Usage;

public sealed class PeriodMultiDeviceTests
{
    private static DeviceOverlapFixtureTests.DeviceOverlapDoc LoadFixture() =>
        FixtureLoader.Load<DeviceOverlapFixtureTests.DeviceOverlapDoc>("device-overlap.json");

    [Fact]
    public void WeekQuery_ReconcilesHeadlineWithDeviceTotalsAndOverlap()
    {
        var doc = LoadFixture();
        var c = doc.Cases.First(x => x.Name == "overlap-counted-once");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(c.TimeZoneId);

        var (dbPath, cleanup) = DeviceOverlapFixtureTests.CreateTestDb(doc, c);
        try
        {
            var localDevId = "dev-win";
            var now = DateTimeOffset.FromUnixTimeSeconds(c.NowUtc);

            // This scope
            var ctxThis = new UsageContext(now, zone, null, () => null, () => WeekStart.Mon,
                gamesOnly: false, dbPath: dbPath, deviceScope: DeviceScope.This, localDeviceId: localDevId);
            var weekThis = WeekQuery.Run(ctxThis, 0);

            weekThis.DeviceTotals.Should().ContainSingle().Which.Key.Should().Be(localDevId);
            weekThis.OnBothSeconds.Should().Be(0);
            weekThis.TotalSeconds.Should().Be(weekThis.DeviceTotals.Values.Sum());

            // All scope
            var ctxAll = new UsageContext(now, zone, null, () => null, () => WeekStart.Mon,
                gamesOnly: false, dbPath: dbPath, deviceScope: DeviceScope.All, localDeviceId: localDevId);
            var weekAll = WeekQuery.Run(ctxAll, 0);

            weekAll.DeviceTotals.Should().NotBeNull();
            weekAll.DeviceTotals!.Count.Should().Be(2);
            weekAll.OnBothSeconds.Should().BeGreaterThan(0);

            // Headline reconciliation: Total == devA + devB - onBoth
            var sumDevices = weekAll.DeviceTotals.Values.Sum();
            (sumDevices - weekAll.OnBothSeconds).Should().Be(weekAll.TotalSeconds);
        }
        finally
        {
            cleanup();
        }
    }

    [Fact]
    public void MonthQuery_ReconcilesHeadlineWithDeviceTotalsAndOverlap()
    {
        var doc = LoadFixture();
        var c = doc.Cases.First(x => x.Name == "overlap-counted-once");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(c.TimeZoneId);

        var (dbPath, cleanup) = DeviceOverlapFixtureTests.CreateTestDb(doc, c);
        try
        {
            var localDevId = "dev-win";
            var now = DateTimeOffset.FromUnixTimeSeconds(c.NowUtc);
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);

            // This scope
            var ctxThis = new UsageContext(now, zone, null, () => null, () => WeekStart.Mon,
                gamesOnly: false, dbPath: dbPath, deviceScope: DeviceScope.This, localDeviceId: localDevId);
            var monthThis = MonthQuery.Run(ctxThis, today.Year, today.Month);

            monthThis.DeviceTotals.Should().ContainSingle().Which.Key.Should().Be(localDevId);
            monthThis.OnBothSeconds.Should().Be(0);
            monthThis.TotalSeconds.Should().Be(monthThis.DeviceTotals.Values.Sum());

            // All scope
            var ctxAll = new UsageContext(now, zone, null, () => null, () => WeekStart.Mon,
                gamesOnly: false, dbPath: dbPath, deviceScope: DeviceScope.All, localDeviceId: localDevId);
            var monthAll = MonthQuery.Run(ctxAll, today.Year, today.Month);

            monthAll.DeviceTotals.Should().NotBeNull();
            monthAll.DeviceTotals!.Count.Should().Be(2);
            monthAll.OnBothSeconds.Should().BeGreaterThan(0);

            // Headline reconciliation: Total == devA + devB - onBoth
            var sumDevices = monthAll.DeviceTotals.Values.Sum();
            (sumDevices - monthAll.OnBothSeconds).Should().Be(monthAll.TotalSeconds);
        }
        finally
        {
            cleanup();
        }
    }

    [Fact]
    public void YearQuery_ReconcilesHeadlineWithDeviceTotalsAndOverlap()
    {
        var doc = LoadFixture();
        var c = doc.Cases.First(x => x.Name == "overlap-counted-once");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(c.TimeZoneId);

        var (dbPath, cleanup) = DeviceOverlapFixtureTests.CreateTestDb(doc, c);
        try
        {
            var localDevId = "dev-win";
            var now = DateTimeOffset.FromUnixTimeSeconds(c.NowUtc);
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);

            // This scope
            var ctxThis = new UsageContext(now, zone, null, () => null, () => WeekStart.Mon,
                gamesOnly: false, dbPath: dbPath, deviceScope: DeviceScope.This, localDeviceId: localDevId);
            var yearThis = YearQuery.Run(ctxThis);

            yearThis.DeviceTotals.Should().ContainSingle().Which.Key.Should().Be(localDevId);
            yearThis.OnBothSeconds.Should().Be(0);
            yearThis.TotalSeconds.Should().Be(yearThis.DeviceTotals.Values.Sum());

            // All scope
            var ctxAll = new UsageContext(now, zone, null, () => null, () => WeekStart.Mon,
                gamesOnly: false, dbPath: dbPath, deviceScope: DeviceScope.All, localDeviceId: localDevId);
            var yearAll = YearQuery.Run(ctxAll);

            yearAll.DeviceTotals.Should().NotBeNull();
            yearAll.DeviceTotals!.Count.Should().Be(2);
            yearAll.OnBothSeconds.Should().BeGreaterThan(0);

            // Headline reconciliation: Total == devA + devB - onBoth
            var sumDevices = yearAll.DeviceTotals.Values.Sum();
            (sumDevices - yearAll.OnBothSeconds).Should().Be(yearAll.TotalSeconds);
        }
        finally
        {
            cleanup();
        }
    }

    [Fact]
    public void WrappedQuery_ReconcilesHeadlineWithDeviceTotalsAndOverlap()
    {
        var doc = LoadFixture();
        var c = doc.Cases.First(x => x.Name == "overlap-counted-once");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(c.TimeZoneId);

        var (dbPath, cleanup) = DeviceOverlapFixtureTests.CreateTestDb(doc, c);
        try
        {
            var localDevId = "dev-win";
            var now = DateTimeOffset.FromUnixTimeSeconds(c.NowUtc);
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);

            // This scope
            var ctxThis = new UsageContext(now, zone, null, () => null, () => WeekStart.Mon,
                gamesOnly: false, dbPath: dbPath, deviceScope: DeviceScope.This, localDeviceId: localDevId);
            var wrappedThis = WrappedQuery.Run(ctxThis);

            wrappedThis.DeviceTotals.Should().ContainSingle().Which.Key.Should().Be(localDevId);
            wrappedThis.OnBothSeconds.Should().Be(0);
            wrappedThis.TotalSeconds.Should().Be(wrappedThis.DeviceTotals.Values.Sum());

            // All scope
            var ctxAll = new UsageContext(now, zone, null, () => null, () => WeekStart.Mon,
                gamesOnly: false, dbPath: dbPath, deviceScope: DeviceScope.All, localDeviceId: localDevId);
            var wrappedAll = WrappedQuery.Run(ctxAll);

            wrappedAll.DeviceTotals.Should().NotBeNull();
            wrappedAll.DeviceTotals!.Count.Should().Be(2);
            wrappedAll.OnBothSeconds.Should().BeGreaterThan(0);

            // Headline reconciliation: Total == devA + devB - onBoth
            var sumDevices = wrappedAll.DeviceTotals.Values.Sum();
            (sumDevices - wrappedAll.OnBothSeconds).Should().Be(wrappedAll.TotalSeconds);
        }
        finally
        {
            cleanup();
        }
    }
}
