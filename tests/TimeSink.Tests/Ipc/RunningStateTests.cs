using TimeSink.Core.Ipc;
using TimeSink.Core.Tracking;

namespace TimeSink.Tests.Ipc;

public class RunningStateTests
{
    private static SessionRecord Open(long appId, long startUtc) => new(appId * 10, appId, startUtc, null, startUtc, null, null, null);

    [Fact]
    public void The_same_apps_in_a_different_order_are_no_change()
    {
        var current = new StateMessage(false, [new RunningApp(1, 100), new RunningApp(2, 200)]);

        RunningState.Next(current, [Open(2, 200), Open(1, 100)]).Should().BeNull();
    }

    [Fact]
    public void A_new_app_is_a_change_and_the_list_is_ordered_by_app_id()
    {
        var current = new StateMessage(false, [new RunningApp(1, 100)]);

        var next = RunningState.Next(current, [Open(3, 300), Open(1, 100)]);

        next.Should().NotBeNull();
        next!.Running.Should().Equal(new RunningApp(1, 100), new RunningApp(3, 300));
        next.Paused.Should().BeFalse();
    }

    [Fact]
    public void A_restarted_session_of_the_same_app_is_a_change()
    {
        var current = new StateMessage(false, [new RunningApp(1, 100)]);

        RunningState.Next(current, [Open(1, 150)]).Should().NotBeNull();
    }

    [Fact]
    public void Nothing_running_after_nothing_running_is_no_change_and_the_last_app_closing_is()
    {
        RunningState.Next(new StateMessage(false, []), []).Should().BeNull();
        RunningState.Next(new StateMessage(false, [new RunningApp(1, 100)]), []).Should().NotBeNull();
    }
}
