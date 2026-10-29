using System.Diagnostics;
using TimeSink.Agent.Native;
using TimeSink.Core.Flyout;

namespace TimeSink.Agent;

/// <summary>The Windows adapter for <see cref="IFlyoutPort"/>: the UI process, the Show and Close events, the hand-off file and the tray icon's rectangle.</summary>
internal sealed class WindowsFlyoutPort : IFlyoutPort
{
    private readonly nint _hwnd;
    private EventWaitHandle? _showEvent;

    public WindowsFlyoutPort(nint hwnd) => _hwnd = hwnd;

    public IUiProcess? StartUi(string[] args, Action<IUiProcess> onExited)
    {
        // Installed layout keeps the UI and its runtime in ui\; dev builds put it beside the Agent.
        var exe = Path.Combine(AppContext.BaseDirectory, "ui", "TimeSink.UI.exe");
        if (!File.Exists(exe)) exe = Path.Combine(AppContext.BaseDirectory, "TimeSink.UI.exe");
        if (!File.Exists(exe)) return null;

        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);

        var p = Process.Start(psi);
        if (p is null) return null;
        NativeMethods.AllowSetForegroundWindow((uint)p.Id);
        var ui = new UiProcess(p);
        p.EnableRaisingEvents = true;
        p.Exited += (_, _) => onExited(ui);
        return ui;
    }

    public FlyoutLaunchArgs LaunchArgs()
    {
        var id = new NOTIFYICONIDENTIFIER { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NOTIFYICONIDENTIFIER>(), hWnd = _hwnd, uID = 1 };
        RECT icon;
        if (NativeMethods.Shell_NotifyIconGetRect(id, out icon) != 0)
        {
            NativeMethods.GetCursorPos(out var pt); // fallback: a 1 px rect at the cursor
            icon = new RECT { Left = pt.X, Top = pt.Y, Right = pt.X + 1, Bottom = pt.Y + 1 };
        }

        var monitor = NativeMethods.MonitorFromRect(icon, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        NativeMethods.GetMonitorInfo(monitor, ref info);
        uint dpi = 96;
        if (NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out var dx, out _) == 0) dpi = dx;

        return new FlyoutLaunchArgs(Px(icon), Px(info.rcMonitor), Px(info.rcWork), (int)dpi);
    }

    public void WriteHandoff(string text) => File.WriteAllText(FlyoutProtocol.HandoffPath, text);

    public void ResetShow() => ShowEvent().Reset();

    public void SignalShow() => ShowEvent().Set();

    public void SignalClose()
    {
        try { using var ev = EventWaitHandle.OpenExisting(FlyoutProtocol.CloseEventName); ev.Set(); }
        catch (WaitHandleCannotBeOpenedException) { }
    }

    private EventWaitHandle ShowEvent() =>
        _showEvent ??= new EventWaitHandle(false, EventResetMode.AutoReset, FlyoutProtocol.ShowEventName);

    private static PxRect Px(RECT r) => new(r.Left, r.Top, r.Right, r.Bottom);

    private sealed class UiProcess(Process process) : IUiProcess
    {
        public bool HasExited
        {
            get
            {
                try { return process.HasExited; }
                catch (InvalidOperationException) { return true; }
            }
        }
    }
}
