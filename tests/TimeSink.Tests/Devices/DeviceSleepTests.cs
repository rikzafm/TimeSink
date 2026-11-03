using FluentAssertions;
using TimeSink.Core.Usage;
using Xunit;

namespace TimeSink.Tests.Devices;

public class DeviceSleepTests
{
    [Fact]
    public void SleepBands_preserves_DeviceId_for_each_device()
    {
        var d0 = 1000L;
        var now = 5000L;
        var segs = new List<UsageSeg>
        {
            // PC sleeps from 1500 to 2500
            new(1, 1, 1200, 1500, Open: true, Sleep: true, DeviceId: "pc-1", Off: false),
            new(1, 1, 2500, 3000, Open: true, Sleep: false, DeviceId: "pc-1", Off: false),

            // Mac sleeps from 2000 to 4000
            new(2, 2, 1800, 2000, Open: true, Sleep: true, DeviceId: "mac-1", Off: false),
            new(2, 2, 4000, 4500, Open: true, Sleep: false, DeviceId: "mac-1", Off: false),
        };

        var bands = UsageUptime.SleepBands(segs, d0, now, liveDeviceId: null);

        bands.Should().HaveCount(2);

        var pcBand = bands.Single(b => b.DeviceId == "pc-1");
        pcBand.StartUtc.Should().Be(1500);
        pcBand.EndUtc.Should().Be(2500);

        var macBand = bands.Single(b => b.DeviceId == "mac-1");
        macBand.StartUtc.Should().Be(2000);
        macBand.EndUtc.Should().Be(4000);
    }

    [Fact]
    public void SleepBands_for_live_device_extends_to_now()
    {
        var d0 = 1000L;
        var now = 5000L;
        var segs = new List<UsageSeg>
        {
            // PC went to sleep at 3000 and is currently sleeping (live device)
            new(1, 1, 2000, 3000, Open: true, Sleep: true, DeviceId: "pc-live", Off: false),
        };

        var bands = UsageUptime.SleepBands(segs, d0, now, liveDeviceId: "pc-live");

        bands.Should().ContainSingle();
        bands[0].DeviceId.Should().Be("pc-live");
        bands[0].StartUtc.Should().Be(3000);
        bands[0].EndUtc.Should().Be(now);
    }

    [Fact]
    public void SleepBands_groups_sleep_per_device_without_cross_device_merge()
    {
        var d0 = 1000L;
        var now = 6000L;
        var segs = new List<UsageSeg>
        {
            // Device A: 1200-2000 sleep
            new(1, 1, 1000, 1200, Open: true, Sleep: true, DeviceId: "dev-a", Off: false),
            new(1, 1, 2000, 2500, Open: true, Sleep: false, DeviceId: "dev-a", Off: false),

            // Device B: 1100-3000 sleep (overlaps A in time, but distinct device)
            new(2, 2, 1000, 1100, Open: true, Sleep: true, DeviceId: "dev-b", Off: false),
            new(2, 2, 3000, 3500, Open: true, Sleep: false, DeviceId: "dev-b", Off: false),
        };

        var bands = UsageUptime.SleepBands(segs, d0, now);

        bands.Should().HaveCount(2);
        var bandA = bands.Single(b => b.DeviceId == "dev-a");
        bandA.StartUtc.Should().Be(1200);
        bandA.EndUtc.Should().Be(2000);

        var bandB = bands.Single(b => b.DeviceId == "dev-b");
        bandB.StartUtc.Should().Be(1100);
        bandB.EndUtc.Should().Be(3000);
    }
}
