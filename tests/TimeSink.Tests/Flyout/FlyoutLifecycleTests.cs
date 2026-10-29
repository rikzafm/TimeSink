using TimeSink.Core.Flyout;

namespace TimeSink.Tests.Flyout;

public class FlyoutLifecycleTests
{
    private static readonly DateTime T0 = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

    // ---- tray clicks ----

    [Fact]
    public void Click_with_no_flyout_opens_one_cold()
        => new FlyoutLifecycle().OnTrayClick(T0, uiAlive: false).Should().Be(FlyoutCommand.OpenCold);

    [Fact]
    public void Click_while_the_flyout_is_showing_closes_it()
    {
        var flyout = new FlyoutLifecycle();
        flyout.OnTrayClick(T0, uiAlive: false);

        flyout.OnTrayClick(T0.AddSeconds(5), uiAlive: true).Should().Be(FlyoutCommand.Close);
    }

    [Fact]
    public void Click_on_a_standby_ui_reveals_it_and_the_next_click_closes_it()
    {
        var flyout = new FlyoutLifecycle();
        flyout.OnTrayHover(T0, uiPresent: false);

        flyout.OnTrayClick(T0.AddSeconds(2), uiAlive: true).Should().Be(FlyoutCommand.RevealStandby);
        flyout.OnTrayClick(T0.AddSeconds(4), uiAlive: true).Should().Be(FlyoutCommand.Close);
    }

    [Fact]
    public void Click_right_after_the_flyout_closed_itself_on_deactivate_is_ignored()
    {
        var flyout = new FlyoutLifecycle();
        flyout.OnUiExited(T0, instantOpen: false);

        flyout.OnTrayClick(T0.AddMilliseconds(120), uiAlive: false).Should().Be(FlyoutCommand.None);
    }

    [Fact]
    public void Click_after_the_debounce_window_opens_again()
    {
        var flyout = new FlyoutLifecycle();
        flyout.OnUiExited(T0, instantOpen: false);

        flyout.OnTrayClick(T0.AddMilliseconds(301), uiAlive: false).Should().Be(FlyoutCommand.OpenCold);
    }

    [Fact]
    public void Rapid_clicking_never_opens_twice_in_the_debounce_window()
    {
        var flyout = new FlyoutLifecycle();
        flyout.OnTrayClick(T0, uiAlive: false).Should().Be(FlyoutCommand.OpenCold);
        flyout.OnUiExited(T0.AddMilliseconds(50), instantOpen: false);

        flyout.OnTrayClick(T0.AddMilliseconds(60), uiAlive: false).Should().Be(FlyoutCommand.None);
        flyout.OnTrayClick(T0.AddMilliseconds(150), uiAlive: false).Should().Be(FlyoutCommand.None);
    }

    [Fact]
    public void The_debounce_also_covers_a_kept_standby_that_started_after_the_exit()
    {
        var flyout = new FlyoutLifecycle();
        flyout.OnUiExited(T0, instantOpen: true);

        flyout.OnTrayClick(T0.AddMilliseconds(100), uiAlive: true).Should().Be(FlyoutCommand.None);
        flyout.OnTrayClick(T0.AddMilliseconds(400), uiAlive: true).Should().Be(FlyoutCommand.RevealStandby);
    }

    [Fact]
    public void A_ui_that_exits_is_no_longer_shown_so_the_next_click_opens_cold()
    {
        var flyout = new FlyoutLifecycle();
        flyout.OnTrayClick(T0, uiAlive: false);
        flyout.OnUiExited(T0.AddSeconds(3), instantOpen: false);

        flyout.Shown.Should().BeFalse();
        flyout.OnTrayClick(T0.AddSeconds(4), uiAlive: false).Should().Be(FlyoutCommand.OpenCold);
    }

    [Fact]
    public void A_ui_that_could_not_be_started_is_not_shown()
    {
        var flyout = new FlyoutLifecycle();
        flyout.OnTrayClick(T0, uiAlive: false).Should().Be(FlyoutCommand.OpenCold);
        flyout.OnStartFailed();

        flyout.Shown.Should().BeFalse();
        flyout.OnTrayClick(T0.AddSeconds(1), uiAlive: false).Should().Be(FlyoutCommand.OpenCold);
    }

    // ---- hover ----

    [Fact]
    public void Hover_starts_a_standby_ui_unless_one_is_already_there()
    {
        new FlyoutLifecycle().OnTrayHover(T0, uiPresent: false).Should().Be(FlyoutCommand.StartStandby);
        new FlyoutLifecycle().OnTrayHover(T0, uiPresent: true).Should().Be(FlyoutCommand.None);
    }

    [Fact]
    public void Hover_starts_a_standby_ui_at_most_once_per_cooldown()
    {
        var flyout = new FlyoutLifecycle();
        flyout.OnTrayHover(T0, uiPresent: false).Should().Be(FlyoutCommand.StartStandby);

        flyout.OnTrayHover(T0.AddSeconds(14), uiPresent: false).Should().Be(FlyoutCommand.None);
        flyout.OnTrayHover(T0.AddSeconds(15), uiPresent: false).Should().Be(FlyoutCommand.StartStandby);
    }

    // ---- ui.instantOpen ----

    [Fact]
    public void With_instant_open_on_an_exit_starts_a_kept_standby_ui()
        => new FlyoutLifecycle().OnUiExited(T0, instantOpen: true).Should().Be(FlyoutCommand.StartKeptStandby);

    [Fact]
    public void With_instant_open_off_an_exit_starts_nothing()
        => new FlyoutLifecycle().OnUiExited(T0, instantOpen: false).Should().Be(FlyoutCommand.None);

    [Fact]
    public void Nothing_starts_a_kept_standby_ui_until_a_ui_has_exited_so_the_first_click_is_a_cold_open()
    {
        var flyout = new FlyoutLifecycle();

        flyout.Shown.Should().BeFalse();
        flyout.OnTrayClick(T0, uiAlive: false).Should().Be(FlyoutCommand.OpenCold);
    }

    [Fact]
    public void A_kept_standby_counts_as_a_standby_start_for_the_hover_cooldown()
    {
        var flyout = new FlyoutLifecycle();
        flyout.OnUiExited(T0, instantOpen: true);

        flyout.OnTrayHover(T0.AddSeconds(5), uiPresent: false).Should().Be(FlyoutCommand.None);
        flyout.OnTrayHover(T0.AddSeconds(16), uiPresent: false).Should().Be(FlyoutCommand.StartStandby);
    }
}
