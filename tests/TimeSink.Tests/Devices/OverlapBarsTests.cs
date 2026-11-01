using TimeSink.Core.Devices;

namespace TimeSink.Tests.Devices;

public sealed class OverlapBarsTests
{
    [Fact]
    public void Layout_BothDevices_ShorterA_PutsAInFrontAndOverlap()
    {
        var (back, front) = OverlapBars.Layout(columnX: 10, columnWidth: 100, heightA: 40, heightB: 80);

        // Back bar is Device B (taller)
        back.IsDeviceA.Should().BeFalse();
        back.InFront.Should().BeFalse();
        back.Height.Should().Be(80);
        back.Width.Should().Be(66);
        back.X.Should().Be(44); // 10 + 100 - 66

        // Front bar is Device A (shorter)
        front.IsDeviceA.Should().BeTrue();
        front.InFront.Should().BeTrue();
        front.Height.Should().Be(40);
        front.Width.Should().Be(66);
        front.X.Should().Be(10);
    }

    [Fact]
    public void Layout_BothDevices_ShorterB_PutsBInFrontAndOverlap()
    {
        var (back, front) = OverlapBars.Layout(columnX: 0, columnWidth: 50, heightA: 90, heightB: 30);

        // Back bar is Device A (taller)
        back.IsDeviceA.Should().BeTrue();
        back.InFront.Should().BeFalse();
        back.Height.Should().Be(90);
        back.Width.Should().Be(33); // 50 * 0.66
        back.X.Should().Be(0);

        // Front bar is Device B (shorter)
        front.IsDeviceA.Should().BeFalse();
        front.InFront.Should().BeTrue();
        front.Height.Should().Be(30);
        front.Width.Should().Be(33);
        front.X.Should().Be(17); // 0 + 50 - 33
    }

    [Fact]
    public void Layout_BothDevices_EqualHeights_DeterministicDrawOrder()
    {
        var (back, front) = OverlapBars.Layout(columnX: 20, columnWidth: 80, heightA: 50, heightB: 50);

        back.Height.Should().Be(50);
        front.Height.Should().Be(50);
        back.InFront.Should().BeFalse();
        front.InFront.Should().BeTrue();
        back.Width.Should().Be(80 * 0.66);
        front.Width.Should().Be(80 * 0.66);
    }

    [Fact]
    public void Layout_OneDevice_DeviceAOnly_TakesFullColumnWidth()
    {
        var (empty, solo) = OverlapBars.Layout(columnX: 15, columnWidth: 60, heightA: 45, heightB: 0);

        solo.IsDeviceA.Should().BeTrue();
        solo.InFront.Should().BeTrue();
        solo.Width.Should().Be(60);
        solo.Height.Should().Be(45);
        solo.X.Should().Be(15);

        empty.Width.Should().Be(0);
        empty.Height.Should().Be(0);
    }

    [Fact]
    public void Layout_OneDevice_DeviceBOnly_TakesFullColumnWidth()
    {
        var (empty, solo) = OverlapBars.Layout(columnX: 5, columnWidth: 70, heightA: 0, heightB: 55);

        solo.IsDeviceA.Should().BeFalse();
        solo.InFront.Should().BeTrue();
        solo.Width.Should().Be(70);
        solo.Height.Should().Be(55);
        solo.X.Should().Be(5);

        empty.Width.Should().Be(0);
        empty.Height.Should().Be(0);
    }

    [Fact]
    public void Layout_ZeroHeights_ProducesZeroWidthAndHeight()
    {
        var (first, second) = OverlapBars.Layout(columnX: 10, columnWidth: 50, heightA: 0, heightB: 0);

        first.Width.Should().Be(0);
        first.Height.Should().Be(0);
        second.Width.Should().Be(0);
        second.Height.Should().Be(0);
    }

    [Fact]
    public void Layout_NegativeHeights_TreatedAsZero()
    {
        var (first, second) = OverlapBars.Layout(columnX: 10, columnWidth: 50, heightA: -10, heightB: -5);

        first.Width.Should().Be(0);
        first.Height.Should().Be(0);
        second.Width.Should().Be(0);
        second.Height.Should().Be(0);
    }
}
