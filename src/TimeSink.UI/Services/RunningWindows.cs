using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using TimeSink.Core.Apps;

namespace TimeSink.UI.Services;

/// <summary>
/// Win32 side of the Add app / Running tab: <c>EnumWindows</c> mapped to owning processes. The filtering rules
/// themselves live in Core (<see cref="RunningApps"/>). Store apps are shown by a window of
/// <c>ApplicationFrameHost</c>; the real app is the child window owned by another process.
/// </summary>
internal static class RunningWindows
{
    internal sealed record Snapshot(IReadOnlyList<OpenWindow> Windows, IReadOnlyDictionary<string, string> AumidByPath);

    private delegate bool EnumProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, nint lParam);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumProc cb, nint lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint hwnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder sb, int max);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attr, out int value, int size);
    [DllImport("kernel32.dll")] private static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(nint p, uint flags, StringBuilder sb, ref uint size);
    [DllImport("kernel32.dll")] private static extern bool GetProcessTimes(nint p, out long creation, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetPackageFamilyName(nint p, ref uint len, StringBuilder sb);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetApplicationUserModelId(nint p, ref uint len, StringBuilder sb);

    private const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80, DWMWA_CLOAKED = 14;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private static readonly string[] ShellClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    private sealed record Proc(string? Path, DateTimeOffset Start, string? Family, string? Aumid, string Name);

    public static Snapshot Capture()
    {
        var procs = new Dictionary<uint, Proc?>();
        var windows = new List<OpenWindow>();
        var aumids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var self = (uint)Environment.ProcessId;

        EnumWindows((hwnd, _) =>
        {
            try
            {
                var visible = IsWindowVisible(hwnd);
                var tool = (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0;
                var cloaked = DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var c, sizeof(int)) == 0 && c != 0;
                if (!visible || tool || cloaked) return true;

                var cls = ClassName(hwnd);
                if (ShellClasses.Contains(cls, StringComparer.Ordinal)) return true;

                GetWindowThreadProcessId(hwnd, out var pid);
                if (cls == "ApplicationFrameWindow" && FindHostedPid(hwnd, pid) is { } real) pid = real;
                else if (cls == "ApplicationFrameWindow") return true; // frame with no app behind it

                if (pid == self) return true;
                if (!procs.TryGetValue(pid, out var p)) procs[pid] = p = Resolve(pid);
                if (p?.Path is null || IsTimeSink(p.Path)) return true;

                if (p.Aumid is not null) aumids[p.Path] = p.Aumid;
                windows.Add(new OpenWindow(p.Path, p.Name, p.Start, true, false, false, p.Family));
            }
            catch { /* a window that vanishes mid-enumeration is simply skipped */ }
            return true;
        }, 0);

        return new Snapshot(windows, aumids);
    }

    private static bool IsTimeSink(string path)
    {
        var file = System.IO.Path.GetFileName(path);
        return file.Equals("TimeSink.Agent.exe", StringComparison.OrdinalIgnoreCase)
            || file.Equals("TimeSink.UI.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static uint? FindHostedPid(nint frame, uint frameHostPid)
    {
        uint? found = null;
        EnumChildWindows(frame, (child, _) =>
        {
            GetWindowThreadProcessId(child, out var p);
            if (p != frameHostPid) { found = p; return false; }
            return true;
        }, 0);
        return found;
    }

    private static string ClassName(nint hwnd)
    {
        var sb = new StringBuilder(128);
        return GetClassName(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
    }

    private static Proc? Resolve(uint pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == 0) return null; // elevated or protected: cannot be matched by path anyway
        try
        {
            var sb = new StringBuilder(1024);
            var size = (uint)sb.Capacity;
            if (!QueryFullProcessImageName(h, 0, sb, ref size)) return null;
            var path = sb.ToString();
            if (!GetProcessTimes(h, out var creation, out _, out _, out _)) return null;
            var start = DateTimeOffset.FromFileTime(creation).ToUniversalTime();

            string? family = null, aumid = null;
            var len = 256u;
            var fam = new StringBuilder((int)len);
            if (GetPackageFamilyName(h, ref len, fam) == 0) family = fam.ToString();
            if (family is not null)
            {
                len = 256;
                var id = new StringBuilder((int)len);
                if (GetApplicationUserModelId(h, ref len, id) == 0) aumid = id.ToString();
            }
            return new Proc(path, start, family, aumid, DisplayNameOf(path));
        }
        finally { CloseHandle(h); }
    }

    /// <summary>File description when present, else the file name without extension (implementation.md 8.3).</summary>
    internal static string DisplayNameOf(string path)
    {
        try
        {
            var d = FileVersionInfo.GetVersionInfo(path).FileDescription;
            if (!string.IsNullOrWhiteSpace(d)) return d.Trim();
        }
        catch { }
        return System.IO.Path.GetFileNameWithoutExtension(path);
    }
}
