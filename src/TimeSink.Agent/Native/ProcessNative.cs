using System.Runtime.InteropServices;

namespace TimeSink.Agent.Native;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PROCESSENTRY32W
{
    public uint dwSize;
    public uint cntUsage;
    public uint th32ProcessID;
    public nuint th32DefaultHeapID;
    public uint th32ModuleID;
    public uint cntThreads;
    public uint th32ParentProcessID;
    public int pcPriClassBase;
    public uint dwFlags;
    public fixed char szExeFile[260];
}

[StructLayout(LayoutKind.Sequential)]
internal struct PROCESS_POWER_THROTTLING_STATE
{
    public uint Version;
    public uint ControlMask;
    public uint StateMask;
}

internal static partial class NativeMethods
{
    public const uint TH32CS_SNAPPROCESS = 2;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const uint SYNCHRONIZE = 0x00100000;
    public const uint WT_EXECUTEONLYONCE = 8;
    public const uint INFINITE = 0xFFFFFFFF;
    public const uint WAIT_OBJECT_0 = 0;

    public const uint WM_TIMER = 0x0113;
    public const uint WM_QUERYENDSESSION = 0x0011;
    public const uint WM_ENDSESSION = 0x0016;
    public const uint WM_POWERBROADCAST = 0x0218;
    public const uint WM_WTSSESSION_CHANGE = 0x02B1;
    public const uint PBT_APMSUSPEND = 4, PBT_APMRESUMESUSPEND = 7, PBT_APMRESUMEAUTOMATIC = 0x12;
    public const uint WTS_SESSION_LOCK = 7, WTS_SESSION_UNLOCK = 8;
    public const uint BELOW_NORMAL_PRIORITY_CLASS = 0x4000;

    [LibraryImport("kernel32.dll")]
    public static partial nint CreateToolhelp32Snapshot(uint flags, uint pid);

    [LibraryImport("kernel32.dll", EntryPoint = "Process32FirstW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool Process32First(nint snapshot, PROCESSENTRY32W* entry);

    [LibraryImport("kernel32.dll", EntryPoint = "Process32NextW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool Process32Next(nint snapshot, PROCESSENTRY32W* entry);

    [LibraryImport("kernel32.dll")]
    public static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool QueryFullProcessImageName(nint process, uint flags, char* buffer, ref uint size);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetProcessTimes(nint process, out long creation, out long exit, out long kernel, out long user);

    [LibraryImport("kernel32.dll", EntryPoint = "GetPackageFamilyName")]
    public static unsafe partial int GetPackageFamilyName(nint process, ref uint length, char* buffer);

    [LibraryImport("kernel32.dll")]
    public static partial uint WaitForSingleObject(nint handle, uint ms);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool RegisterWaitForSingleObject(out nint wait, nint handle,
        delegate* unmanaged[Stdcall]<nint, byte, void> callback, nint context, uint ms, uint flags);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterWaitEx(nint wait, nint completionEvent);

    [LibraryImport("kernel32.dll")]
    public static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetPriorityClass(nint process, uint priority);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool SetProcessInformation(nint process, int infoClass, PROCESS_POWER_THROTTLING_STATE* info, uint size);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessage(nint hwnd, uint msg, nuint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial nuint SetTimer(nint hwnd, nuint id, uint ms, nint proc);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool KillTimer(nint hwnd, nuint id);

    [LibraryImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WTSRegisterSessionNotification(nint hwnd, uint flags);
}
