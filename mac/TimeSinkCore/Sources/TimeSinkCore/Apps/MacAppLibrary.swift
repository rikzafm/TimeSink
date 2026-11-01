import Foundation
import AppKit

public struct TrackedAppItem: Sendable, Identifiable, Equatable {
    public let id: Int64
    public let canonicalId: Int64?
    public let exePath: String
    public let displayName: String
    public let category: String
    public let state: String
    public let addedUtc: Int64
    public let isBackground: Bool
    public let kind: AppKind
    public let deviceId: String
    public let mergedCount: Int
    public let totalSeconds: Int64

    public var isTracked: Bool { state == "tracked" }
}

public struct InstalledAppItem: Sendable, Identifiable, Equatable {
    public var id: String { bundleUrl.path }
    public let name: String
    public let bundleUrl: URL
    public let executablePath: String
    public let bundleId: String?

    public init(name: String, bundleUrl: URL, executablePath: String, bundleId: String?) {
        self.name = name
        self.bundleUrl = bundleUrl
        self.executablePath = executablePath
        self.bundleId = bundleId
    }
}

public final class MacAppLibrary: @unchecked Sendable {
    private let db: SQLiteDatabase

    public init(db: SQLiteDatabase) {
        self.db = db
    }

    /// Tracks an application. Returns the app id.
    @discardableResult
    public func trackApp(
        exePath: String,
        displayName: String,
        category: String = "Other",
        kind: AppKind = .other,
        isBackground: Bool = false,
        deviceId: String
    ) throws -> Int64 {
        let now = Int64(Date().timeIntervalSince1970)
        let stmt = try db.prepare("""
            INSERT INTO apps (exe_path, display_name, category, state, added_utc, is_background, kind, device_id)
            VALUES (?1, ?2, ?3, 'tracked', ?4, ?5, ?6, ?7)
            ON CONFLICT(device_id, exe_path) DO UPDATE SET
                state = 'tracked',
                display_name = excluded.display_name,
                category = excluded.category,
                kind = excluded.kind,
                is_background = excluded.is_background;
            """)
        try stmt.bind(text: exePath, at: 1)
        try stmt.bind(text: displayName, at: 2)
        try stmt.bind(text: category, at: 3)
        try stmt.bind(int64: now, at: 4)
        try stmt.bind(int64: isBackground ? 1 : 0, at: 5)
        try stmt.bind(int64: kind.rawValue, at: 6)
        try stmt.bind(text: deviceId, at: 7)
        _ = try stmt.step()

        return db.lastInsertRowid
    }

    public func stopTracking(appId: Int64) throws {
        let now = Int64(Date().timeIntervalSince1970)
        let stmt = try db.prepare("""
            UPDATE apps SET state = 'retired', retired_utc = ?1 WHERE id = ?2;
            """)
        try stmt.bind(int64: now, at: 1)
        try stmt.bind(int64: appId, at: 2)
        _ = try stmt.step()
    }

    public func clearHistory(appId: Int64) throws {
        try db.transaction {
            let s1 = try db.prepare("DELETE FROM sessions WHERE app_id = ?1;")
            try s1.bind(int64: appId, at: 1)
            _ = try s1.step()

            let s2 = try db.prepare("DELETE FROM daily_usage WHERE app_id = ?1;")
            try s2.bind(int64: appId, at: 1)
            _ = try s2.step()
        }
    }

    public func updateApp(appId: Int64, displayName: String, kind: AppKind, isBackground: Bool) throws {
        let stmt = try db.prepare("""
            UPDATE apps SET display_name = ?1, kind = ?2, is_background = ?3 WHERE id = ?4;
            """)
        try stmt.bind(text: displayName, at: 1)
        try stmt.bind(int64: kind.rawValue, at: 2)
        try stmt.bind(int64: isBackground ? 1 : 0, at: 3)
        try stmt.bind(int64: appId, at: 4)
        _ = try stmt.step()
    }

    public func merge(mainId: Int64, candidateIds: [Int64]) throws {
        try db.transaction {
            for cId in candidateIds where cId != mainId {
                let stmt = try db.prepare("UPDATE apps SET canonical_id = ?1 WHERE id = ?2;")
                try stmt.bind(int64: mainId, at: 1)
                try stmt.bind(int64: cId, at: 2)
                _ = try stmt.step()
            }
        }
    }

    public func unmerge(appId: Int64) throws {
        let stmt = try db.prepare("UPDATE apps SET canonical_id = NULL WHERE id = ?1;")
        try stmt.bind(int64: appId, at: 1)
        _ = try stmt.step()
    }

    public func getTrackedApps(deviceId: String? = nil) throws -> [TrackedAppItem] {
        var sql = """
            SELECT a.id, a.canonical_id, a.exe_path, a.display_name, a.category, a.state,
                   a.added_utc, a.is_background, a.kind, a.device_id,
                   (SELECT COUNT(*) FROM apps m WHERE m.canonical_id = a.id) as merged_count,
                   (SELECT COALESCE(SUM(u.seconds), 0) FROM daily_usage u WHERE u.app_id = a.id OR u.app_id IN (SELECT m.id FROM apps m WHERE m.canonical_id = a.id)) as total_sec
            FROM apps a
            WHERE a.state = 'tracked' AND a.canonical_id IS NULL
            """
        if let dev = deviceId, !dev.isEmpty {
            sql += " AND a.device_id = '\(dev)'"
        }
        sql += " ORDER BY a.display_name COLLATE NOCASE ASC;"

        let stmt = try db.prepare(sql)
        var items: [TrackedAppItem] = []
        while try stmt.step() {
            let id = stmt.columnInt64(at: 0)
            let canon = stmt.columnInt64(at: 1) == 0 ? nil : stmt.columnInt64(at: 1)
            let path = stmt.columnText(at: 2) ?? ""
            let name = stmt.columnText(at: 3) ?? ""
            let cat = stmt.columnText(at: 4) ?? "Other"
            let state = stmt.columnText(at: 5) ?? "tracked"
            let added = stmt.columnInt64(at: 6)
            let bg = stmt.columnInt64(at: 7) == 1
            let kind = AppKind(rawValue: stmt.columnInt64(at: 8)) ?? .other
            let dev = stmt.columnText(at: 9) ?? ""
            let merged = Int(stmt.columnInt64(at: 10))
            let sec = stmt.columnInt64(at: 11)

            items.append(TrackedAppItem(
                id: id,
                canonicalId: canon,
                exePath: path,
                displayName: name,
                category: cat,
                state: state,
                addedUtc: added,
                isBackground: bg,
                kind: kind,
                deviceId: dev,
                mergedCount: merged,
                totalSeconds: sec
            ))
        }
        return items
    }

    public func getMergedChildren(mainId: Int64) throws -> [TrackedAppItem] {
        let sql = """
            SELECT a.id, a.canonical_id, a.exe_path, a.display_name, a.category, a.state,
                   a.added_utc, a.is_background, a.kind, a.device_id, 0,
                   (SELECT COALESCE(SUM(u.seconds), 0) FROM daily_usage u WHERE u.app_id = a.id) as total_sec
            FROM apps a
            WHERE a.canonical_id = ?1
            ORDER BY a.display_name COLLATE NOCASE ASC;
            """
        let stmt = try db.prepare(sql)
        try stmt.bind(int64: mainId, at: 1)
        var items: [TrackedAppItem] = []
        while try stmt.step() {
            items.append(TrackedAppItem(
                id: stmt.columnInt64(at: 0),
                canonicalId: stmt.columnInt64(at: 1),
                exePath: stmt.columnText(at: 2) ?? "",
                displayName: stmt.columnText(at: 3) ?? "",
                category: stmt.columnText(at: 4) ?? "Other",
                state: stmt.columnText(at: 5) ?? "tracked",
                addedUtc: stmt.columnInt64(at: 6),
                isBackground: stmt.columnInt64(at: 7) == 1,
                kind: AppKind(rawValue: stmt.columnInt64(at: 8)) ?? .other,
                deviceId: stmt.columnText(at: 9) ?? "",
                mergedCount: 0,
                totalSeconds: stmt.columnInt64(at: 11)
            ))
        }
        return items
    }

    public func openMergePlan(mainId: Int64) throws -> MergePlan {
        let mainStmt = try db.prepare("SELECT display_name, device_id FROM apps WHERE id = ?1;")
        try mainStmt.bind(int64: mainId, at: 1)
        guard try mainStmt.step() else {
            throw SQLiteError.stepFailed("App not found: \(mainId)")
        }
        let mainName = mainStmt.columnText(at: 0) ?? ""
        let mainDevId = mainStmt.columnText(at: 1) ?? ""

        // Find candidates
        let candStmt = try db.prepare("""
            SELECT a.id, a.display_name, a.exe_path, a.canonical_id, a.device_id,
                   (SELECT COALESCE(SUM(seconds), 0) FROM daily_usage WHERE app_id = a.id) as sec
            FROM apps a
            WHERE a.id != ?1
            ORDER BY a.display_name COLLATE NOCASE ASC;
            """)
        try candStmt.bind(int64: mainId, at: 1)
        var candidates: [MergeCandidate] = []
        while try candStmt.step() {
            let id = candStmt.columnInt64(at: 0)
            let name = candStmt.columnText(at: 1) ?? ""
            let path = candStmt.columnText(at: 2) ?? ""
            let canon = candStmt.columnInt64(at: 3)
            let dev = candStmt.columnText(at: 4) ?? ""
            let sec = candStmt.columnInt64(at: 5)
            let kind: CandidateKind = (canon == mainId) ? .merged : .tracked

            candidates.append(MergeCandidate(
                key: "app:\(id)",
                kind: kind,
                name: name,
                exePath: path,
                appId: id,
                seconds: sec,
                deviceId: dev
            ))
        }

        return MergePlan(
            mainId: mainId,
            mainName: mainName,
            candidates: candidates,
            mainDeviceId: mainDevId
        )
    }

    public static func scanInstalledApplications() -> [InstalledAppItem] {
        let fm = FileManager.default
        let searchDirs: [URL] = [
            URL(fileURLWithPath: "/Applications"),
            URL(fileURLWithPath: "/System/Applications"),
            fm.homeDirectoryForCurrentUser.appendingPathComponent("Applications")
        ]

        var results: [InstalledAppItem] = []
        var seenPaths = Set<String>()

        for dir in searchDirs {
            guard let contents = try? fm.contentsOfDirectory(at: dir, includingPropertiesForKeys: nil, options: [.skipsHiddenFiles]) else {
                continue
            }
            for url in contents where url.pathExtension == "app" {
                let path = url.path
                if seenPaths.contains(path) { continue }
                seenPaths.insert(path)

                let bundle = Bundle(url: url)
                let name = bundle?.localizedInfoDictionary?["CFBundleDisplayName"] as? String
                    ?? bundle?.infoDictionary?["CFBundleName"] as? String
                    ?? url.deletingPathExtension().lastPathComponent
                let bundleId = bundle?.bundleIdentifier
                let execPath = bundle?.executableURL?.path ?? path

                results.append(InstalledAppItem(
                    name: name,
                    bundleUrl: url,
                    executablePath: execPath,
                    bundleId: bundleId
                ))
            }
        }

        return results.sorted(by: { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending })
    }
}
