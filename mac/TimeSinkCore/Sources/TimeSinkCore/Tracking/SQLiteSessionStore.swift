import Foundation

public final class SQLiteSessionStore: SessionStore, @unchecked Sendable {
    private let db: SQLiteDatabase

    public init(db: SQLiteDatabase) {
        self.db = db
    }

    public var dataVersion: Int64? {
        try? db.scalarInt("PRAGMA data_version;")
    }

    public func getTrackedApps() throws -> [TrackedApp] {
        let hasBundleId = (try db.scalarInt("SELECT COUNT(*) FROM pragma_table_info('apps') WHERE name = 'bundle_id';") ?? 0) > 0
        let sql: String
        if hasBundleId {
            sql = "SELECT id, exe_path, bundle_id, package_family, added_utc FROM apps WHERE state = 'tracked' ORDER BY id;"
        } else {
            sql = "SELECT id, exe_path, NULL, package_family, added_utc FROM apps WHERE state = 'tracked' ORDER BY id;"
        }

        let stmt = try db.prepare(sql)
        var apps: [TrackedApp] = []
        while try stmt.step() {
            let id = stmt.columnInt64(at: 0)
            let exePath = stmt.columnText(at: 1) ?? ""
            let bundleId = stmt.columnIsNull(at: 2) ? nil : stmt.columnText(at: 2)
            let pkg = stmt.columnIsNull(at: 3) ? nil : stmt.columnText(at: 3)
            let addedUtc = stmt.columnInt64(at: 4)
            apps.append(TrackedApp(id: id, exePath: exePath, bundleId: bundleId, packageFamily: pkg, addedUtc: addedUtc))
        }
        return apps
    }

    public func getOpenSessions() throws -> [SessionRecord] {
        try fetchSessions(where: "WHERE end_utc IS NULL")
    }

    public func getAllSessions() throws -> [SessionRecord] {
        try fetchSessions(where: "")
    }

    public func openSession(appId: Int64, startUtc: Int64, pid: Int32?, procStartUtc: Int64?) throws -> SessionRecord {
        var start = startUtc
        while try sessionExists(appId: appId, startUtc: start) {
            start += 1
        }

        let stmt = try db.prepare("""
        INSERT INTO sessions (app_id, start_utc, last_seen_utc, pid, proc_start_utc)
        VALUES (?1, ?2, ?2, ?3, ?4);
        """)
        try stmt.bind(int64: appId, at: 1)
        try stmt.bind(int64: start, at: 2)
        if let pid = pid {
            try stmt.bind(int32: pid, at: 3)
        } else {
            try stmt.bindNull(at: 3)
        }
        if let procStart = procStartUtc {
            try stmt.bind(int64: procStart, at: 4)
        } else {
            try stmt.bindNull(at: 4)
        }
        _ = try stmt.step()

        let id = db.lastInsertRowid
        return SessionRecord(
            id: id,
            appId: appId,
            startUtc: start,
            endUtc: nil,
            lastSeenUtc: start,
            pid: pid,
            procStartUtc: procStartUtc,
            endReason: nil
        )
    }

    public func heartbeat(sessionId: Int64, lastSeenUtc: Int64) throws {
        let stmt = try db.prepare("UPDATE sessions SET last_seen_utc = ?1 WHERE id = ?2;")
        try stmt.bind(int64: lastSeenUtc, at: 1)
        try stmt.bind(int64: sessionId, at: 2)
        _ = try stmt.step()
    }

    public func closeSession(sessionId: Int64, endUtc: Int64, reason: String) throws {
        let stmt = try db.prepare("UPDATE sessions SET end_utc = ?1, last_seen_utc = ?1, end_reason = ?2 WHERE id = ?3;")
        try stmt.bind(int64: endUtc, at: 1)
        try stmt.bind(text: reason, at: 2)
        try stmt.bind(int64: sessionId, at: 3)
        _ = try stmt.step()
    }

    public func deleteSession(sessionId: Int64) throws {
        let stmt = try db.prepare("DELETE FROM sessions WHERE id = ?1;")
        try stmt.bind(int64: sessionId, at: 1)
        _ = try stmt.step()
    }

    public func addDailyUsage(appId: Int64, day: String, deltaSeconds: Int64) throws {
        if deltaSeconds == 0 { return }

        let upsert = try db.prepare("""
        INSERT INTO daily_usage (app_id, day, seconds) VALUES (?1, ?2, ?3)
        ON CONFLICT(app_id, day) DO UPDATE SET seconds = seconds + ?3;
        """)
        try upsert.bind(int64: appId, at: 1)
        try upsert.bind(text: day, at: 2)
        try upsert.bind(int64: deltaSeconds, at: 3)
        _ = try upsert.step()

        let cleanup = try db.prepare("DELETE FROM daily_usage WHERE app_id = ?1 AND day = ?2 AND seconds <= 0;")
        try cleanup.bind(int64: appId, at: 1)
        try cleanup.bind(text: day, at: 2)
        _ = try cleanup.step()
    }

    public func getDailyUsage() throws -> [DailyUsageRow] {
        let stmt = try db.prepare("SELECT app_id, day, seconds FROM daily_usage ORDER BY app_id, day;")
        var rows: [DailyUsageRow] = []
        while try stmt.step() {
            let appId = stmt.columnInt64(at: 0)
            let day = stmt.columnText(at: 1) ?? ""
            let sec = stmt.columnInt64(at: 2)
            rows.append(DailyUsageRow(appId: appId, day: day, seconds: sec))
        }
        return rows
    }

    public func replaceDailyUsage(_ rows: [DailyUsageRow]) throws {
        try db.transaction {
            try db.exec("DELETE FROM daily_usage;")
            let stmt = try db.prepare("INSERT INTO daily_usage (app_id, day, seconds) VALUES (?1, ?2, ?3);")
            for row in rows {
                stmt.reset()
                try stmt.bind(int64: row.appId, at: 1)
                try stmt.bind(text: row.day, at: 2)
                try stmt.bind(int64: row.seconds, at: 3)
                _ = try stmt.step()
            }
        }
    }

    private func sessionExists(appId: Int64, startUtc: Int64) throws -> Bool {
        let stmt = try db.prepare("SELECT 1 FROM sessions WHERE app_id = ?1 AND start_utc = ?2 LIMIT 1;")
        try stmt.bind(int64: appId, at: 1)
        try stmt.bind(int64: startUtc, at: 2)
        return try stmt.step()
    }

    private func fetchSessions(where clause: String) throws -> [SessionRecord] {
        let sql = "SELECT id, app_id, start_utc, end_utc, last_seen_utc, pid, proc_start_utc, end_reason FROM sessions \(clause) ORDER BY id;"
        let stmt = try db.prepare(sql)
        var list: [SessionRecord] = []
        while try stmt.step() {
            let id = stmt.columnInt64(at: 0)
            let appId = stmt.columnInt64(at: 1)
            let start = stmt.columnInt64(at: 2)
            let end = stmt.columnIsNull(at: 3) ? nil : stmt.columnInt64(at: 3)
            let lastSeen = stmt.columnInt64(at: 4)
            let pid = stmt.columnIsNull(at: 5) ? nil : stmt.columnInt32(at: 5)
            let procStart = stmt.columnIsNull(at: 6) ? nil : stmt.columnInt64(at: 6)
            let reason = stmt.columnIsNull(at: 7) ? nil : stmt.columnText(at: 7)
            list.append(SessionRecord(
                id: id,
                appId: appId,
                startUtc: start,
                endUtc: end,
                lastSeenUtc: lastSeen,
                pid: pid,
                procStartUtc: procStart,
                endReason: reason
            ))
        }
        return list
    }
}

