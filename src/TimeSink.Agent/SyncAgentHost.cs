using TimeSink.Agent.Native;
using TimeSink.Core.Backup;
using TimeSink.Core.Sync;

namespace TimeSink.Agent;

/// <summary>
/// Connects <see cref="SyncHost"/> into the Agent's Windows message loop and named-event signals.
/// </summary>
internal sealed class SyncAgentHost : IDisposable
{
    public const uint SyncNowMessage = NativeMethods.WM_APP + 5;

    private readonly SyncHost _sync;
    private readonly nint _hwnd;
    private EventWaitHandle? _syncNowEvent;
    private RegisteredWaitHandle? _syncNowWait;

    public SyncAgentHost(string dbPath, nint hwnd)
    {
        _hwnd = hwnd;
        _sync = new SyncHost(dbPath, TimeZoneInfo.Local, null, Log.Info);
    }

    public void Start()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                _syncNowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, AgentSignals.SyncNowEventName);
                _syncNowWait = ThreadPool.RegisterWaitForSingleObject(_syncNowEvent,
                    (_, _) => NativeMethods.PostMessage(_hwnd, SyncNowMessage, 0, 0), null, Timeout.Infinite, executeOnlyOnce: false);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"sync now signal: {ex.Message}");
        }

        _sync.Start();
    }

    public bool HandleMessage(uint msg)
    {
        if (msg == SyncNowMessage)
        {
            _sync.SyncNow();
            return true;
        }
        return false;
    }

    public void SyncNow() => _sync.SyncNow();

    public void Dispose()
    {
        _syncNowWait?.Unregister(null);
        _syncNowEvent?.Dispose();
        _sync.Dispose();
    }
}
