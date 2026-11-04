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
    public let mergedNames: [String]
    public let mergedDeviceIds: [String]

    public var isTracked: Bool { state == "tracked" }

    public init(
        id: Int64,
        canonicalId: Int64?,
        exePath: String,
        displayName: String,
        category: String,
        state: String,
        addedUtc: Int64,
        isBackground: Bool,
        kind: AppKind,
        deviceId: String,
        mergedCount: Int,
        totalSeconds: Int64,
        mergedNames: [String] = [],
        mergedDeviceIds: [String] = []
    ) {
        self.id = id
        self.canonicalId = canonicalId
        self.exePath = exePath
        self.displayName = displayName
        self.category = category
        self.state = state
        self.addedUtc = addedUtc
        self.isBackground = isBackground
        self.kind = kind
        self.deviceId = deviceId
        self.mergedCount = mergedCount
        self.totalSeconds = totalSeconds
        self.mergedNames = mergedNames
        self.mergedDeviceIds = mergedDeviceIds
    }
}

public struct InstalledAppItem: Sendable, Identifiable, Equatable {
    public var id: String { bundleUrl.path }
    public let name: String
    public let bundleUrl: URL
    public let executablePath: String
    public let bundleId: String?
    public let publisher: String?

    public init(name: String, bundleUrl: URL, executablePath: String, bundleId: String?, publisher: String? = nil) {
        self.name = name
        self.bundleUrl = bundleUrl
        self.executablePath = executablePath
        self.bundleId = bundleId
        self.publisher = publisher
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
        kind: AppKind? = nil,
        isBackground: Bool = false,
        bundleId: String? = nil,
        deviceId: String
    ) throws -> Int64 {
        let now = Int64(Date().timeIntervalSince1970)
        let resolvedKind = kind ?? AppKinds.detect(exePath: exePath, displayName: displayName)
        let resolvedBundleId = (bundleId?.isEmpty == false ? bundleId : nil) ?? Self.resolveBundleId(forPath: exePath)

        let stmt = try db.prepare("""
            INSERT INTO apps (exe_path, display_name, category, state, added_utc, is_background, kind, device_id, bundle_id)
            VALUES (?1, ?2, ?3, 'tracked', ?4, ?5, ?6, ?7, ?8)
            ON CONFLICT(device_id, exe_path) DO UPDATE SET
                state = 'tracked',
                display_name = excluded.display_name,
                category = excluded.category,
                kind = excluded.kind,
                is_background = excluded.is_background,
                bundle_id = COALESCE(excluded.bundle_id, apps.bundle_id);
            """)
        try stmt.bind(text: exePath, at: 1)
        try stmt.bind(text: displayName, at: 2)
        try stmt.bind(text: category, at: 3)
        try stmt.bind(int64: now, at: 4)
        try stmt.bind(int64: isBackground ? 1 : 0, at: 5)
        try stmt.bind(int64: resolvedKind.rawValue, at: 6)
        try stmt.bind(text: deviceId, at: 7)
        if let bid = resolvedBundleId {
            try stmt.bind(text: bid, at: 8)
        } else {
            try stmt.bindNull(at: 8)
        }
        _ = try stmt.step()

        return db.lastInsertRowid
    }

    /// Resolves the bundle identifier for a given file or executable path.
    public static func resolveBundleId(forPath path: String) -> String? {
        let fileUrl = URL(fileURLWithPath: path)
        if let bundle = Bundle(url: fileUrl), let bid = bundle.bundleIdentifier, !bid.isEmpty {
            return bid
        }
        var current = fileUrl
        while current.path != "/" {
            if current.pathExtension == "app" {
                if let b = Bundle(url: current), let bid = b.bundleIdentifier, !bid.isEmpty {
                    return bid
                }
            }
            current.deleteLastPathComponent()
        }
        for app in NSWorkspace.shared.runningApplications {
            if let execPath = app.executableURL?.path, execPath.caseInsensitiveCompare(path) == .orderedSame {
                return app.bundleIdentifier
            }
        }
        return nil
    }

    /// Auto-repairs existing tracked apps that have a missing bundle_id.
    public func repairMissingBundleIds() {
        guard let stmt = try? db.prepare("SELECT id, exe_path FROM apps WHERE bundle_id IS NULL OR bundle_id = '';") else {
            return
        }
        var toUpdate: [(Int64, String)] = []
        while (try? stmt.step()) == true {
            let id = stmt.columnInt64(at: 0)
            if let path = stmt.columnText(at: 1), let bundleId = Self.resolveBundleId(forPath: path) {
                toUpdate.append((id, bundleId))
            }
        }
        for (id, bundleId) in toUpdate {
            if let updateStmt = try? db.prepare("UPDATE apps SET bundle_id = ?1 WHERE id = ?2;") {
                try? updateStmt.bind(text: bundleId, at: 1)
                try? updateStmt.bind(int64: id, at: 2)
                _ = try? updateStmt.step()
            }
        }
    }

    /// Auto-repairs missing icons for tracked apps on the local device.
    public func repairMissingIcons(iconCache: MacIconCache, localDeviceId: String = "") {
        guard let stmt = try? db.prepare("""
            SELECT a.id, a.exe_path, a.bundle_id, a.display_name
            FROM apps a
            LEFT JOIN icons i ON i.app_id = a.id
            WHERE a.state = 'tracked' AND a.canonical_id IS NULL AND i.app_id IS NULL
              AND (a.device_id = '' OR ?1 = '' OR a.device_id = ?1);
            """) else { return }
        try? stmt.bind(text: localDeviceId, at: 1)
        var candidates: [(Int64, String, String?, String)] = []
        while (try? stmt.step()) == true {
            let id = stmt.columnInt64(at: 0)
            let path = stmt.columnText(at: 1) ?? ""
            let bundleId = stmt.columnText(at: 2)
            let name = stmt.columnText(at: 3) ?? ""
            candidates.append((id, path, bundleId, name))
        }
        for (id, path, bundleId, name) in candidates {
            _ = iconCache.resolveAndExtractIcon(forAppId: id, exePath: path, bundleId: bundleId, displayName: name)
        }
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
            UPDATE apps SET
                display_name = CASE WHEN id = ?4 THEN ?1 ELSE display_name END,
                kind = ?2,
                is_background = ?3
            WHERE id = (SELECT COALESCE(canonical_id, id) FROM apps WHERE id = ?4)
               OR canonical_id = (SELECT COALESCE(canonical_id, id) FROM apps WHERE id = ?4);
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
                let stmt = try db.prepare("""
                    UPDATE apps SET
                        canonical_id = ?1,
                        kind = (SELECT kind FROM apps WHERE id = ?1),
                        is_background = (SELECT is_background FROM apps WHERE id = ?1)
                    WHERE id = ?2;
                    """)
                try stmt.bind(int64: mainId, at: 1)
                try stmt.bind(int64: cId, at: 2)
                _ = try stmt.step()

                let copyIcon = try db.prepare("""
                    INSERT OR IGNORE INTO icons (app_id, png, hash)
                    SELECT ?1, png, hash FROM icons WHERE app_id = ?2;
                    """)
                try copyIcon.bind(int64: mainId, at: 1)
                try copyIcon.bind(int64: cId, at: 2)
                _ = try copyIcon.step()
            }
        }
    }

    public func unmerge(appId: Int64) throws {
        let stmt = try db.prepare("UPDATE apps SET canonical_id = NULL WHERE id = ?1;")
        try stmt.bind(int64: appId, at: 1)
        _ = try stmt.step()
    }

    /// Makes a merged app the main app of its group: the old main (and every other alias) is merged under it.
    public func makeMain(aliasId: Int64) throws -> Bool {
        var success = false
        try db.transaction {
            let findStmt = try db.prepare("SELECT canonical_id FROM apps WHERE id = ?1 AND state = 'tracked';")
            try findStmt.bind(int64: aliasId, at: 1)
            guard try findStmt.step() else { return }
            if findStmt.columnIsNull(at: 0) { return }
            let oldMain = findStmt.columnInt64(at: 0)
            guard oldMain > 0 else { return }

            let moveStmt = try db.prepare("""
                UPDATE apps SET
                    is_background = (SELECT is_background FROM apps WHERE id = ?1),
                    kind = (SELECT kind FROM apps WHERE id = ?1),
                    added_utc = MIN(added_utc, (SELECT added_utc FROM apps WHERE id = ?1))
                WHERE id = ?2;
                """)
            try moveStmt.bind(int64: oldMain, at: 1)
            try moveStmt.bind(int64: aliasId, at: 2)
            _ = try moveStmt.step()

            let reparentStmt = try db.prepare("UPDATE apps SET canonical_id = ?2 WHERE id = ?1 OR canonical_id = ?1;")
            try reparentStmt.bind(int64: oldMain, at: 1)
            try reparentStmt.bind(int64: aliasId, at: 2)
            _ = try reparentStmt.step()

            let clearStmt = try db.prepare("UPDATE apps SET canonical_id = NULL WHERE id = ?1;")
            try clearStmt.bind(int64: aliasId, at: 1)
            _ = try clearStmt.step()

            let goalsStmt = try db.prepare("UPDATE OR IGNORE goals SET app_id = ?2 WHERE app_id = ?1;")
            try goalsStmt.bind(int64: oldMain, at: 1)
            try goalsStmt.bind(int64: aliasId, at: 2)
            _ = try goalsStmt.step()

            let copyIcon = try db.prepare("""
                INSERT OR IGNORE INTO icons (app_id, png, hash)
                SELECT ?1, png, hash FROM icons WHERE app_id = ?2;
                """)
            try copyIcon.bind(int64: aliasId, at: 1)
            try copyIcon.bind(int64: oldMain, at: 2)
            _ = try copyIcon.step()

            success = true
        }
        return success
    }

    public func getActiveSeconds(appId: Int64) throws -> Int64 {
        let stmt = try db.prepare("""
            SELECT start_utc, COALESCE(end_utc, last_seen_utc)
            FROM sessions
            WHERE app_id = ?1 OR app_id IN (SELECT id FROM apps WHERE canonical_id = ?1)
            ORDER BY start_utc ASC;
            """)
        try stmt.bind(int64: appId, at: 1)
        var intervals: [(start: Int64, end: Int64)] = []
        while try stmt.step() {
            let start = stmt.columnInt64(at: 0)
            let end = stmt.columnInt64(at: 1)
            if end <= start { continue }
            if let last = intervals.last, start <= last.end {
                intervals[intervals.count - 1].end = max(last.end, end)
            } else {
                intervals.append((start: start, end: end))
            }
        }
        return intervals.reduce(0) { $0 + ($1.end - $1.start) }
    }

    public func getTrackedApps(deviceId: String? = nil) throws -> [TrackedAppItem] {
        var sql = """
            SELECT a.id, a.canonical_id, a.exe_path, a.display_name, a.category, a.state,
                   a.added_utc, a.is_background, a.kind, a.device_id,
                   (SELECT COUNT(*) FROM apps m WHERE m.canonical_id = a.id) as merged_count,
                   (SELECT COALESCE(SUM(u.seconds), 0) FROM daily_usage u WHERE u.app_id = a.id OR u.app_id IN (SELECT m.id FROM apps m WHERE m.canonical_id = a.id)) as total_sec,
                   (SELECT GROUP_CONCAT(m.display_name, '||') FROM apps m WHERE m.canonical_id = a.id) as merged_names,
                   (SELECT GROUP_CONCAT(COALESCE(m.device_id, ''), '||') FROM apps m WHERE m.canonical_id = a.id) as merged_device_ids
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
            let kindVal = stmt.columnIsNull(at: 8) ? nil : stmt.columnInt64(at: 8)
            let kind = AppKinds.resolve(kind: kindVal, exePath: path, displayName: name)
            let dev = stmt.columnText(at: 9) ?? ""
            let merged = Int(stmt.columnInt64(at: 10))
            let sec = merged > 0 ? ((try? getActiveSeconds(appId: id)) ?? stmt.columnInt64(at: 11)) : stmt.columnInt64(at: 11)
            let mergedNamesStr = stmt.columnText(at: 12)
            let mergedNames = mergedNamesStr.flatMap { $0.isEmpty ? nil : $0.components(separatedBy: "||") } ?? []
            let mergedDevsStr = stmt.columnText(at: 13)
            let mergedDevs = mergedDevsStr.flatMap { $0.isEmpty ? nil : $0.components(separatedBy: "||") } ?? []

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
                totalSeconds: sec,
                mergedNames: mergedNames,
                mergedDeviceIds: mergedDevs
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
                kind: AppKinds.resolve(kind: stmt.columnIsNull(at: 8) ? nil : stmt.columnInt64(at: 8), exePath: stmt.columnText(at: 2) ?? "", displayName: stmt.columnText(at: 3) ?? ""),
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
                deviceId: dev,
                publisher: AppVendor.name(executable: path)
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

                // Skip internal Campo (Siri AI) companion app in /System/Applications to avoid duplicate Siri
                if url.lastPathComponent == "Siri AI.app" {
                    continue
                }

                let bundle = Bundle(url: url)
                if bundle?.bundleIdentifier == "com.apple.campo" {
                    continue
                }

                let name = bundle?.localizedInfoDictionary?["CFBundleDisplayName"] as? String
                    ?? bundle?.infoDictionary?["CFBundleName"] as? String
                    ?? url.deletingPathExtension().lastPathComponent
                let bundleId = bundle?.bundleIdentifier
                let execPath = bundle?.executableURL?.path ?? path

                results.append(InstalledAppItem(
                    name: name,
                    bundleUrl: url,
                    executablePath: execPath,
                    bundleId: bundleId,
                    publisher: AppVendor.name(executable: execPath)
                ))
            }
        }

        return results.sorted(by: { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending })
    }
}
