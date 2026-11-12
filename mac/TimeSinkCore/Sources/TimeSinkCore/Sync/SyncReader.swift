import Foundation
import CryptoKit
#if canImport(SQLCipher)
import SQLCipher
#elseif canImport(SQLite3)
import SQLite3
#endif

public enum SyncReader {
    /// Settings key holding the fingerprint ("<hash>:<createdUtc>") of the last imported file of a remote device.
    public static func fingerprintKey(deviceId: String) -> String {
        fingerprintKeyPrefix + deviceId
    }

    public static let fingerprintKeyPrefix = "sync.imported."
    /// SQL LIKE pattern matching every fingerprint row.
    public static let fingerprintKeyLike = fingerprintKeyPrefix + "%"

    private static func fingerprint(of manifest: SyncManifest) -> String {
        "\(manifest.hash):\(manifest.createdUtc)"
    }

    /// Forget every recorded import fingerprint so the next sync re-imports (passphrase or encryption change).
    public static func clearFingerprints(db: SQLiteDatabase) {
        try? db.exec("DELETE FROM settings WHERE key LIKE '\(fingerprintKeyLike)';")
    }

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

    private enum IntegrityCheckResult {
        case ok
        case wrongKey(String)
        case corrupt(String)
    }

    private static func verifySnapshotIntegrity(snapshotDbPath: String, passphrase: String?) -> IntegrityCheckResult {
        var handle: OpaquePointer?
        let flags = SQLITE_OPEN_READONLY | SQLITE_OPEN_FULLMUTEX
        guard sqlite3_open_v2(snapshotDbPath, &handle, flags, nil) == SQLITE_OK, let h = handle else {
            if let h = handle { sqlite3_close_v2(h) }
            return passphrase != nil ? .wrongKey("Failed to open snapshot") : .corrupt("Failed to open snapshot")
        }
        defer { sqlite3_close_v2(h) }

        if let passphrase = passphrase, !passphrase.isEmpty {
            let escPass = passphrase.replacingOccurrences(of: "'", with: "''")
            var err: UnsafeMutablePointer<CChar>?
            if sqlite3_exec(h, "PRAGMA key = '\(escPass)';", nil, nil, &err) != SQLITE_OK {
                let msg = err != nil ? String(cString: err!) : "Failed to set PRAGMA key"
                sqlite3_free(err)
                return .wrongKey(msg)
            }
        }

        var stmt: OpaquePointer?
        guard sqlite3_prepare_v2(h, "PRAGMA integrity_check;", -1, &stmt, nil) == SQLITE_OK, let s = stmt else {
            let msg = String(cString: sqlite3_errmsg(h))
            return passphrase != nil ? .wrongKey(msg) : .corrupt(msg)
        }
        defer { sqlite3_finalize(s) }

        let stepRc = sqlite3_step(s)
        if stepRc == SQLITE_ROW {
            if let cStr = sqlite3_column_text(s, 0) {
                let resultStr = String(cString: cStr)
                if resultStr == "ok" {
                    return .ok
                } else {
                    return passphrase != nil ? .wrongKey("Integrity check returned \(resultStr)") : .corrupt("Integrity check returned \(resultStr)")
                }
            }
        } else {
            let msg = String(cString: sqlite3_errmsg(h))
            return passphrase != nil ? .wrongKey("Failed to read integrity check: \(msg)") : .corrupt("Failed to read integrity check: \(msg)")
        }
        return .corrupt("Unknown integrity check failure")
    }

    public static func `import`(
        syncFile: String,
        db: SQLiteDatabase,
        timeZone: TimeZone = .current,
        keyManager: KeyManagerProtocol = KeychainKeyManager.shared
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

        // Already imported this exact file and the remote device is still active: nothing to do.
        let fpKey = fingerprintKey(deviceId: manifest.deviceId).replacingOccurrences(of: "'", with: "''")
        let escDeviceId = manifest.deviceId.replacingOccurrences(of: "'", with: "''")
        let storedFingerprint = try db.scalarText("SELECT value FROM settings WHERE key = '\(fpKey)';")
        if storedFingerprint == fingerprint(of: manifest),
           try db.scalarText("SELECT state FROM devices WHERE device_id = '\(escDeviceId)';") == "active" {
            return SyncImportResult(status: .unchanged)
        }

        let snapData = try Data(contentsOf: URL(fileURLWithPath: extractedDbPath))
        let hashBytes = SHA256.hash(data: snapData)
        let computedHash = hashBytes.map { String(format: "%02x", $0) }.joined()

        guard computedHash.caseInsensitiveCompare(manifest.hash) == .orderedSame else {
            return SyncImportResult(status: .badHash, message: "Snapshot hash mismatch")
        }

        let latestSupportedSchema: Int32 = max(5, Migrator.latestVersion)
        if manifest.format > 1 || manifest.schemaVersion > latestSupportedSchema {
            return SyncImportResult(
                status: .newerFormat,
                message: "Update required (format \(manifest.format), schema \(manifest.schemaVersion))"
            )
        }

        // 3. Encryption verification and integrity check
        var passphrase: String? = nil
        if manifest.encrypted {
            guard keyManager.hasSyncPassphrase(), let pass = try? keyManager.getSyncPassphrase(), !pass.isEmpty else {
                return SyncImportResult(status: .encryptionKeyMissing, message: "Sync encryption key missing")
            }
            passphrase = pass

            let integrityResult = verifySnapshotIntegrity(snapshotDbPath: extractedDbPath, passphrase: pass)
            switch integrityResult {
            case .ok:
                break
            case .wrongKey(let msg):
                return SyncImportResult(status: .encryptionKeyWrong, message: "Incorrect sync passphrase or damaged snapshot: \(msg)")
            case .corrupt(let msg):
                return SyncImportResult(status: .badHash, message: "snapshot.db failed integrity check: \(msg)")
            }
        } else {
            // Check if local device expects encryption
            let localEncrypted = (try? db.scalarText("SELECT value FROM settings WHERE key = 'sync.encrypted';"))?.lowercased() == "true"
            if localEncrypted {
                return SyncImportResult(status: .remoteNotEncrypted, message: "Remote sync archive is not encrypted")
            }

            let integrityResult = verifySnapshotIntegrity(snapshotDbPath: extractedDbPath, passphrase: nil)
            switch integrityResult {
            case .ok:
                break
            case .wrongKey, .corrupt:
                return SyncImportResult(status: .badHash, message: "snapshot.db failed integrity check")
            }
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
        try db.detachIfAttached("src")
        if let passphrase = passphrase, !passphrase.isEmpty {
            let escPass = passphrase.replacingOccurrences(of: "'", with: "''")
            try db.exec("ATTACH DATABASE '\(escapedExtractedPath)' AS src KEY '\(escPass)';")
        } else {
            try db.exec("ATTACH DATABASE '\(escapedExtractedPath)' AS src KEY '';")
        }
        defer {
            do {
                try db.exec("DETACH DATABASE src;")
            } catch {
                NSLog("SyncReader detach src failed: \(error)")
            }
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

            // Record the fingerprint in the same transaction so a rolled-back import never stores it
            let fpStmt = try db.prepare("INSERT INTO main.settings (key, value) VALUES (?1, ?2) ON CONFLICT(key) DO UPDATE SET value = excluded.value;")
            try fpStmt.bind(text: fingerprintKey(deviceId: manifest.deviceId), at: 1)
            try fpStmt.bind(text: fingerprint(of: manifest), at: 2)
            _ = try fpStmt.step()

            // 2. Map & insert apps
            try db.exec("CREATE TEMP TABLE idmap (src_id INTEGER PRIMARY KEY, dst_id INTEGER NOT NULL);")
            defer {
                try? db.exec("DROP TABLE temp.idmap;")
            }

            var hasUpdatedUtcInSrc = false
            let pragmaStmt = try db.prepare("PRAGMA src.table_info(apps);")
            while try pragmaStmt.step() {
                if let colName = pragmaStmt.columnText(at: 1), colName == "updated_utc" {
                    hasUpdatedUtcInSrc = true
                    break
                }
            }
            let selectSrcAppsSql = hasUpdatedUtcInSrc
                ? "SELECT id, canonical_id, exe_path, bundle_id, device_id, package_family, display_name, category, state, added_utc, retired_utc, is_background, kind, updated_utc FROM src.apps;"
                : "SELECT id, canonical_id, exe_path, bundle_id, device_id, package_family, display_name, category, state, added_utc, retired_utc, is_background, kind, 0 FROM src.apps;"
            let selectSrcApps = try db.prepare(selectSrcAppsSql)

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
                let updatedUtc: Int64
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
                    kind: resolvedKind,
                    updatedUtc: selectSrcApps.columnInt64(at: 13)
                ))
            }

            var newlyInsertedApps: [(srcId: Int64, dstId: Int64, canonicalId: Int64?)] = []
            var allSnapshotApps: [(srcId: Int64, dstId: Int64, canonicalId: Int64?)] = []
            for a in srcApps {
                var dstId: Int64 = 0
                let findExeStmt = try db.prepare("SELECT id FROM main.apps WHERE device_id = ?1 AND exe_path = ?2 COLLATE NOCASE;")
                try findExeStmt.bind(text: a.deviceId, at: 1)
                try findExeStmt.bind(text: a.exePath, at: 2)
                if try findExeStmt.step() {
                    dstId = findExeStmt.columnInt64(at: 0)
                } else if let bundle = a.bundleId, !bundle.isEmpty {
                    let findBundleStmt = try db.prepare("SELECT id FROM main.apps WHERE device_id = ?1 AND bundle_id = ?2;")
                    try findBundleStmt.bind(text: a.deviceId, at: 1)
                    try findBundleStmt.bind(text: bundle, at: 2)
                    if try findBundleStmt.step() {
                        dstId = findBundleStmt.columnInt64(at: 0)
                    }
                }

                if dstId == 0 {
                    let insStmt = try db.prepare("""
                        INSERT INTO main.apps (
                            exe_path, bundle_id, device_id, package_family, display_name,
                            category, state, added_utc, retired_utc, is_background, kind, updated_utc
                        ) VALUES (
                            ?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12
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
                    try insStmt.bind(int64: a.updatedUtc, at: 12)
                    _ = try insStmt.step()

                    dstId = db.lastInsertRowid
                    appsAdded += 1
                    newlyInsertedApps.append((a.id, dstId, a.canonicalId))
                } else {
                    let localUpdatedUtc: Int64
                    let checkStmt = try db.prepare("SELECT updated_utc FROM main.apps WHERE id = ?1;")
                    try checkStmt.bind(int64: dstId, at: 1)
                    if try checkStmt.step() {
                        localUpdatedUtc = checkStmt.columnInt64(at: 0)
                    } else {
                        localUpdatedUtc = 0
                    }

                    if a.deviceId.caseInsensitiveCompare(manifest.deviceId) == .orderedSame {
                        let updStmt = try db.prepare("""
                            UPDATE main.apps SET
                                exe_path = ?1,
                                bundle_id = CASE WHEN ?2 IS NOT NULL AND ?2 != '' THEN ?2 ELSE bundle_id END,
                                package_family = CASE WHEN ?3 IS NOT NULL AND ?3 != '' THEN ?3 ELSE package_family END,
                                display_name = CASE WHEN ?11 >= ?12 THEN ?4 ELSE display_name END,
                                category = CASE WHEN ?11 >= ?12 THEN ?5 ELSE category END,
                                state = ?6,
                                retired_utc = ?7,
                                is_background = CASE
                                    WHEN ?11 >= ?12 THEN (CASE WHEN ?8 != 0 THEN 1 ELSE 0 END)
                                    WHEN ?8 != 0 THEN 1
                                    WHEN is_background != 0 THEN 1
                                    WHEN canonical_id IS NOT NULL AND (SELECT is_background FROM main.apps WHERE id = main.apps.canonical_id) != 0 THEN 1
                                    ELSE 0
                                END,
                                kind = CASE
                                    WHEN ?11 >= ?12 THEN (CASE WHEN ?9 IS NOT NULL AND ?9 != 0 THEN ?9 ELSE COALESCE(?9, 0) END)
                                    WHEN ?9 IS NOT NULL AND ?9 != 0 THEN ?9
                                    WHEN kind IS NOT NULL AND kind != 0 THEN kind
                                    WHEN canonical_id IS NOT NULL AND (SELECT kind FROM main.apps WHERE id = main.apps.canonical_id) IS NOT NULL AND (SELECT kind FROM main.apps WHERE id = main.apps.canonical_id) != 0
                                        THEN (SELECT kind FROM main.apps WHERE id = main.apps.canonical_id)
                                    ELSE COALESCE(?9, 0)
                                END,
                                updated_utc = CASE WHEN ?11 >= ?12 THEN ?11 ELSE updated_utc END
                            WHERE id = ?10;
                            """)
                        try updStmt.bind(text: a.exePath, at: 1)
                        if let b = a.bundleId { try updStmt.bind(text: b, at: 2) } else { try updStmt.bindNull(at: 2) }
                        if let f = a.packageFamily { try updStmt.bind(text: f, at: 3) } else { try updStmt.bindNull(at: 3) }
                        try updStmt.bind(text: a.displayName, at: 4)
                        try updStmt.bind(text: a.category, at: 5)
                        try updStmt.bind(text: a.state, at: 6)
                        if let r = a.retiredUtc { try updStmt.bind(int64: r, at: 7) } else { try updStmt.bindNull(at: 7) }
                        try updStmt.bind(int64: a.isBackground, at: 8)
                        if let k = a.kind { try updStmt.bind(int64: k, at: 9) } else { try updStmt.bindNull(at: 9) }
                        try updStmt.bind(int64: dstId, at: 10)
                        try updStmt.bind(int64: a.updatedUtc, at: 11)
                        try updStmt.bind(int64: localUpdatedUtc, at: 12)
                        _ = try updStmt.step()
                    } else if a.updatedUtc > localUpdatedUtc {
                        // Remote edit for this app (e.g. Mac app whose classification was modified on Windows)
                        let updRemoteStmt = try db.prepare("""
                            UPDATE main.apps SET
                                display_name = ?1,
                                category = ?2,
                                is_background = ?3,
                                kind = ?4,
                                updated_utc = ?5
                            WHERE id = ?6;
                            """)
                        try updRemoteStmt.bind(text: a.displayName, at: 1)
                        try updRemoteStmt.bind(text: a.category, at: 2)
                        try updRemoteStmt.bind(int64: a.isBackground, at: 3)
                        if let k = a.kind { try updRemoteStmt.bind(int64: k, at: 4) } else { try updRemoteStmt.bindNull(at: 4) }
                        try updRemoteStmt.bind(int64: a.updatedUtc, at: 5)
                        try updRemoteStmt.bind(int64: dstId, at: 6)
                        _ = try updRemoteStmt.step()

                        // Also propagate to merged group members on local device
                        let propStmt = try db.prepare("""
                            UPDATE main.apps SET
                                category = ?1,
                                kind = ?2,
                                is_background = ?3,
                                updated_utc = ?4
                            WHERE (canonical_id = ?5 OR id = (SELECT canonical_id FROM main.apps WHERE id = ?5))
                              AND updated_utc <= ?4;
                            """)
                        try propStmt.bind(text: a.category, at: 1)
                        if let k = a.kind { try propStmt.bind(int64: k, at: 2) } else { try propStmt.bindNull(at: 2) }
                        try propStmt.bind(int64: a.isBackground, at: 3)
                        try propStmt.bind(int64: a.updatedUtc, at: 4)
                        try propStmt.bind(int64: dstId, at: 5)
                        _ = try propStmt.step()
                    }
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
                    let stmtExe = try db.prepare("SELECT id FROM main.apps WHERE device_id = ?1 AND exe_path = ?2 COLLATE NOCASE LIMIT 1;")
                    try stmtExe.bind(text: dev, at: 1)
                    try stmtExe.bind(text: exe, at: 2)
                    if try stmtExe.step() { return stmtExe.columnInt64(at: 0) }

                    if !bundle.isEmpty {
                        let stmt = try db.prepare("SELECT id FROM main.apps WHERE device_id = ?1 AND bundle_id = ?2 LIMIT 1;")
                        try stmt.bind(text: dev, at: 1)
                        try stmt.bind(text: bundle, at: 2)
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
                        let upStmt = try db.prepare("""
                            UPDATE main.apps
                            SET canonical_id = ?1,
                                kind = CASE
                                    WHEN (SELECT kind FROM main.apps WHERE id = ?1) IS NOT NULL AND (SELECT kind FROM main.apps WHERE id = ?1) != 0
                                        THEN (SELECT kind FROM main.apps WHERE id = ?1)
                                    WHEN kind IS NOT NULL AND kind != 0 THEN kind
                                    ELSE 0
                                END,
                                is_background = CASE
                                    WHEN (SELECT is_background FROM main.apps WHERE id = ?1) != 0 THEN 1
                                    WHEN is_background != 0 THEN 1
                                    ELSE 0
                                END
                            WHERE id = ?2;
                            """)
                        try upStmt.bind(int64: tId, at: 1)
                        try upStmt.bind(int64: sId, at: 2)
                        _ = try upStmt.step()

                        let upCanon = try db.prepare("""
                            UPDATE main.apps
                            SET kind = (SELECT kind FROM main.apps WHERE id = ?2),
                                is_background = CASE WHEN (SELECT is_background FROM main.apps WHERE id = ?2) != 0 THEN 1 ELSE is_background END
                            WHERE id = ?1
                              AND (kind IS NULL OR kind = 0)
                              AND (SELECT kind FROM main.apps WHERE id = ?2) IS NOT NULL
                              AND (SELECT kind FROM main.apps WHERE id = ?2) != 0;
                            """)
                        try upCanon.bind(int64: tId, at: 1)
                        try upCanon.bind(int64: sId, at: 2)
                        _ = try upCanon.step()
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

            // Harmonize kind and is_background across all merged groups
            try? db.exec("""
                UPDATE main.apps
                SET kind = (SELECT kind FROM main.apps c WHERE c.id = apps.canonical_id)
                WHERE canonical_id IS NOT NULL
                  AND (kind IS NULL OR kind = 0)
                  AND (SELECT kind FROM main.apps c WHERE c.id = apps.canonical_id) IS NOT NULL
                  AND (SELECT kind FROM main.apps c WHERE c.id = apps.canonical_id) != 0;

                UPDATE main.apps
                SET kind = (SELECT a.kind FROM main.apps a WHERE a.canonical_id = apps.id AND a.kind IS NOT NULL AND a.kind != 0 LIMIT 1)
                WHERE (kind IS NULL OR kind = 0)
                  AND EXISTS (SELECT 1 FROM main.apps a WHERE a.canonical_id = apps.id AND a.kind IS NOT NULL AND a.kind != 0);

                UPDATE main.apps
                SET is_background = 1
                WHERE is_background = 0
                  AND canonical_id IS NOT NULL
                  AND (SELECT is_background FROM main.apps c WHERE c.id = apps.canonical_id) != 0;

                UPDATE main.apps
                SET is_background = 1
                WHERE is_background = 0
                  AND EXISTS (SELECT 1 FROM main.apps a WHERE a.canonical_id = apps.id AND a.is_background != 0);
                """)

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

