using TimeSink.Core.Flyout;

namespace TimeSink.Tests.Flyout;

public class FlyoutLaunchArgsTests
{
    [Fact]
    public void Args_round_trip_including_negative_coordinates()
    {
        var original = new FlyoutLaunchArgs(
            new PxRect(3700, 600, 3724, 624),
            new PxRect(1920, -440, 3840, 640),
            new PxRect(1920, -440, 3840, 592),
            144);

        var parsed = FlyoutLaunchArgs.TryParse(original.ToArgs(), out var result);

        parsed.Should().BeTrue();
        result.Should().Be(original);
    }

    [Fact]
    public void Missing_or_garbled_args_are_rejected()
    {
        FlyoutLaunchArgs.TryParse([], out _).Should().BeFalse();
        FlyoutLaunchArgs.TryParse(["--anchor", "1,2,3"], out _).Should().BeFalse();
    }
}
