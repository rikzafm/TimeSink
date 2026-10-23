namespace TimeSink.Core.Tracking;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>One running process. <see cref="FullPath"/> is null when it cannot be opened (elevated/protected).</summary>
public sealed record ProcInfo(int Pid, string Name, string? FullPath, DateTimeOffset StartUtc, string? PackageFamily = null);

public interface IProcessProvider
{
    IReadOnlyList<ProcInfo> Snapshot();
}

/// <summary>Power/session notifications, each carrying the time they happened.</summary>
public interface IPowerEvents
{
    event Action<DateTimeOffset> Suspend;
    event Action<DateTimeOffset> Resume;
    event Action<DateTimeOffset> Lock;
    event Action<DateTimeOffset> Unlock;
    event Action<DateTimeOffset> Shutdown;
}

/// <summary>Live view of the tracking.* settings; read on every decision so changes apply at once.</summary>
public interface ITrackingSettings
{
    bool PauseOnSleep { get; }
    bool CountWhenLocked { get; }
    int MinSessionSeconds { get; }
}

public sealed class TrackingSettings : ITrackingSettings
{
    public bool PauseOnSleep { get; set; } = true;
    public bool CountWhenLocked { get; set; } = true;
    public int MinSessionSeconds { get; set; } = 15;
}

public sealed record TrackedApp(long Id, string ExePath, string? PackageFamily, long AddedUtc);

public static class EndReasons
{
    public const string Exit = "exit";
    public const string Sleep = "sleep";
    public const string Shutdown = "shutdown";
    public const string Crash = "crash";
    public const string Removed = "removed";
    public const string Restore = "restore";
}

public sealed record SessionRecord(
    long Id, long AppId, long StartUtc, long? EndUtc, long LastSeenUtc, int? Pid, long? ProcStartUtc, string? EndReason);

public sealed record DailyUsageRow(long AppId, string Day, long Seconds);

public interface ISessionStore
{
    /// <summary>Apps with state 'tracked'.</summary>
    IReadOnlyList<TrackedApp> GetTrackedApps();
    IReadOnlyList<SessionRecord> GetOpenSessions();
    IReadOnlyList<SessionRecord> GetAllSessions();
    /// <summary>Opens a session. If (app, start) is taken the start is moved forward to the next free second.</summary>
    SessionRecord OpenSession(long appId, long startUtc, int? pid, long? procStartUtc);
    void Heartbeat(long sessionId, long lastSeenUtc);
    /// <summary>Sets end_utc, end_reason and last_seen_utc = end.</summary>
    void CloseSession(long sessionId, long endUtc, string reason);
    void DeleteSession(long sessionId);
    /// <summary>Adds a (possibly negative) delta; rows reaching 0 or below are removed.</summary>
    void AddDailyUsage(long appId, string day, long deltaSeconds);
    IReadOnlyList<DailyUsageRow> GetDailyUsage();
    void ReplaceDailyUsage(IReadOnlyCollection<DailyUsageRow> rows);
}
