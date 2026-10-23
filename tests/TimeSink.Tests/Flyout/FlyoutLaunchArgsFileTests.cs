using TimeSink.Core.Flyout;

namespace TimeSink.Tests.Flyout;

public class FlyoutLaunchArgsFileTests
{
    [Fact]
    public void Launch_args_round_trip_through_the_handoff_file_text()
    {
        var args = new FlyoutLaunchArgs(new PxRect(1800, 1040, 1824, 1080), new PxRect(0, 0, 1920, 1080), new PxRect(0, 0, 1920, 1040), 144);

        FlyoutLaunchArgs.TryParseFileText(args.ToFileText(), out var parsed).Should().BeTrue();

        parsed.Should().Be(args);
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("--anchor\n1,2,3,4")]
    public void Malformed_handoff_text_is_rejected(string text)
        => FlyoutLaunchArgs.TryParseFileText(text, out _).Should().BeFalse();
}
