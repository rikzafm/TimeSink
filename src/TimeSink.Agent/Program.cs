using System.Diagnostics;
using TimeSink.Agent.Native;
using TimeSink.Core.Data;

namespace TimeSink.Agent;

internal static class Program
{
    private static uint s_taskbarCreated;
    private static nint s_hwnd;
    private static nint s_icon;
    private static Db? s_db;

    private const uint TrayCallback = NativeMethods.WM_APP + 1;
    private const uint IdOpen = 1, IdQuit = 2;
    private const string ClassName = "TimeSink.Agent.MessageWindow";

    private static int Main(string[] args)
    {
        // Spike/diagnostic: prove AOT + SQLite end to end without a tray. `--smoke <dbPath>`.
        if (args.Length == 2 && args[0] == "--smoke")
            return Smoke(args[1]);

        using var mutex = new Mutex(true, @"Local\TimeSink.Agent", out var isFirst);
        if (!isFirst) return 0;

        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeSink", "timesink.db");
        s_db = Db.Open(dbPath);

        return RunMessageLoop();
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
        if (command == IdOpen) OpenUi();
        else if (command == IdQuit) NativeMethods.DestroyWindow(s_hwnd);
    }

    private static void OpenUi()
    {
        // Spike: the UI is a stub. Launch it if present next to the Agent.
        var ui = Path.Combine(AppContext.BaseDirectory, "TimeSink.UI.exe");
        if (File.Exists(ui)) Process.Start(new ProcessStartInfo(ui) { UseShellExecute = false });
    }

    [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        if (msg == s_taskbarCreated && s_taskbarCreated != 0)
        {
            AddTrayIcon(); // Explorer restarted: tray icons were wiped.
            return 0;
        }
        switch (msg)
        {
            case TrayCallback:
                var ev = (uint)(lParam & 0xFFFF);
                if (ev == NativeMethods.WM_RBUTTONUP || ev == NativeMethods.WM_CONTEXTMENU) ShowMenu();
                else if (ev == NativeMethods.WM_LBUTTONUP) OpenUi();
                return 0;
            case NativeMethods.WM_DESTROY:
                RemoveTrayIcon();
                NativeMethods.PostQuitMessage(0);
                return 0;
        }
        return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}
