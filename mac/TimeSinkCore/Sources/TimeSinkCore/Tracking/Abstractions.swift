import Foundation

public protocol Clock: Sendable {
    var utcNow: Date { get }
}

public struct SystemClock: Clock, Sendable {
    public init() {}
    public var utcNow: Date { Date() }
}

/// One running process. `fullPath` is nil when it cannot be opened (elevated/protected).
public struct ProcInfo: Sendable, Equatable {
    public let pid: Int32
    public let name: String
    public let fullPath: String?
    public let startUtc: Date
    public let packageFamily: String?
    public let bundleId: String?

    public init(
        pid: Int32,
        name: String,
        fullPath: String?,
        startUtc: Date,
        packageFamily: String? = nil,
        bundleId: String? = nil
    ) {
        self.pid = pid
        self.name = name
        self.fullPath = fullPath
        self.startUtc = startUtc
        self.packageFamily = packageFamily
        self.bundleId = bundleId
    }
}

public protocol ProcessProvider: Sendable {
    func snapshot() -> [ProcInfo]
}

/// Power and session notifications hub.
public final class PowerEventHub: @unchecked Sendable {
    public init() {}

    public var onSuspend: [(@Sendable (Date) -> Void)] = []
    public var onResume: [(@Sendable (Date) -> Void)] = []
    public var onLock: [(@Sendable (Date) -> Void)] = []
    public var onUnlock: [(@Sendable (Date) -> Void)] = []
    public var onShutdown: [(@Sendable (Date) -> Void)] = []

    public func raiseSuspend(_ date: Date) { onSuspend.forEach { $0(date) } }
    public func raiseResume(_ date: Date) { onResume.forEach { $0(date) } }
    public func raiseLock(_ date: Date) { onLock.forEach { $0(date) } }
    public func raiseUnlock(_ date: Date) { onUnlock.forEach { $0(date) } }
    public func raiseShutdown(_ date: Date) { onShutdown.forEach { $0(date) } }
}

/// Live view of the tracking.* settings.
public protocol TrackingSettings: Sendable {
    var pauseOnSleep: Bool { get }
    var countWhenLocked: Bool { get }
    var minSessionSeconds: Int { get }
}

public final class InMemoryTrackingSettings: TrackingSettings, @unchecked Sendable {
    public var pauseOnSleep: Bool
    public var countWhenLocked: Bool
    public var minSessionSeconds: Int

    public init(pauseOnSleep: Bool = true, countWhenLocked: Bool = true, minSessionSeconds: Int = 15) {
        self.pauseOnSleep = pauseOnSleep
        self.countWhenLocked = countWhenLocked
        self.minSessionSeconds = minSessionSeconds
    }
}

public final class DbTrackingSettings: TrackingSettings, @unchecked Sendable {
    private let db: SQLiteDatabase

    public init(db: SQLiteDatabase) {
        self.db = db
    }

    public var pauseOnSleep: Bool {
        (try? db.scalarText("SELECT value FROM settings WHERE key = 'tracking.pauseOnSleep';"))?.lowercased() != "false"
    }

    public var countWhenLocked: Bool {
        (try? db.scalarText("SELECT value FROM settings WHERE key = 'tracking.countWhenLocked';"))?.lowercased() != "false"
    }

    public var minSessionSeconds: Int {
        if let text = try? db.scalarText("SELECT value FROM settings WHERE key = 'tracking.minSessionSeconds';"),
           let val = Int(text) {
            return val
        }
        return 15
    }
}

public struct TrackedApp: Sendable, Equatable {
    public let id: Int64
    public let exePath: String
    public let bundleId: String?
    public let packageFamily: String?
    public let addedUtc: Int64

    public init(id: Int64, exePath: String, bundleId: String? = nil, packageFamily: String? = nil, addedUtc: Int64) {
        self.id = id
        self.exePath = exePath
        self.bundleId = bundleId
        self.packageFamily = packageFamily
        self.addedUtc = addedUtc
    }
}

public enum EndReasons {
    public static let exit = "exit"
    public static let sleep = "sleep"
    public static let shutdown = "shutdown"
    public static let crash = "crash"
    public static let removed = "removed"
    public static let restore = "restore"
}

public struct SessionRecord: Sendable, Equatable {
    public let id: Int64
    public let appId: Int64
    public let startUtc: Int64
    public let endUtc: Int64?
    public let lastSeenUtc: Int64
    public let pid: Int32?
    public let procStartUtc: Int64?
    public let endReason: String?

    public init(
        id: Int64,
        appId: Int64,
        startUtc: Int64,
        endUtc: Int64?,
        lastSeenUtc: Int64,
        pid: Int32?,
        procStartUtc: Int64?,
        endReason: String?
    ) {
        self.id = id
        self.appId = appId
        self.startUtc = startUtc
        self.endUtc = endUtc
        self.lastSeenUtc = lastSeenUtc
        self.pid = pid
        self.procStartUtc = procStartUtc
        self.endReason = endReason
    }
}

public struct DailyUsageRow: Sendable, Equatable {
    public let appId: Int64
    public let day: String
    public let seconds: Int64

    public init(appId: Int64, day: String, seconds: Int64) {
        self.appId = appId
        self.day = day
        self.seconds = seconds
    }
}

public protocol SessionStore: Sendable {
    /// Apps with state 'tracked'.
    func getTrackedApps() throws -> [TrackedApp]

    /// Changes whenever another connection commits to the database; nil when unknown.
    var dataVersion: Int64? { get }

    func getOpenSessions() throws -> [SessionRecord]
    func getAllSessions() throws -> [SessionRecord]

    /// Opens a session. If (app, start) is taken, start moves forward to next free second.
    func openSession(appId: Int64, startUtc: Int64, pid: Int32?, procStartUtc: Int64?) throws -> SessionRecord
    func heartbeat(sessionId: Int64, lastSeenUtc: Int64) throws
    func closeSession(sessionId: Int64, endUtc: Int64, reason: String) throws
    func deleteSession(sessionId: Int64) throws

    /// Adds a (possibly negative) delta; rows reaching <= 0 are removed.
    func addDailyUsage(appId: Int64, day: String, deltaSeconds: Int64) throws
    func getDailyUsage() throws -> [DailyUsageRow]
    func replaceDailyUsage(_ rows: [DailyUsageRow]) throws
}
