import Foundation

/// Deterministic tracking state machine. No OS calls, no timers: the host feeds it ticks and power events.
/// All instants are handled as UTC unix seconds.
/// Invariant: for every open session, the usage accumulated so far covers exactly [start, last_seen].
public final class TrackingEngine: @unchecked Sendable {
    private let gapSeconds: Int64 = 20
    private let heartbeatSeconds: Int64 = 30
    private let recoveryFreshSeconds: Int64 = 120

    private final class Open {
        let sessionId: Int64
        let appId: Int64
        let start: Int64
        var accumulatedTo: Int64

        init(sessionId: Int64, appId: Int64, start: Int64, accumulatedTo: Int64) {
            self.sessionId = sessionId
            self.appId = appId
            self.start = start
            self.accumulatedTo = accumulatedTo
        }
    }

    private let store: SessionStore
    private let processes: ProcessProvider
    private let clock: Clock
    private let settings: TrackingSettings
    private let usage: UsageAccumulator
    private var open: [Int64: Open] = [:]

    private var apps: [TrackedApp] = []
    private var appsVersion: Int64?
    private var lastTick: Int64?
    private var lastHeartbeat: Int64
    private var floor: Int64
    private var suspended: Bool = false
    private var locked: Bool = false
    private var stopped: Bool = false

    public init(
        store: SessionStore,
        processes: ProcessProvider,
        clock: Clock,
        settings: TrackingSettings,
        timeZone: TimeZone
    ) throws {
        self.store = store
        self.processes = processes
        self.clock = clock
        self.settings = settings
        self.usage = UsageAccumulator(store: store, timeZone: timeZone)
        let nowSec = Int64(clock.utcNow.timeIntervalSince1970)
        self.floor = nowSec
        self.lastHeartbeat = nowSec
        self.appsVersion = store.dataVersion
        self.apps = try store.getTrackedApps()
    }

    /// Wires power/session events from a power hub to the engine.
    public func attach(power: PowerEventHub) {
        power.onSuspend.append { [weak self] t in
            try? self?.onSuspend(t)
        }
        power.onResume.append { [weak self] t in
            try? self?.onResume(t)
        }
        power.onLock.append { [weak self] t in
            try? self?.onLock(t)
        }
        power.onUnlock.append { [weak self] t in
            try? self?.onUnlock(t)
        }
        power.onShutdown.append { [weak self] t in
            try? self?.onShutdown(t)
        }
    }

    public func onTick(_ now: Date) throws {
        if stopped || suspended { return }
        let n = Int64(now.timeIntervalSince1970)

        if let last = lastTick {
            if n < last {
                try closeAll(end: last, reason: EndReasons.exit) // clock moved backwards
                floor = n
                lastHeartbeat = n
            } else if n - last > gapSeconds && settings.pauseOnSleep {
                try closeAll(end: last, reason: EndReasons.sleep) // missed suspend notification
                floor = n
            }
        } else {
            lastHeartbeat = n
        }

        try detect(n)
        if n - lastHeartbeat >= heartbeatSeconds {
            for o in open.values {
                try accumulate(o, to: n)
            }
            lastHeartbeat = n
        }
        lastTick = n
    }

    public func onSuspend(_ now: Date) throws {
        if stopped || !settings.pauseOnSleep { return }
        suspended = true
        try closeAll(end: Int64(now.timeIntervalSince1970), reason: EndReasons.sleep)
    }

    public func onResume(_ now: Date) throws {
        if stopped { return }
        let n = Int64(now.timeIntervalSince1970)
        suspended = false
        if settings.pauseOnSleep {
            try closeAll(end: min(n, lastTick ?? n), reason: EndReasons.sleep) // safety net
        }
        floor = n
        lastTick = n
        lastHeartbeat = n
        try detect(n)
    }

    public func onLock(_ now: Date) throws {
        if stopped || settings.countWhenLocked { return }
        locked = true
        try closeAll(end: Int64(now.timeIntervalSince1970), reason: EndReasons.sleep)
    }

    public func onUnlock(_ now: Date) throws {
        if stopped || !locked { return }
        let n = Int64(now.timeIntervalSince1970)
        locked = false
        floor = n
        try detect(n)
    }

    public func onShutdown(_ now: Date) throws {
        if stopped { return }
        try closeAll(end: Int64(now.timeIntervalSince1970), reason: EndReasons.shutdown)
        stopped = true
    }

    /// Forgets all in-memory open sessions without touching the database.
    public func discardOpenSessions(_ now: Date) throws {
        open.removeAll()
        floor = Int64(now.timeIntervalSince1970)
        lastTick = nil
        lastHeartbeat = floor
        appsVersion = store.dataVersion
        apps = try store.getTrackedApps()
    }

    /// Re-reads the tracked apps; sessions of apps that are no longer tracked are closed as 'removed'.
    public func reloadTrackedApps() throws {
        let version = store.dataVersion
        if version != nil && version == appsVersion { return }
        appsVersion = version
        apps = try store.getTrackedApps()
        let tracked = Set(apps.map { $0.id })
        let now = Int64(clock.utcNow.timeIntervalSince1970)
        for o in Array(open.values) where !tracked.contains(o.appId) {
            try close(o, end: now, reason: EndReasons.removed)
        }
    }

    /// At start: continue sessions whose process is still the same and recently seen, close the rest.
    public func recoverFromCrash(_ now: Date) throws {
        let n = Int64(now.timeIntervalSince1970)
        appsVersion = store.dataVersion
        apps = try store.getTrackedApps()
        let tracked = Set(apps.map { $0.id })
        let procs = processes.snapshot()

        for s in try store.getOpenSessions() {
            let o = Open(sessionId: s.id, appId: s.appId, start: s.startUtc, accumulatedTo: s.lastSeenUtc)
            let alive = s.pid != nil && s.procStartUtc != nil &&
                procs.contains { p in
                    p.pid == s.pid! && abs(Int64(p.startUtc.timeIntervalSince1970) - s.procStartUtc!) <= 1
                }
            if alive && tracked.contains(s.appId) && n - s.lastSeenUtc <= recoveryFreshSeconds {
                open[s.appId] = o
            } else {
                try close(o, end: s.lastSeenUtc, reason: EndReasons.crash)
            }
        }
    }

    // MARK: - Internals

    private func detect(_ n: Int64) throws {
        let blocked = suspended || (locked && !settings.countWhenLocked)
        let procs = processes.snapshot()
        for app in apps {
            let matching = procs.filter { matches(app: app, proc: $0) }
            if let o = open[app.id] {
                if matching.isEmpty {
                    try close(o, end: n, reason: EndReasons.exit)
                }
            } else if !matching.isEmpty && !blocked {
                guard let first = matching.min(by: { $0.startUtc < $1.startUtc }) else { continue }
                let firstStart = Int64(first.startUtc.timeIntervalSince1970)
                let start = max(max(firstStart, floor), app.addedUtc)
                if start > n { continue }
                let rec = try store.openSession(appId: app.id, startUtc: start, pid: first.pid, procStartUtc: firstStart)
                open[app.id] = Open(sessionId: rec.id, appId: app.id, start: rec.startUtc, accumulatedTo: rec.startUtc)
            }
        }
    }

    private func matches(app: TrackedApp, proc: ProcInfo) -> Bool {
        if let bundle = proc.bundleId, !bundle.isEmpty {
            if let appBundle = app.bundleId, appBundle.caseInsensitiveCompare(bundle) == .orderedSame {
                return true
            }
            if let appPkg = app.packageFamily, appPkg.caseInsensitiveCompare(bundle) == .orderedSame {
                return true
            }
        }
        if let fullPath = proc.fullPath {
            if fullPath.caseInsensitiveCompare(app.exePath) == .orderedSame {
                return true
            }
            if let pkg = app.packageFamily, let procPkg = proc.packageFamily,
               pkg.caseInsensitiveCompare(procPkg) == .orderedSame {
                return true
            }
            if let bundle = app.bundleId, let procPkg = proc.packageFamily,
               bundle.caseInsensitiveCompare(procPkg) == .orderedSame {
                return true
            }
        } else {
            // Unreadable process (elevated/protected): fall back to the image name when unambiguous.
            let appFile = (app.exePath.replacingOccurrences(of: "\\", with: "/") as NSString).lastPathComponent
            let isUniqueInApps = apps.filter {
                ($0.exePath.replacingOccurrences(of: "\\", with: "/") as NSString).lastPathComponent.caseInsensitiveCompare(appFile) == .orderedSame
            }.count == 1
            if isUniqueInApps && proc.name.caseInsensitiveCompare(appFile) == .orderedSame {
                return true
            }
            if let bundle = app.bundleId, bundle.caseInsensitiveCompare(proc.name) == .orderedSame {
                return true
            }
        }
        return false
    }

    private func accumulate(_ o: Open, to: Int64) throws {
        if to <= o.accumulatedTo { return }
        try usage.add(appId: o.appId, fromUtc: o.accumulatedTo, toUtc: to)
        o.accumulatedTo = to
        try store.heartbeat(sessionId: o.sessionId, lastSeenUtc: to)
    }

    private func closeAll(end: Int64, reason: String) throws {
        for o in Array(open.values) {
            try close(o, end: end, reason: reason)
        }
    }

    private func close(_ o: Open, end: Int64, reason: String) throws {
        let effectiveEnd = max(end, o.start)
        try usage.add(appId: o.appId, fromUtc: o.accumulatedTo, toUtc: effectiveEnd) // signed: also trims if we had accumulated past 'end'
        if effectiveEnd - o.start < Int64(settings.minSessionSeconds) {
            try usage.add(appId: o.appId, fromUtc: effectiveEnd, toUtc: o.start) // subtract everything counted for this session
            try store.deleteSession(sessionId: o.sessionId)
        } else {
            try store.closeSession(sessionId: o.sessionId, endUtc: effectiveEnd, reason: reason)
        }
        open.removeValue(forKey: o.appId)
    }
}
