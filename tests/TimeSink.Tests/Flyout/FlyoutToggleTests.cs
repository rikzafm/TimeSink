using TimeSink.Core.Flyout;

namespace TimeSink.Tests.Flyout;

public class FlyoutToggleTests
{
    private static readonly DateTime T0 = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Click_with_no_flyout_opens_it()
    {
        var gate = new FlyoutToggle();
        gate.OnTrayClick(T0, uiRunning: false).Should().Be(FlyoutAction.Open);
    }

    [Fact]
    public void Click_while_open_closes_it()
    {
        var gate = new FlyoutToggle();
        gate.OnTrayClick(T0, uiRunning: true).Should().Be(FlyoutAction.Close);
    }

    [Fact]
    public void Click_right_after_the_flyout_closed_itself_on_deactivate_is_ignored()
    {
        var gate = new FlyoutToggle();
        gate.OnUiExited(T0);
        gate.OnTrayClick(T0.AddMilliseconds(120), uiRunning: false).Should().Be(FlyoutAction.Ignore);
    }

    [Fact]
    public void Click_after_the_debounce_window_opens_again()
    {
        var gate = new FlyoutToggle();
        gate.OnUiExited(T0);
        gate.OnTrayClick(T0.AddMilliseconds(301), uiRunning: false).Should().Be(FlyoutAction.Open);
    }

    [Fact]
    public void Rapid_clicking_never_opens_twice_in_the_debounce_window()
    {
        var gate = new FlyoutToggle();
        gate.OnTrayClick(T0, false).Should().Be(FlyoutAction.Open);
        gate.OnUiExited(T0.AddMilliseconds(50));
        gate.OnTrayClick(T0.AddMilliseconds(60), false).Should().Be(FlyoutAction.Ignore);
        gate.OnTrayClick(T0.AddMilliseconds(150), false).Should().Be(FlyoutAction.Ignore);
    }
}
