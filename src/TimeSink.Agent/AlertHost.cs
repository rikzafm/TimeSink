using TimeSink.Agent.Native;
using TimeSink.Core.Alerts;
using TimeSink.Core.Data;

namespace TimeSink.Agent;

/// <summary>
/// Goal/limit alerts and the weekly summary (story 41): every 30 s asks <see cref="AlertEngine"/> what is due and shows it
/// as a tray balloon. Runs on the message-loop thread next to tracking (same database connection), cheap: two settings
/// reads when both switches are off, a few small queries otherwise. Notifies only; nothing is ever blocked.
/// </summary>
internal sealed class AlertHost
{
    private const nuint TimerId = 3;
    private const uint CheckMs = 30 * 1000;

    private readonly AlertEngine _engine;
    private readonly nint _hwnd;
    private readonly Action<string, string> _show;

    public AlertHost(Db db, nint hwnd, Action<string, string> show)
    {
        _engine = new AlertEngine(db, TimeZoneInfo.Local);
        _hwnd = hwnd;
        _show = show;
    }

    public void Start()
    {
        NativeMethods.SetTimer(_hwnd, TimerId, CheckMs, 0);
        Check();
    }

    public void Stop() => NativeMethods.KillTimer(_hwnd, TimerId);

    /// <summary>Handles a window message; returns true if consumed.</summary>
    public bool HandleMessage(uint msg, nuint wParam)
    {
        if (msg != NativeMethods.WM_TIMER || wParam != TimerId) return false;
        Check();
        return true;
    }

    private void Check()
    {
        try
        {
            foreach (var n in _engine.Check(DateTimeOffset.UtcNow))
            {
                Log.Info($"alert shown: {n.Kind}");
                _show(n.Title, n.Body);
            }
        }
        catch (Exception ex) { Log.Error($"alerts: {ex.GetType().Name}: {ex.Message}"); }
    }
}
