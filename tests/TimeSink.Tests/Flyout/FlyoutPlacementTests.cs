using TimeSink.Core.Flyout;

namespace TimeSink.Tests.Flyout;

public class FlyoutPlacementTests
{
    private static readonly PxSize Size = new(420, 620);

    // Monitor 1920x1080 at origin; the taskbar (48 px) takes one edge.
    private static readonly PxRect Monitor = new(0, 0, 1920, 1080);

    [Fact]
    public void Bottom_taskbar_puts_flyout_flush_on_the_taskbar_centred_on_the_icon()
    {
        var work = new PxRect(0, 0, 1920, 1032);
        var icon = new PxRect(1600, 1040, 1624, 1064);

        var r = FlyoutPlacement.Compute(icon, Monitor, work, Size);

        r.Should().Be(new PxRect(1612 - 210, 1032 - 620, 1612 + 210, 1032));
    }

    [Fact]
    public void Right_edge_is_clamped_inside_the_work_area()
    {
        var work = new PxRect(0, 0, 1920, 1032);
        var icon = new PxRect(1890, 1040, 1914, 1064);

        var r = FlyoutPlacement.Compute(icon, Monitor, work, Size);

        r.Right.Should().Be(1920);
        r.Left.Should().Be(1500);
        r.Bottom.Should().Be(1032);
    }

    [Fact]
    public void Top_taskbar_places_flyout_flush_below_it()
    {
        var work = new PxRect(0, 48, 1920, 1080);
        var icon = new PxRect(1000, 10, 1024, 34);

        var r = FlyoutPlacement.Compute(icon, Monitor, work, Size);

        r.Top.Should().Be(48);
        r.Bottom.Should().Be(48 + 620);
        r.Left.Should().Be(1012 - 210);
    }

    [Fact]
    public void Left_taskbar_places_flyout_flush_against_it_and_clamps_vertically()
    {
        var work = new PxRect(60, 0, 1920, 1080);
        var icon = new PxRect(18, 1000, 42, 1024);

        var r = FlyoutPlacement.Compute(icon, Monitor, work, Size);

        r.Left.Should().Be(60);
        r.Bottom.Should().Be(1080);
        r.Top.Should().Be(460);
    }

    [Fact]
    public void Right_taskbar_places_flyout_flush_against_it()
    {
        var work = new PxRect(0, 0, 1860, 1080);
        var icon = new PxRect(1878, 500, 1902, 524);

        var r = FlyoutPlacement.Compute(icon, Monitor, work, Size);

        r.Right.Should().Be(1860);
        r.Left.Should().Be(1860 - 420);
        r.Top.Should().Be(512 - 310);
    }

    [Fact]
    public void Second_monitor_with_negative_and_offset_coordinates_is_handled()
    {
        var monitor = new PxRect(1920, -440, 3840, 640);
        var work = new PxRect(1920, -440, 3840, 592);
        var icon = new PxRect(3700, 600, 3724, 624);

        var r = FlyoutPlacement.Compute(icon, monitor, work, Size);

        r.Bottom.Should().Be(592);
        r.Top.Should().Be(-28);
        r.Left.Should().BeGreaterThanOrEqualTo(1920);
        r.Right.Should().BeLessThanOrEqualTo(3840);
    }

    [Fact]
    public void Auto_hidden_taskbar_falls_back_to_the_nearest_monitor_edge()
    {
        var icon = new PxRect(1600, 1056, 1624, 1080);

        var r = FlyoutPlacement.Compute(icon, Monitor, Monitor, Size);

        r.Bottom.Should().Be(1080);
        r.Left.Should().Be(1612 - 210);
    }

    [Fact]
    public void Flyout_larger_than_the_work_area_is_shrunk_to_fit()
    {
        var work = new PxRect(0, 0, 1920, 500);
        var monitor = new PxRect(0, 0, 1920, 548);
        var icon = new PxRect(960, 520, 984, 544);

        var r = FlyoutPlacement.Compute(icon, monitor, work, Size);

        r.Top.Should().Be(0);
        r.Bottom.Should().Be(500);
    }
}
