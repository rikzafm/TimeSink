namespace TimeSink.Core.Flyout;

/// <summary>What the Agent should do about the UI process. It performs the command; <see cref="FlyoutLifecycle"/> only decides.</summary>
public enum FlyoutCommand
{
    None,

    /// <summary>No UI is running: start one that shows itself as soon as it can.</summary>
    OpenCold,

    /// <summary>A standby UI is running: hand it the launch geometry and wake it.</summary>
    RevealStandby,

    /// <summary>A UI is showing: signal it to close.</summary>
    Close,

    /// <summary>Start a hidden UI that gives up when idle, so a following click is near-instant.</summary>
    StartStandby,

    /// <summary>Start a hidden UI that waits for the Show signal for as long as it lives (<c>ui.instantOpen</c>).</summary>
    StartKeptStandby,
}

/// <summary>
/// The Agent's rules for the flyout's UI process: what a tray click, a hover over the tray icon and the UI exiting each
/// lead to. It holds the little state those rules need and does nothing itself, so it is tested with plain times.
/// <list type="bullet">
/// <item>A click while a UI is showing closes it; otherwise it opens one (waking a standby UI when there is one).</item>
/// <item>The click that closes the flyout by deactivating it must not reopen it, so a click within <see cref="Debounce"/> of the UI exiting does nothing.</item>
/// <item>A hover starts a standby UI, at most once per <see cref="HoverCooldown"/>, and never while a UI is running.</item>
/// <item>When the UI exits with <c>ui.instantOpen</c> on, a kept standby UI is started at once. Nothing else starts one, so with it on the first click after the Agent starts is a cold open.</item>
/// </list>
/// Not thread-safe: the caller serialises its calls.
/// </summary>
public sealed class FlyoutLifecycle
{
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);
    public static readonly TimeSpan HoverCooldown = TimeSpan.FromSeconds(15);

    private DateTime? _lastExit;
    private DateTime _lastStandbyStart = DateTime.MinValue;

    /// <summary>True from the moment the UI is told to show until it exits or fails to start. A standby UI is not shown.</summary>
    public bool Shown { get; private set; }

    /// <param name="uiAlive">Whether a UI process is running (showing or on standby).</param>
    public FlyoutCommand OnTrayClick(DateTime utcNow, bool uiAlive)
    {
        if (uiAlive && Shown) return FlyoutCommand.Close;
        if (_lastExit is { } exited && utcNow - exited <= Debounce) return FlyoutCommand.None;
        Shown = true;
        return uiAlive ? FlyoutCommand.RevealStandby : FlyoutCommand.OpenCold;
    }

    /// <param name="uiPresent">Whether the Agent still holds a UI process, even one that is exiting.</param>
    public FlyoutCommand OnTrayHover(DateTime utcNow, bool uiPresent)
    {
        if (uiPresent || utcNow - _lastStandbyStart < HoverCooldown) return FlyoutCommand.None;
        _lastStandbyStart = utcNow;
        Shown = false;
        return FlyoutCommand.StartStandby;
    }

    public FlyoutCommand OnUiExited(DateTime utcNow, bool instantOpen)
    {
        _lastExit = utcNow;
        Shown = false;
        if (!instantOpen) return FlyoutCommand.None;
        _lastStandbyStart = utcNow;
        return FlyoutCommand.StartKeptStandby;
    }

    /// <summary>The UI process could not be started (its exe is missing): nothing is showing.</summary>
    public void OnStartFailed() => Shown = false;
}
