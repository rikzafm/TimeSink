using TimeSink.Core.Tracking;

namespace TimeSink.Agent.Native;

/// <summary>
/// Process snapshot for the tracking engine (implementation.md 6.1): Toolhelp list filtered to the image names of
/// tracked apps, each resolved once (full path, creation time, package family) and watched for exit.
/// FullPath is null when the process cannot be opened.
/// </summary>
internal sealed class Win32ProcessProvider : IProcessProvider
{
    private sealed class Entry
    {
        public required ProcInfo Info { get; init; }
        public nint Handle;
        public nint Wait;
    }

    private static Action<int>? s_onExit;

    private readonly ISessionStore _store;
    private readonly Dictionary<int, Entry> _resolved = new();            // readable processes; the open handle pins the pid
    private readonly Dictionary<int, DateTimeOffset> _unreadable = new(); // pid -> first seen
    private readonly HashSet<int> _loggedUnreadable = new();

    /// <param name="onExit">Called on a thread-pool thread with the pid of a watched process that exited.</param>
    public Win32ProcessProvider(ISessionStore store, Action<int> onExit)
    {
        _store = store;
        s_onExit = onExit;
    }

    public unsafe IReadOnlyList<ProcInfo> Snapshot()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in _store.GetTrackedApps()) names.Add(Path.GetFileName(a.ExePath));

        var seen = new HashSet<int>();
        var result = new List<ProcInfo>();
        if (names.Count > 0)
        {
            var snap = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPPROCESS, 0);
            if (snap != 0 && snap != -1)
            {
                try
                {
                    var e = new PROCESSENTRY32W { dwSize = (uint)sizeof(PROCESSENTRY32W) };
                    for (var ok = NativeMethods.Process32First(snap, &e); ok; ok = NativeMethods.Process32Next(snap, &e))
                    {
                        var name = new string(e.szExeFile);
                        if (!names.Contains(name)) continue;
                        var pid = (int)e.th32ProcessID;
                        seen.Add(pid);
                        var info = Resolve(pid, name);
                        if (info is not null) result.Add(info);
                    }
                }
                finally { NativeMethods.CloseHandle(snap); }
            }
        }
        Prune(seen);
        return result;
    }

    private ProcInfo? Resolve(int pid, string name)
    {
        if (_resolved.TryGetValue(pid, out var cached))
        {
            // Signalled but still listed: it has exited; the exit message ticks the engine.
            if (NativeMethods.WaitForSingleObject(cached.Handle, 0) == NativeMethods.WAIT_OBJECT_0) return null;
            return cached.Info;
        }

        var h = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION | NativeMethods.SYNCHRONIZE, false, (uint)pid);
        if (h == 0) return Unreadable(pid, name);

        var path = QueryPath(h);
        if (path is null || !NativeMethods.GetProcessTimes(h, out var creation, out _, out _, out _))
        {
            NativeMethods.CloseHandle(h);
            return Unreadable(pid, name);
        }
        var start = DateTimeOffset.FromFileTime(creation).ToUniversalTime();
        var family = path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) ? QueryFamily(h) : null;
        var entry = new Entry { Info = new ProcInfo(pid, name, path, start, family), Handle = h };
        RegisterExitWait(entry, pid);
        _resolved[pid] = entry;
        _unreadable.Remove(pid);
        return entry.Info;
    }

    private static unsafe void RegisterExitWait(Entry entry, int pid)
    {
        if (!NativeMethods.RegisterWaitForSingleObject(out entry.Wait, entry.Handle, &ExitCallback, pid,
                NativeMethods.INFINITE, NativeMethods.WT_EXECUTEONLYONCE))
            entry.Wait = 0;
    }

    // Runs on a thread-pool thread: only forwards the pid; the Agent posts it to the message window.
    [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static void ExitCallback(nint context, byte timedOut) => s_onExit?.Invoke((int)context);

    private ProcInfo Unreadable(int pid, string name)
    {
        if (!_unreadable.TryGetValue(pid, out var first)) _unreadable[pid] = first = DateTimeOffset.UtcNow;
        if (_loggedUnreadable.Add(pid)) Log.Info($"cannot open process {name} (pid {pid}); matching by image name only");
        return new ProcInfo(pid, name, null, first);
    }

    private void Prune(HashSet<int> seen)
    {
        foreach (var pid in _resolved.Keys.Where(p => !seen.Contains(p)).ToList())
        {
            var e = _resolved[pid];
            if (e.Wait != 0) NativeMethods.UnregisterWaitEx(e.Wait, 0);
            NativeMethods.CloseHandle(e.Handle);
            _resolved.Remove(pid);
        }
        foreach (var pid in _unreadable.Keys.Where(p => !seen.Contains(p)).ToList()) _unreadable.Remove(pid);
        _loggedUnreadable.RemoveWhere(p => !seen.Contains(p));
    }

    private static unsafe string? QueryPath(nint h)
    {
        var buf = stackalloc char[1024];
        uint len = 1024;
        return NativeMethods.QueryFullProcessImageName(h, 0, buf, ref len) ? new string(buf, 0, (int)len) : null;
    }

    private static unsafe string? QueryFamily(nint h)
    {
        var buf = stackalloc char[128];
        uint len = 128;
        return NativeMethods.GetPackageFamilyName(h, ref len, buf) == 0 ? new string(buf, 0, (int)len - 1) : null;
    }
}
