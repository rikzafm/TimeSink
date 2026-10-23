namespace TimeSink.Core.Flyout;

public enum FlyoutAction { Open, Close, Ignore }

/// <summary>
/// Decides what a tray click does. The click that closes the flyout (by deactivating it) must not
/// reopen it, so clicks within <see cref="Debounce"/> of the UI exiting are ignored.
/// </summary>
public sealed class FlyoutToggle
{
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

    private long _lastExitTicks = long.MinValue;

    public void OnUiExited(DateTime utcNow) => Interlocked.Exchange(ref _lastExitTicks, utcNow.Ticks);

    public FlyoutAction OnTrayClick(DateTime utcNow, bool uiRunning)
    {
        if (uiRunning) return FlyoutAction.Close;
        var last = Interlocked.Read(ref _lastExitTicks);
        if (last != long.MinValue && utcNow.Ticks - last <= Debounce.Ticks) return FlyoutAction.Ignore;
        return FlyoutAction.Open;
    }
}
