import Foundation
import CryptoKit

public enum SyncReader {
    public static func isConflictCopy(filePath: String) -> Bool {
        let name = (filePath as NSString).lastPathComponent
        let pattern = #"(\(\d+\)| - Copy|-conflict-|\.sync-conflict-|conflicted copy)"#
        return name.range(of: pattern, options: [.regularExpression, .caseInsensitive]) != nil
    }

    public static func isPlaceholder(filePath: String) -> Bool {
        let fm = FileManager.default
        guard fm.fileExists(atPath: filePath) else { return false }

        // Test marker seam
        if fm.fileExists(atPath: filePath + ".placeholder") {
            return true
        }

        // Empty file ending in .tssync is a stub
        if filePath.hasSuffix(SyncWriter.extension),
           let attrs = try? fm.attributesOfItem(atPath: filePath),
           let size = attrs[.size] as? UInt64, size == 0 {
            return true
        }

        // macOS dataless file check via stat
        var statBuf = stat()
        if stat(filePath, &statBuf) == 0 {
            let sfDataless: UInt32 = 0x40000000
            if (statBuf.st_flags & sfDataless) != 0 {
                return true
            }
        }

        return false
    }

    public static func `import`(
        syncFile: String,
        db: SQLiteDatabase,
        timeZone: TimeZone = .current
    ) throws -> SyncImportResult {
        let fm = FileManager.default
        guard fm.fileExists(atPath: syncFile) else {
            return SyncImportResult(status: .failed, message: "File not found")
        }

        if isConflictCopy(filePath: syncFile) {
            return SyncImportResult(status: .conflictCopy, message: "Cloud conflict copy ignored")
        }

        if isPlaceholder(filePath: syncFile) {
            return SyncImportResult(status: .pending, message: "Cloud placeholder pending download")
        }

        let scratchDir = (NSTemporaryDirectory() as NSString).appendingPathComponent("timesink-sync-in-\(UUID().uuidString)")
        try fm.createDirectory(atPath: scratchDir, withIntermediateDirectories: true)
        defer {
            try? fm.removeItem(atPath: scratchDir)
        }

        let extractedDbPath = (scratchDir as NSString).appendingPathComponent(SyncManifest.dbEntryName)
        let extractedManifestPath = (scratchDir as NSString).appendingPathComponent(SyncManifest.entryName)

        // 1. Unzip using /usr/bin/unzip
        let proc = Process()
        proc.executableURL = URL(fileURLWithPath: "/usr/bin/unzip")
        proc.arguments = ["-q", "-o", syncFile, "-d", scratchDir]
        try proc.run()
        proc.waitUntilExit()

        guard proc.terminationStatus == 0,
              fm.fileExists(atPath: extractedDbPath),
              fm.fileExists(atPath: extractedManifestPath) else {
            return SyncImportResult(status: .badHash, message: "Missing or damaged archive contents")
        }

        // 2. Parse and verify manifest
        let manifestData = try Data(contentsOf: URL(fileURLWithPath: extractedManifestPath))
        let manifest = try JSONDecoder().decode(SyncManifest.self, from: manifestData)

        let snapData = try Data(contentsOf: URL(fileURLWithPath: extractedDbPath))
        let hashBytes = SHA256.hash(data: snapData)
        let computedHash = hashBytes.map { String(format: "%02x", $0) }.joined()

        guard computedHash.caseInsensitiveCompare(manifest.hash) == .orderedSame else {
            return SyncImportResult(status: .badHash, message: "Snapshot hash mismatch")
        }

        if manifest.format > 1 || manifest.schemaVersion > Migrator.latestVersion {
            return SyncImportResult(
                status: .newerFormat,
                message: "Update required (format \(manifest.format), schema \(manifest.schemaVersion))"
            )
        }

        // Integrity check
        let snapDb = try SQLiteDatabase(path: extractedDbPath, allowPlaintext: true)
        let integrity = try snapDb.scalarText("PRAGMA integrity_check;")
        snapDb.close()
        guard integrity == "ok" else {
            return SyncImportResult(status: .badHash, message: "snapshot.db failed integrity check")
        }

        // Check self and device state
        let localDeviceId = (try db.scalarText("SELECT value FROM settings WHERE key = 'device.id';")) ?? ""
        if manifest.deviceId.caseInsensitiveCompare(localDeviceId) == .orderedSame {
            return SyncImportResult(status: .selfFile, message: "Self snapshot ignored")
        }

        let existingState = try db.scalarText("SELECT state FROM devices WHERE device_id = '\(manifest.deviceId)';")
        if existingState == "forgotten" {
            return SyncImportResult(status: .deviceForgotten, message: "Device is forgotten")
        }

        if existingState == nil {
            let activeRemoteCount = (try db.scalarInt("SELECT COUNT(*) FROM devices WHERE is_local = 0 AND state = 'active';")) ?? 0
            if activeRemoteCount >= 1 {
                return SyncImportResult(status: .thirdDeviceIgnored, message: "Only two devices are supported yet")
            }
        }

        // Ingestion inside a transaction
        var appsAdded = 0
        var sessionsAdded = 0
        var sessionsUpdated = 0
        var sessionsSkipped = 0

        let escapedExtractedPath = extractedDbPath.replacingOccurrences(of: "'", with: "''")
        try db.exec("ATTACH DATABASE '\(escapedExtractedPath)' AS src KEY '';")
        defer {
            try? db.exec("DETACH DATABASE src;")
        }

        try db.transaction {
            let nowIso = ISO8601DateFormatter().string(from: Date())
            let fileUtcIso = manifest.createdUtc

            // 1. Upsert device record
            let devStmt = try db.prepare("""
                INSERT INTO main.devices (device_id, name, platform, is_local, first_seen_utc, last_import_utc, last_file_utc, state)
                VALUES (?1, ?2, ?3, 0, ?4, ?4, ?5, 'active')
                ON CONFLICT(device_id) DO UPDATE SET
                    name = excluded.name,
                    platform = excluded.platform,
                    last_import_utc = excluded.last_import_utc,
                    last_file_utc = excluded.last_file_utc;
                """)
            try devStmt.bind(text: manifest.deviceId, at: 1)
            try devStmt.bind(text: manifest.deviceName, at: 2)
            try devStmt.bind(text: manifest.platform, at: 3)
            try devStmt.bind(text: nowIso, at: 4)
            try devStmt.bind(text: fileUtcIso, at: 5)
            _ = try devStmt.step()

            // 2. Map & insert apps
            try db.exec("CREATE TEMP TABLE idmap (src_id INTEGER PRIMARY KEY, dst_id INTEGER NOT NULL);")
            defer {
                try? db.exec("DROP TABLE temp.idmap;")
            }

            let selectSrcApps = try db.prepare("""
                SELECT id, canonical_id, exe_path, bundle_id, device_id, package_family, display_name, category, state, added_utc, retired_utc, is_background, kind
                FROM src.apps;
                """)

            struct SrcApp {
                let id: Int64
                let canonicalId: Int64?
                let exePath: String
                let bundleId: String?
                let deviceId: String
                let packageFamily: String?
                let displayName: String
                let category: String
                let state: String
                let addedUtc: Int64
                let retiredUtc: Int64?
                let isBackground: Int64
                let kind: Int64?
            }

            var srcApps: [SrcApp] = []
            while try selectSrcApps.step() {
                let path = selectSrcApps.columnText(at: 2) ?? ""
                let name = selectSrcApps.columnText(at: 6) ?? ""
                let rawKind = selectSrcApps.columnIsNull(at: 12) ? nil : selectSrcApps.columnInt64(at: 12)
                let resolvedKind = rawKind ?? AppKinds.detect(exePath: path, displayName: name).rawValue

                srcApps.append(SrcApp(
                    id: selectSrcApps.columnInt64(at: 0),
                    canonicalId: selectSrcApps.columnInt64(at: 1) == 0 ? nil : selectSrcApps.columnInt64(at: 1),
                    exePath: path,
                    bundleId: selectSrcApps.columnText(at: 3),
                    deviceId: selectSrcApps.columnText(at: 4) ?? "",
                    packageFamily: selectSrcApps.columnText(at: 5),
                    displayName: name,
                    category: selectSrcApps.columnText(at: 7) ?? "Other",
                    state: selectSrcApps.columnText(at: 8) ?? "tracked",
                    addedUtc: selectSrcApps.columnInt64(at: 9),
                    retiredUtc: selectSrcApps.columnInt64(at: 10) == 0 ? nil : selectSrcApps.columnInt64(at: 10),
                    isBackground: selectSrcApps.columnInt64(at: 11),
                    kind: resolvedKind
                ))
            }

            var newlyInsertedApps: [(srcId: Int64, dstId: Int64, canonicalId: Int64?)] = []
            var allSnapshotApps: [(srcId: Int64, dstId: Int64, canonicalId: Int64?)] = []
            for a in srcApps {
                var dstId: Int64 = 0
                if let bundle = a.bundleId, !bundle.isEmpty {
                    let findStmt = try db.prepare("SELECT id FROM main.apps WHERE device_id = ?1 AND bundle_id = ?2;")
                    try findStmt.bind(text: a.deviceId, at: 1)
                    try findStmt.bind(text: bundle, at: 2)
                    if try findStmt.step() {
                        dstId = findStmt.columnInt64(at: 0)
                    }
                } else {
                    let findStmt = try db.prepare("SELECT id FROM main.apps WHERE device_id = ?1 AND exe_path = ?2 COLLATE NOCASE;")
                    try findStmt.bind(text: a.deviceId, at: 1)
                    try findStmt.bind(text: a.exePath, at: 2)
                    if try findStmt.step() {
                        dstId = findStmt.columnInt64(at: 0)
                    }
                }

                if dstId == 0 {
                    let insStmt = try db.prepare("""
                        INSERT INTO main.apps (
                            exe_path, bundle_id, device_id, package_family, display_name,
                            category, state, added_utc, retired_utc, is_background, kind
                        ) VALUES (
                            ?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11
                        );
                        """)
                    try insStmt.bind(text: a.exePath, at: 1)
                    if let b = a.bundleId { try insStmt.bind(text: b, at: 2) }
                    try insStmt.bind(text: a.deviceId, at: 3)
                    if let f = a.packageFamily { try insStmt.bind(text: f, at: 4) }
                    try insStmt.bind(text: a.displayName, at: 5)
                    try insStmt.bind(text: a.category, at: 6)
                    try insStmt.bind(text: a.state, at: 7)
                    try insStmt.bind(int64: a.addedUtc, at: 8)
                    if let r = a.retiredUtc { try insStmt.bind(int64: r, at: 9) }
                    try insStmt.bind(int64: a.isBackground, at: 10)
                    if let k = a.kind { try insStmt.bind(int64: k, at: 11) } else { try insStmt.bindNull(at: 11) }
                    _ = try insStmt.step()

                    dstId = db.lastInsertRowid
                    appsAdded += 1
                    newlyInsertedApps.append((a.id, dstId, a.canonicalId))
                } else if a.deviceId.caseInsensitiveCompare(manifest.deviceId) == .orderedSame {
                    let updStmt = try db.prepare("""
                        UPDATE main.apps SET
                            display_name = ?1,
                            category = ?2,
                            state = ?3,
                            retired_utc = ?4,
                            is_background = ?5,
                            kind = ?6
                        WHERE id = ?7;
                        """)
                    try updStmt.bind(text: a.displayName, at: 1)
                    try updStmt.bind(text: a.category, at: 2)
                    try updStmt.bind(text: a.state, at: 3)
                    if let r = a.retiredUtc { try updStmt.bind(int64: r, at: 4) } else { try updStmt.bindNull(at: 4) }
                    try updStmt.bind(int64: a.isBackground, at: 5)
                    if let k = a.kind { try updStmt.bind(int64: k, at: 6) } else { try updStmt.bindNull(at: 6) }
                    try updStmt.bind(int64: dstId, at: 7)
                    _ = try updStmt.step()
                }

                allSnapshotApps.append((a.id, dstId, a.canonicalId))

                let mapStmt = try db.prepare("INSERT INTO temp.idmap VALUES (?1, ?2);")
                try mapStmt.bind(int64: a.id, at: 1)
                try mapStmt.bind(int64: dstId, at: 2)
                _ = try mapStmt.step()
            }

            // Remap canonical links for all snapshot apps (intra-device merges)
            for (_, dstId, srcCanonical) in allSnapshotApps {
                if let sc = srcCanonical {
                    let updateStmt = try db.prepare("""
                        UPDATE main.apps
                        SET canonical_id = (SELECT dst_id FROM temp.idmap WHERE src_id = ?1)
                        WHERE id = ?2;
                        """)
                    try updateStmt.bind(int64: sc, at: 1)
                    try updateStmt.bind(int64: dstId, at: 2)
                    _ = try updateStmt.step()
                } else {
                    let unmergeStmt = try db.prepare("""
                        UPDATE main.apps
                        SET canonical_id = NULL
                        WHERE id = ?1 AND canonical_id IN (SELECT dst_id FROM temp.idmap);
                        """)
                    try unmergeStmt.bind(int64: dstId, at: 1)
                    _ = try unmergeStmt.step()
                }
            }

            // Sync cross-device merges
            let hasCrossTable = try db.prepare("SELECT COUNT(*) FROM src.sqlite_master WHERE type='table' AND name='cross_device_merges';")
            if try hasCrossTable.step(), hasCrossTable.columnInt64(at: 0) > 0 {
                func findAppInMain(dev: String, exe: String, bundle: String) throws -> Int64? {
                    if !bundle.isEmpty {
                        let stmt = try db.prepare("SELECT id FROM main.apps WHERE device_id = ?1 AND bundle_id = ?2 LIMIT 1;")
                        try stmt.bind(text: dev, at: 1)
                        try stmt.bind(text: bundle, at: 2)
                        if try stmt.step() { return stmt.columnInt64(at: 0) }
                    } else {
                        let stmt = try db.prepare("SELECT id FROM main.apps WHERE device_id = ?1 AND exe_path = ?2 COLLATE NOCASE LIMIT 1;")
                        try stmt.bind(text: dev, at: 1)
                        try stmt.bind(text: exe, at: 2)
                        if try stmt.step() { return stmt.columnInt64(at: 0) }
                    }
                    return nil
                }

                var validSubordinates: Set<Int64> = []
                let readCross = try db.prepare("SELECT source_device_id, source_exe_path, source_bundle_id, target_device_id, target_exe_path, target_bundle_id FROM src.cross_device_merges;")
                while try readCross.step() {
                    let sDev = readCross.columnText(at: 0) ?? ""
                    let sExe = readCross.columnText(at: 1) ?? ""
                    let sBundle = readCross.columnText(at: 2) ?? ""
                    let tDev = readCross.columnText(at: 3) ?? ""
                    let tExe = readCross.columnText(at: 4) ?? ""
                    let tBundle = readCross.columnText(at: 5) ?? ""

                    if let sId = try findAppInMain(dev: sDev, exe: sExe, bundle: sBundle),
                       let tId = try findAppInMain(dev: tDev, exe: tExe, bundle: tBundle),
                       sId != tId {
                        let upStmt = try db.prepare("UPDATE main.apps SET canonical_id = ?1 WHERE id = ?2;")
                        try upStmt.bind(int64: tId, at: 1)
                        try upStmt.bind(int64: sId, at: 2)
                        _ = try upStmt.step()
                        validSubordinates.insert(sId)
                    }
                }

                // Unmerge stale cross-device merges from this snapshot device
                let unmergeCross = try db.prepare("""
                    SELECT a.id FROM main.apps a
                    JOIN main.apps c ON c.id = a.canonical_id
                    WHERE a.device_id = ?1 AND c.device_id != ?1;
                    """)
                try unmergeCross.bind(text: manifest.deviceId, at: 1)
                var staleIds: [Int64] = []
                while try unmergeCross.step() {
                    let id = unmergeCross.columnInt64(at: 0)
                    if !validSubordinates.contains(id) {
                        staleIds.append(id)
                    }
                }
                for sid in staleIds {
                    let clearStmt = try db.prepare("UPDATE main.apps SET canonical_id = NULL WHERE id = ?1;")
                    try clearStmt.bind(int64: sid, at: 1)
                    _ = try clearStmt.step()
                }
            }

            // Merge icons
            try db.exec("""
                INSERT OR IGNORE INTO main.icons (app_id, png, hash)
                SELECT m.dst_id, i.png, i.hash
                FROM src.icons i
                JOIN temp.idmap m ON m.src_id = i.app_id;
                """)

            // Gather existing sessions for stats
            var existingSessions: [String: (endUtc: Int64?, lastSeenUtc: Int64)] = [:]
            let exStmt = try db.prepare("""
                SELECT s.app_id, s.start_utc, s.end_utc, s.last_seen_utc
                FROM main.sessions s
                JOIN main.apps a ON a.id = s.app_id
                WHERE a.device_id = ?1;
                """)
            try exStmt.bind(text: manifest.deviceId, at: 1)
            while try exStmt.step() {
                let appId = exStmt.columnInt64(at: 0)
                let startUtc = exStmt.columnInt64(at: 1)
                let endUtc = exStmt.columnInt64(at: 2) == 0 ? nil : exStmt.columnInt64(at: 2)
                let lastSeen = exStmt.columnInt64(at: 3)
                existingSessions["\(appId):\(startUtc)"] = (endUtc, lastSeen)
            }

            // Ingest sessions
            try db.exec("""
                INSERT INTO main.sessions (app_id, start_utc, end_utc, last_seen_utc, pid, proc_start_utc, end_reason)
                SELECT
                    m.dst_id,
                    s.start_utc,
                    COALESCE(s.end_utc, s.last_seen_utc),
                    s.last_seen_utc,
                    s.pid,
                    s.proc_start_utc,
                    COALESCE(s.end_reason, 'restore')
                FROM src.sessions s
                JOIN temp.idmap m ON m.src_id = s.app_id
                ON CONFLICT(app_id, start_utc) DO UPDATE SET
                    end_utc = CASE
                        WHEN COALESCE(excluded.end_utc, excluded.last_seen_utc) > COALESCE(main.sessions.end_utc, main.sessions.last_seen_utc)
                        THEN excluded.end_utc
                        ELSE main.sessions.end_utc
                    END,
                    last_seen_utc = MAX(main.sessions.last_seen_utc, excluded.last_seen_utc),
                    end_reason = CASE
                        WHEN excluded.last_seen_utc >= main.sessions.last_seen_utc AND excluded.end_reason IS NOT NULL
                        THEN excluded.end_reason
                        ELSE main.sessions.end_reason
                    END;
                """)

            // Calculate stats
            let statStmt = try db.prepare("""
                SELECT m.dst_id, s.start_utc, COALESCE(s.end_utc, s.last_seen_utc), s.last_seen_utc
                FROM src.sessions s
                JOIN temp.idmap m ON m.src_id = s.app_id;
                """)
            while try statStmt.step() {
                let dstId = statStmt.columnInt64(at: 0)
                let startUtc = statStmt.columnInt64(at: 1)
                let effectiveEnd = statStmt.columnInt64(at: 2)
                let lastSeen = statStmt.columnInt64(at: 3)

                let key = "\(dstId):\(startUtc)"
                if let prev = existingSessions[key] {
                    let prevEnd = prev.endUtc ?? prev.lastSeenUtc
                    if effectiveEnd > prevEnd || lastSeen > prev.lastSeenUtc {
                        sessionsUpdated += 1
                    } else {
                        sessionsSkipped += 1
                    }
                } else {
                    sessionsAdded += 1
                }
            }
        }

        // Rebuild daily usage
        let store = SQLiteSessionStore(db: db)
        let accumulator = UsageAccumulator(store: store, timeZone: timeZone)
        try accumulator.rebuildAll()

        return SyncImportResult(
            status: .success,
            message: nil,
            appsAdded: appsAdded,
            sessionsAdded: sessionsAdded,
            sessionsUpdated: sessionsUpdated,
            sessionsSkipped: sessionsSkipped
        )
    }
}

