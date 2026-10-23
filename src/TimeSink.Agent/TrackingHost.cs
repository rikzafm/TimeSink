using TimeSink.Agent.Native;
using TimeSink.Core.Data;
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

    public TrackingHost(Db db, nint hwnd)
    {
        _hwnd = hwnd;
        var store = new SqliteSessionStore(db);
        var provider = new Win32ProcessProvider(store, pid => NativeMethods.PostMessage(hwnd, ExitMessage, (nuint)pid, 0));
        _engine = new TrackingEngine(store, provider, new SystemClock(), new DbTrackingSettings(db), TimeZoneInfo.Local);
        _engine.Attach(_power);
    }

    public void Start()
    {
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
    });

    private static void Guard(string what, Action a)
    {
        try { a(); }
        catch (Exception ex) { Log.Error($"{what}: {ex.GetType().Name}: {ex.Message}"); }
    }
}
