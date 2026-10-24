using System.Diagnostics;
using TimeSink.Agent.Native;
using TimeSink.Core.Data;
using TimeSink.Core.Flyout;
using TimeSink.Core.Icons;
using TimeSink.Core.Ipc;

namespace TimeSink.Agent;

internal static class Program
{
    private static uint s_taskbarCreated;
    private static nint s_hwnd;
    private static nint s_icon;
    private static Db? s_db;
    private static TrackingHost? s_tracking;
    private static IpcServer? s_ipc;
    private static string s_pipeName = "";
    private static BackupScheduler? s_backup;
    private static AlertHost? s_alerts;
    private static string s_dbPath = "";
    private static readonly FlyoutToggle s_toggle = new();
    private static Process? s_ui;
    private const string CloseEventName = @"Local\TimeSink.UI.Close";

    private const uint IconResourceVersion = 0x00030000; // the version CreateIconFromResourceEx requires
    private const uint TrayCallback = NativeMethods.WM_APP + 1;
    private const uint SetIconMessage = NativeMethods.WM_APP + 4; // WM_APP + 2/3 are TrackingHost's
    private static string s_requestedIcon = IconCatalogue.DefaultId;
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
        s_dbPath = dbPath;
        var scratch = args.Length == 2 && args[0] == "--db" ? dbPath : null;
        if (scratch is not null) Environment.SetEnvironmentVariable("TIMESINK_DB", scratch); // inherited by the UI we spawn
        s_pipeName = PipeNames.For(scratch);
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
        s_icon = LoadTrayIcon(IconCatalogue.Resolve(SettingsStore.Get(s_dbPath, "ui.icon")));
        if (s_icon == 0) s_icon = NativeMethods.LoadIcon(0, NativeMethods.IDI_APPLICATION); // never add the tray entry without an icon

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

        s_ipc = new IpcServer(s_pipeName, () => s_tracking?.CurrentState ?? new StateMessage(false, []), Log.Error);
        s_ipc.IconRequested += id => { s_requestedIcon = IconCatalogue.Resolve(id); NativeMethods.PostMessage(s_hwnd, SetIconMessage, 0, 0); };
        s_ipc.Start();
        s_tracking = new TrackingHost(s_db!, s_hwnd, s_ipc.Publish);
        s_tracking.Start();
        s_backup = new BackupScheduler(s_dbPath, s_hwnd);
        s_backup.Start();
        s_alerts = new AlertHost(s_db!, s_hwnd, ShowBalloon);
        s_alerts.Start();

        MSG msg;
        while (NativeMethods.GetMessageW(&msg, 0, 0, 0) > 0)
            NativeMethods.DispatchMessageW(&msg);

        s_ipc?.Dispose();
        s_db?.Dispose();
        return 0;
    }

    private static unsafe void AddTrayIcon()
    {
        var nid = NewNotifyData();
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, &nid);
    }

    /// <summary>Shows a tray balloon (a toast on current Windows) from the tray icon. Local only: no network, no toast package.</summary>
    private static unsafe void ShowBalloon(string title, string body)
    {
        var nid = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = s_hwnd,
            uID = 1,
            uFlags = NativeMethods.NIF_INFO,
            dwInfoFlags = NativeMethods.NIIF_INFO,
        };
        for (var i = 0; i < Math.Min(title.Length, 63); i++) nid.szInfoTitle[i] = title[i];
        for (var i = 0; i < Math.Min(body.Length, 255); i++) nid.szInfo[i] = body[i];
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, &nid);
    }

    /// <summary>Builds the tray HICON for an icon id at the system small-icon size. 0 (no icon) if the frame cannot be loaded.</summary>
    private static unsafe nint LoadTrayIcon(string id)
    {
        var px = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SM_CXSMICON, NativeMethods.GetDpiForSystem());
        var frame = IconCatalogue.ReadFrame(id, px);
        fixed (byte* p = frame)
            return NativeMethods.CreateIconFromResourceEx(p, (uint)frame.Length, true, IconResourceVersion, px, px, 0);
    }

    /// <summary>The UI applied a new icon: swap it on the live tray entry and free the old handle.</summary>
    private static unsafe void ApplyTrayIcon()
    {
        var fresh = LoadTrayIcon(s_requestedIcon);
        if (fresh == 0) return;
        var old = s_icon;
        s_icon = fresh;
        var nid = new NOTIFYICONDATAW { cbSize = (uint)sizeof(NOTIFYICONDATAW), hWnd = s_hwnd, uID = 1, uFlags = NativeMethods.NIF_ICON, hIcon = fresh };
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, &nid);
        if (old != 0) NativeMethods.DestroyIcon(old);
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

    // UI process state. A "standby" UI is started (hover or ui.instantOpen) and renders off-screen; it counts as
    // not shown until the Show signal. Touched from the message loop and from Process.Exited, hence the lock.
    private static readonly object s_gate = new();
    private static bool s_uiShown;
    private static DateTime s_lastStandbyStart = DateTime.MinValue;
    private static EventWaitHandle? s_showEvent;
    private const string ShowEventName = @"Local\TimeSink.UI.Show";
    private static readonly TimeSpan HoverCooldown = TimeSpan.FromSeconds(15);

    /// <summary>Tray left-click or "Open" menu item: toggle the flyout.</summary>
    private static void ToggleFlyout()
    {
        lock (s_gate)
        {
            var alive = false;
            try { alive = s_ui is { HasExited: false }; } catch (InvalidOperationException) { }

            switch (s_toggle.OnTrayClick(DateTime.UtcNow, alive && s_uiShown))
            {
                case FlyoutAction.Open:
                    if (alive) ShowStandby(); else OpenUi();
                    break;
                case FlyoutAction.Close:
                    try { using var ev = EventWaitHandle.OpenExisting(CloseEventName); ev.Set(); }
                    catch (WaitHandleCannotBeOpenedException) { }
                    break;
            }
        }
    }

    /// <summary>Cold start: the UI shows itself as soon as it can.</summary>
    private static void OpenUi()
    {
        if (StartUi(ComputeLaunchArgs().ToArgs()) is not null) s_uiShown = true;
    }

    /// <summary>Hand the launch geometry to a running standby UI and wake it.</summary>
    private static void ShowStandby()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeSink");
        File.WriteAllText(Path.Combine(dir, "flyout.args"), ComputeLaunchArgs().ToFileText());
        s_showEvent ??= new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        s_showEvent.Set();
        s_uiShown = true;
    }

    /// <summary>Start the UI hidden so a following click is near-instant. Idle standbys exit on their own.</summary>
    private static void StartStandby(bool keep)
    {
        lock (s_gate)
        {
            if (s_ui is { HasExited: false }) return;
            s_lastStandbyStart = DateTime.UtcNow;
            s_showEvent ??= new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            s_showEvent.Reset(); // drop a stale signal from an earlier standby
            if (StartUi(keep ? ["--standby", "--keep"] : ["--standby"]) is not null) s_uiShown = false;
        }
    }

    private static Process? StartUi(string[] args)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "TimeSink.UI.exe");
        if (!File.Exists(exe)) return null;

        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);

        var p = Process.Start(psi);
        if (p is null) return null;
        NativeMethods.AllowSetForegroundWindow((uint)p.Id);
        p.EnableRaisingEvents = true;
        p.Exited += (_, _) => OnUiExited(p);
        s_ui = p;
        return p;
    }

    private static void OnUiExited(Process p)
    {
        lock (s_gate)
        {
            if (!ReferenceEquals(s_ui, p)) return;
            s_toggle.OnUiExited(DateTime.UtcNow);
            s_ui = null;
            s_uiShown = false;
            // ui.instantOpen: keep a warm UI ready at all times (about 40 MB, opt-in).
            if (InstantOpen()) StartStandby(keep: true);
        }
    }

    private static bool InstantOpen()
    {
        var v = SettingsStore.Get(s_dbPath, "ui.instantOpen");
        return v is not null && (v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1");
    }

    /// <summary>Mouse over the tray icon: a click is likely, so warm the UI up (at most once per cooldown).</summary>
    private static void OnTrayHover()
    {
        lock (s_gate)
        {
            if (s_ui is not null) return;
            if (DateTime.UtcNow - s_lastStandbyStart < HoverCooldown) return;
        }
        StartStandby(keep: false);
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
        if (s_backup is { } backup && backup.HandleMessage(msg, wParam)) return 0;
        if (s_alerts is { } alerts && alerts.HandleMessage(msg, wParam)) return 0;
        switch (msg)
        {
            case TrayCallback:
                var ev = (uint)(lParam & 0xFFFF);
                if (ev == NativeMethods.WM_RBUTTONUP || ev == NativeMethods.WM_CONTEXTMENU) ShowMenu();
                else if (ev == NativeMethods.WM_LBUTTONUP) ToggleFlyout();
                else if (ev == NativeMethods.WM_MOUSEMOVE) OnTrayHover();
                return 0;
            case SetIconMessage:
                ApplyTrayIcon();
                return 0;
            case NativeMethods.WM_DESTROY:
                s_backup?.Stop();
                s_alerts?.Stop();
                s_tracking?.Stop();
                RemoveTrayIcon();
                NativeMethods.PostQuitMessage(0);
                return 0;
        }
        return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}
