using TimeSink.Core.Flyout;

namespace TimeSink.Tests.Flyout;

public class FlyoutProtocolTests
{
    [Fact]
    public void The_names_both_processes_use_are_the_ones_installed_builds_already_use()
    {
        FlyoutProtocol.CloseEventName.Should().Be(@"Local\TimeSink.UI.Close");
        FlyoutProtocol.ShowEventName.Should().Be(@"Local\TimeSink.UI.Show");
        FlyoutProtocol.StandbyArg.Should().Be("--standby");
        FlyoutProtocol.KeepArg.Should().Be("--keep");
        FlyoutProtocol.HandoffPath.Should().EndWith(Path.Combine("TimeSink", "flyout.args"));
    }

    [Fact]
    public void A_standby_command_line_says_keep_only_when_asked()
    {
        FlyoutProtocol.StandbyArgs(keep: false).Should().Equal("--standby");
        FlyoutProtocol.StandbyArgs(keep: true).Should().Equal("--standby", "--keep");
    }
}
