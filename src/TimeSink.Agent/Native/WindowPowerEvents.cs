using TimeSink.Core.Tracking;

namespace TimeSink.Agent.Native;

/// <summary>Translates power/session window messages into <see cref="IPowerEvents"/>.</summary>
internal sealed class WindowPowerEvents : IPowerEvents
{
    public event Action<DateTimeOffset>? Suspend;
    public event Action<DateTimeOffset>? Resume;
    public event Action<DateTimeOffset>? Lock;
    public event Action<DateTimeOffset>? Unlock;
    public event Action<DateTimeOffset>? Shutdown;

    /// <summary>Returns true when the message was a power/session message.</summary>
    public bool Handle(uint msg, nuint wParam, DateTimeOffset now)
    {
        switch (msg)
        {
            case NativeMethods.WM_POWERBROADCAST:
                if (wParam == NativeMethods.PBT_APMSUSPEND) Suspend?.Invoke(now);
                else if (wParam is NativeMethods.PBT_APMRESUMEAUTOMATIC or NativeMethods.PBT_APMRESUMESUSPEND) Resume?.Invoke(now);
                return true;
            case NativeMethods.WM_WTSSESSION_CHANGE:
                if (wParam == NativeMethods.WTS_SESSION_LOCK) Lock?.Invoke(now);
                else if (wParam == NativeMethods.WTS_SESSION_UNLOCK) Unlock?.Invoke(now);
                return true;
            case NativeMethods.WM_ENDSESSION:
                if (wParam != 0) Shutdown?.Invoke(now);
                return true;
        }
        return false;
    }

    public void RaiseShutdown(DateTimeOffset now) => Shutdown?.Invoke(now);
}
