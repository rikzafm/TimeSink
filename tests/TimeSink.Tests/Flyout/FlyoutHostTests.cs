using TimeSink.Core.Flyout;

namespace TimeSink.Tests.Flyout;

public class FlyoutHostTests
{
    private static readonly DateTime T0 = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
    private static readonly FlyoutLaunchArgs Geometry = new(new PxRect(1, 2, 3, 4), new PxRect(0, 0, 1920, 1080), new PxRect(0, 0, 1920, 1040), 144);

    private sealed class FakeUi : IUiProcess
    {
        public bool HasExited { get; set; }
        public string[] Args { get; init; } = [];
        public Action<IUiProcess> OnExited { get; init; } = _ => { };
        public void Exit() { HasExited = true; OnExited(this); }
    }

    private sealed class FakePort : IFlyoutPort
    {
        public List<string> Calls { get; } = [];
        public List<FakeUi> Started { get; } = [];
        public bool ExeMissing { get; set; }
        public FlyoutLaunchArgs LaunchArgs() => Geometry;
        public IUiProcess? StartUi(string[] args, Action<IUiProcess> onExited)
        {
            Calls.Add("start " + string.Join(' ', args));
            if (ExeMissing) return null;
            var ui = new FakeUi { Args = args, OnExited = onExited };
            Started.Add(ui);
            return ui;
        }
        public void WriteHandoff(string text) => Calls.Add("handoff " + (text == Geometry.ToFileText() ? "geometry" : text));
        public void ResetShow() => Calls.Add("reset");
        public void SignalShow() => Calls.Add("show");
        public void SignalClose() => Calls.Add("close");
    }

    private static (FlyoutHost Host, FakePort Port, Func<DateTime> Now, Action<TimeSpan> Advance, Action<bool> SetInstant) Make()
    {
        var port = new FakePort();
        var now = T0;
        var instant = false;
        var host = new FlyoutHost(port, () => instant, () => now);
        return (host, port, () => now, d => now += d, v => instant = v);
    }

    [Fact]
    public void A_click_with_no_ui_starts_one_with_the_launch_geometry()
    {
        var (host, port, _, _, _) = Make();

        host.OnTrayClick();

        port.Calls.Should().Equal("start " + string.Join(' ', Geometry.ToArgs()));
    }

    [Fact]
    public void A_second_click_closes_the_flyout()
    {
        var (host, port, _, advance, _) = Make();
        host.OnTrayClick();
        advance(TimeSpan.FromSeconds(3));

        host.OnTrayClick();

        port.Calls.Last().Should().Be("close");
    }

    [Fact]
    public void A_click_on_a_standby_ui_writes_the_hand_off_file_before_it_sets_the_show_event()
    {
        var (host, port, _, advance, _) = Make();
        host.OnTrayHover();
        advance(TimeSpan.FromSeconds(2));
        port.Calls.Clear();

        host.OnTrayClick();

        port.Calls.Should().Equal("handoff geometry", "show");
        port.Started.Should().HaveCount(1);
    }

    [Fact]
    public void A_hover_starts_a_standby_ui_after_dropping_a_stale_show_signal()
    {
        var (host, port, _, _, _) = Make();

        host.OnTrayHover();

        port.Calls.Should().Equal("reset", "start --standby");
    }

    [Fact]
    public void A_hover_while_a_ui_is_held_or_within_the_cooldown_starts_nothing()
    {
        var (host, port, _, advance, _) = Make();
        host.OnTrayHover();
        port.Calls.Clear();

        advance(TimeSpan.FromSeconds(20));
        host.OnTrayHover();

        port.Calls.Should().BeEmpty();
    }

    [Fact]
    public void A_missing_ui_exe_leaves_nothing_showing_so_the_next_click_tries_again()
    {
        var (host, port, _, advance, _) = Make();
        port.ExeMissing = true;
        host.OnTrayClick();
        advance(TimeSpan.FromSeconds(3));
        port.ExeMissing = false;

        host.OnTrayClick();

        port.Started.Should().HaveCount(1);
        port.Calls.Should().NotContain("close");
    }

    [Fact]
    public void With_instant_open_on_the_exit_of_the_ui_starts_a_kept_standby_ui()
    {
        var (host, port, _, _, setInstant) = Make();
        host.OnTrayClick();
        setInstant(true);
        port.Calls.Clear();

        port.Started[0].Exit();

        port.Calls.Should().Equal("reset", "start --standby --keep");
    }

    [Fact]
    public void Without_instant_open_the_exit_of_the_ui_starts_nothing()
    {
        var (host, port, _, _, _) = Make();
        host.OnTrayClick();
        port.Calls.Clear();

        port.Started[0].Exit();

        port.Calls.Should().BeEmpty();
    }

    [Fact]
    public void The_exit_of_a_ui_that_was_replaced_is_ignored()
    {
        var (host, port, _, advance, setInstant) = Make();
        host.OnTrayHover();
        var first = port.Started[0];
        first.HasExited = true; // gone, but its Exited callback has not run yet
        advance(TimeSpan.FromSeconds(20));
        host.OnTrayHover(); // still held, so no new standby
        host.OnTrayClick(); // not alive: opens a new one
        port.Started.Should().HaveCount(2);
        setInstant(true);
        port.Calls.Clear();

        first.OnExited(first);

        port.Calls.Should().BeEmpty();
    }

    [Fact]
    public void A_click_right_after_the_flyout_closed_itself_does_not_reopen_it()
    {
        var (host, port, _, advance, _) = Make();
        host.OnTrayClick();
        advance(TimeSpan.FromSeconds(3));
        port.Started[0].Exit();
        port.Calls.Clear();
        advance(TimeSpan.FromMilliseconds(120));

        host.OnTrayClick();

        port.Calls.Should().BeEmpty();
    }

    [Fact]
    public void A_hover_after_the_ui_exited_starts_a_standby_ui()
    {
        var (host, port, _, advance, _) = Make();
        host.OnTrayClick();
        port.Started[0].Exit();
        port.Calls.Clear();
        advance(TimeSpan.FromSeconds(30));

        host.OnTrayHover();

        port.Calls.Should().Equal("reset", "start --standby");
    }
}
