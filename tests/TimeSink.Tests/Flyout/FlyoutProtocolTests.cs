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
        FlyoutProtocol.ParentPidArg.Should().Be("--parent-pid");
        FlyoutProtocol.HandoffPath.Should().EndWith(Path.Combine("TimeSink", "flyout.args"));
    }

    [Fact]
    public void A_standby_command_line_says_keep_only_when_asked()
    {
        FlyoutProtocol.StandbyArgs(keep: false).Should().Equal("--standby");
        FlyoutProtocol.StandbyArgs(keep: true).Should().Equal("--standby", "--keep");
    }

    [Fact]
    public void Parent_pid_is_extracted_when_present()
    {
        FlyoutProtocol.TryGetParentPid(["--standby", "--parent-pid", "1234"]).Should().Be(1234);
        FlyoutProtocol.TryGetParentPid(["--parent-pid", "42"]).Should().Be(42);
        FlyoutProtocol.TryGetParentPid(["--standby"]).Should().BeNull();
        FlyoutProtocol.TryGetParentPid(["--parent-pid"]).Should().BeNull();
        FlyoutProtocol.TryGetParentPid(["--parent-pid", "not-a-number"]).Should().BeNull();
        FlyoutProtocol.TryGetParentPid(["--parent-pid", "-5"]).Should().BeNull();
        FlyoutProtocol.TryGetParentPid([]).Should().BeNull();
    }
}
