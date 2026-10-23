using TimeSink.Core.Usage;

namespace TimeSink.Core.Tracking;

public sealed class TrackingEngine
{
    public TrackingEngine(ISessionStore store, IProcessProvider processes, IClock clock, ITrackingSettings settings, TimeZoneInfo zone) { }
    public void Attach(IPowerEvents power) { }
    public void OnTick(DateTimeOffset now) => throw new NotImplementedException();
    public void OnSuspend(DateTimeOffset now) => throw new NotImplementedException();
    public void OnResume(DateTimeOffset now) => throw new NotImplementedException();
    public void OnLock(DateTimeOffset now) => throw new NotImplementedException();
    public void OnUnlock(DateTimeOffset now) => throw new NotImplementedException();
    public void OnShutdown(DateTimeOffset now) => throw new NotImplementedException();
    public void ReloadTrackedApps() => throw new NotImplementedException();
    public void RecoverFromCrash(DateTimeOffset now) => throw new NotImplementedException();
}
