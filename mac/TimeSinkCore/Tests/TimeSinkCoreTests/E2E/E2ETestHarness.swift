import Foundation
import XCTest
@testable import TimeSinkCore

/// Hermetic test harness for E2E opaque-box tests.
/// Manages isolated SQLite databases, simulated hardware/OS events,
/// virtual clocks, and process providers with zero cross-test pollution.
public final class E2ETestHarness {
    public let tempDir: URL
    public let dbPath: String
    public let db: SQLiteDatabase
    public let localDeviceId: String
    public let localDeviceName: String
    public var remoteDeviceId: String?

    public init(name: String = "e2e-test") throws {
        self.tempDir = FileManager.default.temporaryDirectory
            .appendingPathComponent("timesink-\(name)-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: tempDir, withIntermediateDirectories: true)
        self.dbPath = tempDir.appendingPathComponent("timesink.db").path

        self.db = try SQLiteDatabase(path: dbPath)
        try Migrator.apply(to: db)

        self.localDeviceId = try db.scalarText("SELECT value FROM settings WHERE key = 'device.id';")
            ?? (try db.scalarText("SELECT device_id FROM devices WHERE is_local = 1;") ?? "mac-local")
        self.localDeviceName = try db.scalarText("SELECT value FROM settings WHERE key = 'device.name';")
            ?? "Mac"
    }

    deinit {
        db.close()
        try? FileManager.default.removeItem(at: tempDir)
    }

    @discardableResult
    public func addRemoteDevice(
        id: String = "pc-remote-win",
        name: String = "Desktop PC",
        platform: String = "windows",
        state: String = "active"
    ) throws -> String {
        self.remoteDeviceId = id
        let stmt = try db.prepare("""
            INSERT INTO devices (device_id, name, platform, is_local, first_seen_utc, state)
            VALUES (?1, ?2, ?3, 0, ?4, ?5)
            ON CONFLICT(device_id) DO UPDATE SET name = excluded.name, state = excluded.state;
            """)
        try stmt.bind(text: id, at: 1)
        try stmt.bind(text: name, at: 2)
        try stmt.bind(text: platform, at: 3)
        try stmt.bind(text: "2026-01-01T00:00:00Z", at: 4)
        try stmt.bind(text: state, at: 5)
        _ = try stmt.step()
        return id
    }

    @discardableResult
    public func insertApp(
        path: String,
        name: String,
        category: String = "Productivity",
        bundleId: String? = nil,
        kind: AppKind = .other,
        isBackground: Bool = false,
        deviceId: String? = nil,
        addedUtc: Int64 = 1773144000
    ) throws -> Int64 {
        let dev = deviceId ?? localDeviceId
        let stmt = try db.prepare("""
            INSERT INTO apps (exe_path, display_name, category, bundle_id, kind, is_background, device_id, added_utc, state)
            VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, 'tracked');
            """)
        try stmt.bind(text: path, at: 1)
        try stmt.bind(text: name, at: 2)
        try stmt.bind(text: category, at: 3)
        if let b = bundleId {
            try stmt.bind(text: b, at: 4)
        } else {
            try stmt.bindNull(at: 4)
        }
        try stmt.bind(int64: kind.rawValue, at: 5)
        try stmt.bind(int64: isBackground ? 1 : 0, at: 6)
        try stmt.bind(text: dev, at: 7)
        try stmt.bind(int64: addedUtc, at: 8)
        _ = try stmt.step()
        return db.lastInsertRowid
    }

    public func insertSession(
        appId: Int64,
        startUtc: Int64,
        endUtc: Int64?,
        lastSeenUtc: Int64,
        endReason: String? = "exit"
    ) throws {
        let stmt = try db.prepare("""
            INSERT INTO sessions (app_id, start_utc, end_utc, last_seen_utc, end_reason)
            VALUES (?1, ?2, ?3, ?4, ?5);
            """)
        try stmt.bind(int64: appId, at: 1)
        try stmt.bind(int64: startUtc, at: 2)
        if let e = endUtc {
            try stmt.bind(int64: e, at: 3)
        } else {
            try stmt.bindNull(at: 3)
        }
        try stmt.bind(int64: lastSeenUtc, at: 4)
        if let r = endReason {
            try stmt.bind(text: r, at: 5)
        } else {
            try stmt.bindNull(at: 5)
        }
        _ = try stmt.step()
    }

    public func insertDailyUsage(appId: Int64, day: String, seconds: Int64) throws {
        let stmt = try db.prepare("""
            INSERT INTO daily_usage (app_id, day, seconds)
            VALUES (?1, ?2, ?3)
            ON CONFLICT(app_id, day) DO UPDATE SET seconds = seconds + excluded.seconds;
            """)
        try stmt.bind(int64: appId, at: 1)
        try stmt.bind(text: day, at: 2)
        try stmt.bind(int64: seconds, at: 3)
        _ = try stmt.step()
    }

    public func makeContext(
        now: Date = Date(timeIntervalSince1970: 1793534400), // 2026-11-01T12:00:00Z
        zone: TimeZone = TimeZone(secondsFromGMT: 0)!,
        running: Set<Int64>? = nil,
        weekStart: WeekStart = .mon,
        gamesOnly: Bool = false,
        deviceScope: DeviceScope = .this
    ) -> UsageContext {
        UsageContext(
            now: now,
            zone: zone,
            running: running,
            signInUtc: nil,
            weekStart: weekStart,
            gamesOnly: gamesOnly,
            dbPath: dbPath,
            db: db,
            deviceScope: deviceScope,
            localDeviceId: localDeviceId
        )
    }
}
