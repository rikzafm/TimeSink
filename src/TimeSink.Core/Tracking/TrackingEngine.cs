using TimeSink.Core.Usage;

namespace TimeSink.Core.Tracking;

/// <summary>
/// Deterministic tracking state machine (implementation.md section 6). No Win32 calls, no timers: the Agent
/// feeds it ticks and power events. All instants are handled as UTC unix seconds.
/// Invariant: for every open session, the usage accumulated so far covers exactly [start, last_seen].
/// </summary>
public sealed class TrackingEngine
{
    private const int GapSeconds = 20;
    private const int HeartbeatSeconds = 30;
    private const int RecoveryFreshSeconds = 120;

    private sealed class Open
    {
        public required long SessionId { get; init; }
        public required long AppId { get; init; }
        public required long Start { get; init; }
        public long AccumulatedTo { get; set; }
    }

    private readonly ISessionStore _store;
    private readonly IProcessProvider _processes;
    private readonly IClock _clock;
    private readonly ITrackingSettings _settings;
    private readonly UsageAccumulator _usage;
    private readonly Dictionary<long, Open> _open = new();

    private IReadOnlyList<TrackedApp> _apps = Array.Empty<TrackedApp>();
    private long? _appsVersion;    // store.DataVersion when _apps was read
    private long? _lastTick;
    private long _lastHeartbeat;
    private long _floor;           // nothing before this is counted (engine start / resume / unlock / clock change)
    private bool _suspended, _locked, _stopped;

    public TrackingEngine(ISessionStore store, IProcessProvider processes, IClock clock, ITrackingSettings settings, TimeZoneInfo zone)
    {
        _store = store;
        _processes = processes;
        _clock = clock;
        _settings = settings;
        _usage = new UsageAccumulator(store, zone);
        _floor = clock.UtcNow.ToUnixTimeSeconds();
        _appsVersion = store.DataVersion;
        _apps = store.GetTrackedApps();
    }

    /// <summary>Wires the power/session events to the engine.</summary>
    public void Attach(IPowerEvents power)
    {
        power.Suspend += OnSuspend;
        power.Resume += OnResume;
        power.Lock += OnLock;
        power.Unlock += OnUnlock;
        power.Shutdown += OnShutdown;
    }

    public void OnTick(DateTimeOffset now)
    {
        if (_stopped || _suspended) return;
        var n = Unix(now);

        if (_lastTick is { } last)
        {
            if (n < last)
            {
                CloseAll(last, EndReasons.Exit);   // clock moved backwards
                _floor = n;
                _lastHeartbeat = n;
            }
            else if (n - last > GapSeconds && _settings.PauseOnSleep)
            {
                CloseAll(last, EndReasons.Sleep);  // missed suspend notification
                _floor = n;
            }
        }
        else
        {
            _lastHeartbeat = n;
        }

        Detect(n);
        if (n - _lastHeartbeat >= HeartbeatSeconds)
        {
            foreach (var o in _open.Values) Accumulate(o, n);
            _lastHeartbeat = n;
        }
        _lastTick = n;
    }

    public void OnSuspend(DateTimeOffset now)
    {
        if (_stopped || !_settings.PauseOnSleep) return;
        _suspended = true;
        CloseAll(Unix(now), EndReasons.Sleep);
    }

    public void OnResume(DateTimeOffset now)
    {
        if (_stopped) return;
        var n = Unix(now);
        _suspended = false;
        if (_settings.PauseOnSleep) CloseAll(Math.Min(n, _lastTick ?? n), EndReasons.Sleep); // safety net
        _floor = n;
        _lastTick = n;
        _lastHeartbeat = n;
        Detect(n);
    }

    public void OnLock(DateTimeOffset now)
    {
        if (_stopped || _settings.CountWhenLocked) return;
        _locked = true;
        CloseAll(Unix(now), EndReasons.Sleep);
    }

    public void OnUnlock(DateTimeOffset now)
    {
        if (_stopped || !_locked) return;
        var n = Unix(now);
        _locked = false;
        _floor = n;
        Detect(n);
    }

    public void OnShutdown(DateTimeOffset now)
    {
        if (_stopped) return;
        CloseAll(Unix(now), EndReasons.Shutdown);
        _stopped = true;
    }

    /// <summary>
    /// Forgets all in-memory open sessions without touching the database. Used after the database was replaced or
    /// its history deleted by restore/delete: still-running apps get new sessions starting at <paramref name="now"/>.
    /// </summary>
    public void DiscardOpenSessions(DateTimeOffset now)
    {
        _open.Clear();
        _floor = Unix(now);
        _lastTick = null;
        _lastHeartbeat = _floor;
        _appsVersion = _store.DataVersion;
        _apps = _store.GetTrackedApps();
    }

    /// <summary>Re-reads the tracked apps; sessions of apps that are no longer tracked are closed as 'removed'.</summary>
    public void ReloadTrackedApps()
    {
        var version = _store.DataVersion; // read before the apps, so a commit in between triggers another reload
        if (version is not null && version == _appsVersion) return;
        _appsVersion = version;
        _apps = _store.GetTrackedApps();
        var tracked = _apps.Select(a => a.Id).ToHashSet();
        var now = Unix(_clock.UtcNow);
        foreach (var o in _open.Values.Where(o => !tracked.Contains(o.AppId)).ToList())
            Close(o, now, EndReasons.Removed);
    }

    /// <summary>At Agent start: continue sessions whose process is still the same and recently seen, close the rest.</summary>
    public void RecoverFromCrash(DateTimeOffset now)
    {
        var n = Unix(now);
        _appsVersion = _store.DataVersion;
        _apps = _store.GetTrackedApps();
        var tracked = _apps.Select(a => a.Id).ToHashSet();
        var procs = _processes.Snapshot();

        foreach (var s in _store.GetOpenSessions())
        {
            var o = new Open { SessionId = s.Id, AppId = s.AppId, Start = s.StartUtc, AccumulatedTo = s.LastSeenUtc };
            var alive = s.Pid is { } pid && s.ProcStartUtc is { } ps &&
                        procs.Any(p => p.Pid == pid && Math.Abs(Unix(p.StartUtc) - ps) <= 1);
            if (alive && tracked.Contains(s.AppId) && n - s.LastSeenUtc <= RecoveryFreshSeconds)
                _open[s.AppId] = o;
            else
                Close(o, s.LastSeenUtc, EndReasons.Crash);
        }
    }

    // ---- internals --------------------------------------------------------

    private void Detect(long n)
    {
        var blocked = _suspended || (_locked && !_settings.CountWhenLocked);
        var procs = _processes.Snapshot();
        foreach (var app in _apps)
        {
            var matching = procs.Where(p => Matches(app, p)).ToList();
            if (_open.TryGetValue(app.Id, out var o))
            {
                if (matching.Count == 0) Close(o, n, EndReasons.Exit);
            }
            else if (matching.Count > 0 && !blocked)
            {
                var first = matching.MinBy(p => p.StartUtc)!;
                var start = Math.Max(Math.Max(Unix(first.StartUtc), _floor), app.AddedUtc);
                if (start > n) continue;
                var rec = _store.OpenSession(app.Id, start, first.Pid, Unix(first.StartUtc));
                _open[app.Id] = new Open { SessionId = rec.Id, AppId = app.Id, Start = rec.StartUtc, AccumulatedTo = rec.StartUtc };
            }
        }
    }

    private bool Matches(TrackedApp app, ProcInfo p)
    {
        if (p.FullPath is not null)
            return string.Equals(p.FullPath, app.ExePath, StringComparison.OrdinalIgnoreCase)
                   || (app.PackageFamily is not null && string.Equals(p.PackageFamily, app.PackageFamily, StringComparison.OrdinalIgnoreCase));
        // Unreadable process (elevated/protected): fall back to the image name when it is unambiguous.
        var file = Path.GetFileName(app.ExePath);
        return string.Equals(p.Name, file, StringComparison.OrdinalIgnoreCase)
               && _apps.Count(a => string.Equals(Path.GetFileName(a.ExePath), file, StringComparison.OrdinalIgnoreCase)) == 1;
    }

    private void Accumulate(Open o, long to)
    {
        if (to <= o.AccumulatedTo) return;
        _usage.Add(o.AppId, o.AccumulatedTo, to);
        o.AccumulatedTo = to;
        _store.Heartbeat(o.SessionId, to);
    }

    private void CloseAll(long end, string reason)
    {
        foreach (var o in _open.Values.ToList()) Close(o, end, reason);
    }

    private void Close(Open o, long end, string reason)
    {
        end = Math.Max(end, o.Start);
        _usage.Add(o.AppId, o.AccumulatedTo, end);   // signed: also trims if we had accumulated past 'end'
        if (end - o.Start < _settings.MinSessionSeconds)
        {
            _usage.Add(o.AppId, end, o.Start);       // subtract everything counted for this session
            _store.DeleteSession(o.SessionId);
        }
        else
        {
            _store.CloseSession(o.SessionId, end, reason);
        }
        _open.Remove(o.AppId);
    }

    private static long Unix(DateTimeOffset t) => t.ToUnixTimeSeconds();
}
