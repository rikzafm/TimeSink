namespace TimeSink.Core.Flyout;

/// <summary>A UI process the Agent started. <see cref="HasExited"/> is true once it is gone, and also when that cannot be told.</summary>
public interface IUiProcess
{
    bool HasExited { get; }
}

/// <summary>The Windows side of the flyout: starting the UI, the two named events, the hand-off file and the tray icon's position.</summary>
public interface IFlyoutPort
{
    /// <summary>Starts the UI with <paramref name="args"/> and calls <paramref name="onExited"/> (on any thread) when it exits. Null when it could not be started, for instance when its exe is missing.</summary>
    IUiProcess? StartUi(string[] args, Action<IUiProcess> onExited);

    /// <summary>Where the tray icon is, on which monitor and at what DPI; read when the flyout is about to open.</summary>
    FlyoutLaunchArgs LaunchArgs();

    /// <summary>Leaves the launch geometry (<see cref="FlyoutLaunchArgs.ToFileText"/>) for a standby UI.</summary>
    void WriteHandoff(string text);

    /// <summary>Drops a Show signal left over from an earlier standby UI.</summary>
    void ResetShow();

    /// <summary>Wakes a standby UI.</summary>
    void SignalShow();

    /// <summary>Closes a UI that is showing; nothing happens when none is listening.</summary>
    void SignalClose();
}

/// <summary>
/// The Agent's flyout controller: it holds the UI process and performs the commands <see cref="FlyoutLifecycle"/> returns
/// through an <see cref="IFlyoutPort"/>. Tray clicks, hovers and the UI process exiting arrive on different threads, so
/// every entry point takes one lock. Behaviour it keeps:
/// <list type="bullet">
/// <item>Launch geometry is written to the hand-off file before the Show event is set, so a woken UI never reads an old one.</item>
/// <item>The exit of a UI the host no longer holds (replaced by a newer one) is ignored.</item>
/// <item>A standby UI is not started while one is alive, and a stale Show signal is dropped before a new standby starts.</item>
/// <item>A UI that cannot be started clears the lifecycle's Shown flag, so the next click tries again.</item>
/// </list>
/// </summary>
public sealed class FlyoutHost
{
    private readonly object _gate = new();
    private readonly FlyoutLifecycle _lifecycle = new();
    private readonly IFlyoutPort _port;
    private readonly Func<bool> _instantOpen;
    private readonly Func<DateTime> _utcNow;
    private IUiProcess? _ui;

    public FlyoutHost(IFlyoutPort port, Func<bool> instantOpen, Func<DateTime>? utcNow = null)
    {
        _port = port;
        _instantOpen = instantOpen;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>Tray left-click or the "Open" menu item: toggle the flyout.</summary>
    public void OnTrayClick()
    {
        lock (_gate) Perform(_lifecycle.OnTrayClick(_utcNow(), UiAlive()));
    }

    /// <summary>Mouse over the tray icon: a click is likely, so warm the UI up (at most once per cooldown).</summary>
    public void OnTrayHover()
    {
        lock (_gate) Perform(_lifecycle.OnTrayHover(_utcNow(), _ui is not null));
    }

    private bool UiAlive() => _ui is { HasExited: false };

    private void OnUiExited(IUiProcess p)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_ui, p)) return;
            _ui = null;
            Perform(_lifecycle.OnUiExited(_utcNow(), _instantOpen())); // ui.instantOpen keeps a warm UI ready at all times
        }
    }

    private void Perform(FlyoutCommand command)
    {
        switch (command)
        {
            case FlyoutCommand.OpenCold:
                if (!StartUi(_port.LaunchArgs().ToArgs())) _lifecycle.OnStartFailed();
                break;
            case FlyoutCommand.RevealStandby:
                _port.WriteHandoff(_port.LaunchArgs().ToFileText());
                _port.SignalShow();
                break;
            case FlyoutCommand.Close:
                _port.SignalClose();
                break;
            case FlyoutCommand.StartStandby:
                StartStandby(keep: false);
                break;
            case FlyoutCommand.StartKeptStandby:
                StartStandby(keep: true);
                break;
        }
    }

    private void StartStandby(bool keep)
    {
        if (UiAlive()) return;
        _port.ResetShow();
        StartUi(FlyoutProtocol.StandbyArgs(keep));
    }

    private bool StartUi(string[] args)
    {
        var p = _port.StartUi(args, OnUiExited);
        if (p is null) return false;
        _ui = p;
        return true;
    }
}
