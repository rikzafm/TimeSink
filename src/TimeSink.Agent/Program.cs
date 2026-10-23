using System.Diagnostics;
using TimeSink.Agent.Native;
using TimeSink.Core.Data;
using TimeSink.Core.Flyout;

namespace TimeSink.Agent;

internal static class Program
{
    private static uint s_taskbarCreated;
    private static nint s_hwnd;
    private static nint s_icon;
    private static Db? s_db;
    private static TrackingHost? s_tracking;
    private static readonly FlyoutToggle s_toggle = new();
    private static Process? s_ui;
    private const string CloseEventName = @"Local\TimeSink.UI.Close";

    private const uint TrayCallback = NativeMethods.WM_APP + 1;
    private const uint IdOpen = 1, IdQuit = 2;
    private const string ClassName = "TimeSink.Agent.MessageWindow";

    private static int Main(string[] args)
    {
        // Spike/diagnostic: prove AOT + SQLite end to end without a tray. `--smoke <dbPath>`.
        if (args.Length == 2 && args[0] == "--smoke")
            return Smoke(args[1]);

        NativeMethods.SetProcessDpiAwarenessContext(NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

        using var mutex = new Mutex(true, @"Local\TimeSink.Agent", out var isFirst);
        if (!isFirst) return 0;

        // `--db <path>` overrides the database location (testing).
        var dbPath = args.Length == 2 && args[0] == "--db" ? args[1] : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeSink", "timesink.db");
        s_db = Db.Open(dbPath);

        LowerPower();
        return RunMessageLoop();
    }

    /// <summary>implementation.md 6.6: below-normal priority and EcoQoS.</summary>
    private static unsafe void LowerPower()
    {
        var self = NativeMethods.GetCurrentProcess();
        NativeMethods.SetPriorityClass(self, NativeMethods.BELOW_NORMAL_PRIORITY_CLASS);
        var state = new PROCESS_POWER_THROTTLING_STATE { Version = 1, ControlMask = 1, StateMask = 1 };
        NativeMethods.SetProcessInformation(self, 4, &state, (uint)sizeof(PROCESS_POWER_THROTTLING_STATE));
    }

    private static int Smoke(string dbPath)
    {
        using var db = Db.Open(dbPath);
        db.InsertApp(@"c:\smoke\test.exe", "Smoke", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        return db.GetApps().Count == 1 && db.SchemaVersion == 1 ? 0 : 1;
    }

    private static unsafe int RunMessageLoop()
    {
        s_taskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        var instance = NativeMethods.GetModuleHandle(0);
        s_icon = NativeMethods.LoadIcon(0, NativeMethods.IDI_APPLICATION);

        fixed (char* className = ClassName)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WndProc,
                hInstance = instance,
                lpszClassName = className,
            };
            if (NativeMethods.RegisterClassEx(&wc) == 0) return 2;
        }

        s_hwnd = NativeMethods.CreateWindowEx(0, ClassName, null, 0, 0, 0, 0, 0,
            0, 0, instance, 0); // hidden top-level (never shown): message-only windows do not receive the TaskbarCreated broadcast
        if (s_hwnd == 0) return 3;

        AddTrayIcon();

        s_tracking = new TrackingHost(s_db!, s_hwnd);
        s_tracking.Start();

        MSG msg;
        while (NativeMethods.GetMessageW(&msg, 0, 0, 0) > 0)
            NativeMethods.DispatchMessageW(&msg);

        s_db?.Dispose();
        return 0;
    }

    private static unsafe void AddTrayIcon()
    {
        var nid = NewNotifyData();
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, &nid);
    }

    private static unsafe void RemoveTrayIcon()
    {
        var nid = NewNotifyData();
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, &nid);
    }

    private static unsafe NOTIFYICONDATAW NewNotifyData()
    {
        var nid = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = s_hwnd,
            uID = 1,
            uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP,
            uCallbackMessage = TrayCallback,
            hIcon = s_icon,
        };
        const string tip = "TimeSink";
        for (var i = 0; i < tip.Length; i++) nid.szTip[i] = tip[i];
        return nid;
    }

    private static void ShowMenu()
    {
        var menu = NativeMethods.CreatePopupMenu();
        NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, IdOpen, "Open");
        NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, IdQuit, "Quit");
        NativeMethods.GetCursorPos(out var pt);
        NativeMethods.SetForegroundWindow(s_hwnd); // required so the menu dismisses on outside click
        var cmd = (uint)NativeMethods.TrackPopupMenu(menu,
            NativeMethods.TPM_RETURNCMD | NativeMethods.TPM_RIGHTBUTTON | NativeMethods.TPM_BOTTOMALIGN,
            pt.X, pt.Y, 0, s_hwnd, 0);
        NativeMethods.DestroyMenu(menu);
        Handle(cmd);
    }

    private static void Handle(uint command)
    {
        if (command == IdOpen) ToggleFlyout();
        else if (command == IdQuit) NativeMethods.DestroyWindow(s_hwnd);
    }

    /// <summary>Tray left-click or "Open" menu item: toggle the flyout.</summary>
    private static void ToggleFlyout()
    {
        var ui = s_ui;
        var running = false;
        try { running = ui is { HasExited: false }; } catch (InvalidOperationException) { }

        switch (s_toggle.OnTrayClick(DateTime.UtcNow, running))
        {
            case FlyoutAction.Open: OpenUi(); break;
            case FlyoutAction.Close:
                try { using var ev = EventWaitHandle.OpenExisting(CloseEventName); ev.Set(); }
                catch (WaitHandleCannotBeOpenedException) { }
                break;
        }
    }

    private static void OpenUi()
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "TimeSink.UI.exe");
        if (!File.Exists(exe)) return;

        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        foreach (var a in ComputeLaunchArgs().ToArgs()) psi.ArgumentList.Add(a);

        var p = Process.Start(psi);
        if (p is null) return;
        NativeMethods.AllowSetForegroundWindow((uint)p.Id);
        p.EnableRaisingEvents = true;
        p.Exited += (_, _) => s_toggle.OnUiExited(DateTime.UtcNow);
        s_ui = p;
    }

    private static FlyoutLaunchArgs ComputeLaunchArgs()
    {
        var id = new NOTIFYICONIDENTIFIER { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NOTIFYICONIDENTIFIER>(), hWnd = s_hwnd, uID = 1 };
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

    private static PxRect Px(RECT r) => new(r.Left, r.Top, r.Right, r.Bottom);

    [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        if (msg == s_taskbarCreated && s_taskbarCreated != 0)
        {
            AddTrayIcon(); // Explorer restarted: tray icons were wiped.
            return 0;
        }
        if (s_tracking is { } tracking && tracking.HandleMessage(msg, wParam))
            return msg == NativeMethods.WM_POWERBROADCAST ? 1 : 0;
        switch (msg)
        {
            case TrayCallback:
                var ev = (uint)(lParam & 0xFFFF);
                if (ev == NativeMethods.WM_RBUTTONUP || ev == NativeMethods.WM_CONTEXTMENU) ShowMenu();
                else if (ev == NativeMethods.WM_LBUTTONUP) ToggleFlyout();
                return 0;
            case NativeMethods.WM_DESTROY:
                s_tracking?.Stop();
                RemoveTrayIcon();
                NativeMethods.PostQuitMessage(0);
                return 0;
        }
        return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}
