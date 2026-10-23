using TimeSink.Agent.Native;
using TimeSink.Core.Data;
using TimeSink.Core.Ipc;
using TimeSink.Core.Tracking;

namespace TimeSink.Agent;

/// <summary>Owns the tracking engine and feeds it ticks, process-exit notifications and power events.</summary>
internal sealed class TrackingHost
{
    public const uint ExitMessage = NativeMethods.WM_APP + 2;
    private const nuint TimerId = 1;
    private const uint TickMs = 5000;

    private readonly TrackingEngine _engine;
    private readonly WindowPowerEvents _power = new();
    private readonly nint _hwnd;
    private readonly SqliteSessionStore _store;
    private readonly Action<StateMessage> _publish;
    private volatile StateMessage _state = new(false, []);

    public TrackingHost(Db db, nint hwnd, Action<StateMessage> publish)
    {
        _hwnd = hwnd;
        _publish = publish;
        var store = _store = new SqliteSessionStore(db);
        var provider = new Win32ProcessProvider(store, pid => NativeMethods.PostMessage(hwnd, ExitMessage, (nuint)pid, 0));
        _engine = new TrackingEngine(store, provider, new SystemClock(), new DbTrackingSettings(db), TimeZoneInfo.Local);
        _engine.Attach(_power);
    }

    public const uint ResetMessage = NativeMethods.WM_APP + 3;
    private EventWaitHandle? _resetEvent;
    private RegisteredWaitHandle? _resetWait;

    public void Start()
    {
        // The UI sets this after a Replace restore or Delete all history (the database changed underneath us).
        _resetEvent = new EventWaitHandle(false, EventResetMode.AutoReset, TimeSink.Core.Backup.AgentSignals.ResetEventName);
        _resetWait = ThreadPool.RegisterWaitForSingleObject(_resetEvent,
            (_, _) => NativeMethods.PostMessage(_hwnd, ResetMessage, 0, 0), null, Timeout.Infinite, executeOnlyOnce: false);

        Guard("recover", () => _engine.RecoverFromCrash(DateTimeOffset.UtcNow));
        NativeMethods.WTSRegisterSessionNotification(_hwnd, 0);
        NativeMethods.SetTimer(_hwnd, TimerId, TickMs, 0);
        Tick();
    }

    /// <summary>Handles a window message; returns true if consumed.</summary>
    public bool HandleMessage(uint msg, nuint wParam)
    {
        if (msg == NativeMethods.WM_TIMER && wParam == TimerId) { Tick(); return true; }
        if (msg == ExitMessage) { Tick(); return true; }
        if (msg == ResetMessage)
        {
            Guard("reset", () => { _engine.DiscardOpenSessions(DateTimeOffset.UtcNow); Tick(); });
            return true;
        }
        var handled = false;
        Guard("power", () => handled = _power.Handle(msg, wParam, DateTimeOffset.UtcNow));
        return handled;
    }

    /// <summary>Quit: close open sessions as 'shutdown' and stop the timer.</summary>
    public void Stop()
    {
        NativeMethods.KillTimer(_hwnd, TimerId);
        Guard("stop", () => _power.RaiseShutdown(DateTimeOffset.UtcNow));
    }

    private void Tick() => Guard("tick", () =>
    {
        _engine.ReloadTrackedApps(); // picks up apps added or retired directly in the database
        _engine.OnTick(DateTimeOffset.UtcNow);
        PublishIfChanged();
    });

    /// <summary>Last pushed state; safe to read from any thread (IPC replies to getState with it).</summary>
    public StateMessage CurrentState => _state;

    /// <summary>Pushes the running-apps list to the UI only when it differs from the last one pushed.</summary>
    private void PublishIfChanged()
    {
        var running = _store.GetOpenSessions().Select(s => new RunningApp(s.AppId, s.StartUtc)).OrderBy(r => r.AppId).ToList();
        var old = _state.Running;
        if (old.Count == running.Count && old.SequenceEqual(running)) return;
        _state = new StateMessage(false, running);
        _publish(_state);
    }

    private static void Guard(string what, Action a)
    {
        try { a(); }
        catch (Exception ex) { Log.Error($"{what}: {ex.GetType().Name}: {ex.Message}"); }
    }
}
