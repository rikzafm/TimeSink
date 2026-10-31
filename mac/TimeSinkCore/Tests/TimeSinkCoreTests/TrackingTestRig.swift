import Foundation
import XCTest
@testable import TimeSinkCore

public final class FakeClock: Clock, @unchecked Sendable {
    public var utcNow: Date

    public init(base: Date) {
        self.utcNow = base
    }
}

public final class FakeProcesses: ProcessProvider, @unchecked Sendable {
    public var running: [ProcInfo] = []

    public init() {}

    public func snapshot() -> [ProcInfo] {
        running
    }
}

public final class TrackingTestRig {
    public static let baseDate = Date(timeIntervalSince1970: 1773144000) // 2026-03-10T12:00:00Z
    public static let notepadPath = "c:\\windows\\notepad.exe"

    public let tempDir: URL
    public let dbPath: String
    public let db: SQLiteDatabase
    public let store: SQLiteSessionStore
    public let baseTime: Date
    public let clock: FakeClock
    public let procs: FakeProcesses
    public let power: PowerEventHub
    public let settings: InMemoryTrackingSettings
    public let timeZone: TimeZone
    public var engine: TrackingEngine!
    public let usage: UsageAccumulator
    public var defaultAppId: Int64 = 1
    public var deviceId: String = ""

    public init(timeZone: TimeZone? = nil, baseTime: Date? = nil, addedUtc: Int64 = 0) throws {
        self.baseTime = baseTime ?? Self.baseDate
        self.timeZone = timeZone ?? TimeZone(secondsFromGMT: 0)!
        self.clock = FakeClock(base: self.baseTime)
        self.procs = FakeProcesses()
        self.power = PowerEventHub()
        self.settings = InMemoryTrackingSettings(minSessionSeconds: 15)

        self.tempDir = FileManager.default.temporaryDirectory.appendingPathComponent("timesink-test-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: tempDir, withIntermediateDirectories: true)
        self.dbPath = tempDir.appendingPathComponent("test.db").path

        self.db = try SQLiteDatabase(path: dbPath)

        // Apply migrations
        try Migrator.apply(to: db)

        self.deviceId = try db.scalarText("SELECT device_id FROM devices WHERE is_local = 1;") ?? "dev-local"

        self.store = SQLiteSessionStore(db: db)
        self.usage = UsageAccumulator(store: store, timeZone: self.timeZone)

        // Insert default app
        let insertStmt = try db.prepare("""
        INSERT INTO apps (exe_path, display_name, bundle_id, added_utc, device_id, state)
        VALUES (?1, ?2, ?3, ?4, ?5, 'tracked');
        """)
        try insertStmt.bind(text: Self.notepadPath, at: 1)
        try insertStmt.bind(text: "Notepad", at: 2)
        try insertStmt.bind(text: "com.microsoft.Notepad", at: 3)
        try insertStmt.bind(int64: addedUtc, at: 4)
        try insertStmt.bind(text: deviceId, at: 5)
        _ = try insertStmt.step()
        self.defaultAppId = db.lastInsertRowid

        self.engine = try newEngine()
    }

    deinit {
        db.close()
        try? FileManager.default.removeItem(at: tempDir)
    }

    public func newEngine() throws -> TrackingEngine {
        let eng = try TrackingEngine(
            store: store,
            processes: procs,
            clock: clock,
            settings: settings,
            timeZone: timeZone
        )
        eng.attach(power: power)
        self.engine = eng
        return eng
    }

    public func atTime(_ seconds: Int) -> Date {
        baseTime.addingTimeInterval(TimeInterval(seconds))
    }

    public func notepadProc(pid: Int32, startSeconds: Int = -100000) -> ProcInfo {
        ProcInfo(
            pid: pid,
            name: "notepad.exe",
            fullPath: Self.notepadPath,
            startUtc: atTime(startSeconds),
            bundleId: "com.microsoft.Notepad"
        )
    }

    public func tick(_ seconds: Int) throws {
        let t = atTime(seconds)
        clock.utcNow = t
        try engine.onTick(t)
    }

    public func tickEvery5(from: Int, to: Int) throws {
        for s in stride(from: from, through: to, by: 5) {
            try tick(s)
        }
    }

    public func getAllSessions() throws -> [SessionRecord] {
        try store.getAllSessions()
    }

    public func dailyUsageMap(appId: Int64? = nil) throws -> [String: Int64] {
        var map: [String: Int64] = [:]
        for r in try store.getDailyUsage() {
            if appId == nil || r.appId == appId {
                map[r.day, default: 0] += r.seconds
            }
        }
        return map
    }

    public func externalExec(_ sql: String) throws {
        var finalSql = sql
        if finalSql.localizedCaseInsensitiveContains("INSERT INTO apps") && !finalSql.localizedCaseInsensitiveContains("device_id") {
            finalSql = finalSql.replacingOccurrences(of: "INSERT INTO apps (", with: "INSERT INTO apps (device_id, ")
            finalSql = finalSql.replacingOccurrences(of: "INSERT INTO apps(", with: "INSERT INTO apps(device_id, ")
            finalSql = finalSql.replacingOccurrences(of: "VALUES (", with: "VALUES ('\(deviceId)', ")
            finalSql = finalSql.replacingOccurrences(of: "VALUES(", with: "VALUES('\(deviceId)', ")
        }
        let extDb = try SQLiteDatabase(path: dbPath)
        try extDb.exec(finalSql)
        extDb.close()
    }

    public func insertApp(path: String, name: String, bundleId: String? = nil, addedUtc: Int64 = 0) throws -> Int64 {
        let stmt = try db.prepare("""
        INSERT INTO apps (exe_path, display_name, bundle_id, added_utc, device_id, state)
        VALUES (?1, ?2, ?3, ?4, ?5, 'tracked');
        """)
        try stmt.bind(text: path, at: 1)
        try stmt.bind(text: name, at: 2)
        if let b = bundleId {
            try stmt.bind(text: b, at: 3)
        } else {
            try stmt.bindNull(at: 3)
        }
        try stmt.bind(int64: addedUtc, at: 4)
        try stmt.bind(text: deviceId, at: 5)
        _ = try stmt.step()
        return db.lastInsertRowid
    }
}
