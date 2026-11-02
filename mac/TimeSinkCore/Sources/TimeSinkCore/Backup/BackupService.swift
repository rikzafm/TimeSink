import Foundation
#if canImport(SQLCipher)
import SQLCipher
#elseif canImport(SQLite3)
import SQLite3
#endif

public final class BackupService: Sendable {
    public static let fileExtension = ".timesink"

    public static func create(
        dbPath: String,
        destFile: String,
        now: Date = Date(),
        appVersion: String = "1.0.0",
        key: Data? = nil
    ) throws {
        let destDir = (destFile as NSString).deletingLastPathComponent
        try FileManager.default.createDirectory(atPath: destDir, withIntermediateDirectories: true)

        let tempDir = URL(fileURLWithPath: NSTemporaryDirectory())
            .appendingPathComponent("timesink-bk-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: tempDir, withIntermediateDirectories: true)
        defer {
            try? FileManager.default.removeItem(at: tempDir)
        }

        let partial = destFile + ".partial"
        try? FileManager.default.removeItem(atPath: partial)
        defer {
            try? FileManager.default.removeItem(atPath: partial)
        }

        let tempDbPath = tempDir.appendingPathComponent(BackupManifest.dbEntryName).path
        let isPlaintext = EncryptionMigrator.isPlaintextDatabase(at: dbPath)
        let db = try SQLiteDatabase(path: dbPath, key: isPlaintext ? nil : key, allowPlaintext: isPlaintext)
        try db.backup(to: tempDbPath, destKey: isPlaintext ? nil : (key ?? db.key))
        let version = db.userVersion
        db.close()

        let isoFormatter = ISO8601DateFormatter()
        isoFormatter.formatOptions = [.withInternetDateTime]
        let manifest = BackupManifest(
            format: 1,
            schemaVersion: Int(version),
            appVersion: appVersion,
            createdUtc: isoFormatter.string(from: now)
        )
        let manifestPath = tempDir.appendingPathComponent(BackupManifest.entryName).path
        let manifestData = try manifest.toJsonData()
        try manifestData.write(to: URL(fileURLWithPath: manifestPath))

        // Create standard zip archive with manifest.json and timesink.db
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/zip")
        process.arguments = ["-q", "-j", partial, manifestPath, tempDbPath]
        try process.run()
        process.waitUntilExit()

        guard process.terminationStatus == 0 else {
            throw BackupError.executionFailed("zip command exited with status \(process.terminationStatus)")
        }

        try? FileManager.default.removeItem(atPath: destFile)
        try FileManager.default.moveItem(atPath: partial, toPath: destFile)
    }

    public static func inspect(backupFile: String, key: Data? = nil) throws -> BackupInspection {
        let prepared = try prepare(backupFile: backupFile, key: key)
        defer {
            try? FileManager.default.removeItem(at: prepared.dir)
        }

        let db = try SQLiteDatabase(path: prepared.dbPath, key: prepared.isPlaintext ? nil : key, allowPlaintext: prepared.isPlaintext)
        let apps = Int(try db.scalarInt("SELECT COUNT(*) FROM apps;") ?? 0)
        let sessions = Int(try db.scalarInt("SELECT COUNT(*) FROM sessions;") ?? 0)
        db.close()

        return BackupInspection(
            manifest: prepared.manifest,
            apps: apps,
            sessions: sessions,
            needsMigration: prepared.migrated
        )
    }

    public static func replace(
        backupFile: String,
        dbPath: String,
        safetyFolder: String,
        now: Date = Date(),
        zone: TimeZone = .current,
        key: Data? = nil
    ) throws -> ReplaceResult {
        let prepared = try prepare(backupFile: backupFile, key: key)
        defer {
            try? FileManager.default.removeItem(at: prepared.dir)
        }

        var safetyCopy: String? = nil
        if FileManager.default.fileExists(atPath: dbPath) {
            try FileManager.default.createDirectory(atPath: safetyFolder, withIntermediateDirectories: true)
            let df = DateFormatter()
            df.dateFormat = "yyyyMMdd-HHmmss"
            df.timeZone = TimeZone(secondsFromGMT: 0)
            let safetyName = "pre-restore-\(df.string(from: now))\(fileExtension)"
            let safetyPath = (safetyFolder as NSString).appendingPathComponent(safetyName)
            try create(dbPath: dbPath, destFile: safetyPath, now: now, key: key)
            safetyCopy = safetyPath
        }

        let db = try SQLiteDatabase(path: dbPath, key: key)
        let escapedSrc = prepared.dbPath.replacingOccurrences(of: "'", with: "''")
        let attachSql: String
        if prepared.isPlaintext {
            attachSql = "ATTACH DATABASE '\(escapedSrc)' AS src KEY '';"
        } else {
            let resolvedKey = try (key ?? db.key ?? KeychainKeyManager.shared.getOrCreateDatabaseKey())
            let hexKey = resolvedKey.map { String(format: "%02x", $0) }.joined()
            attachSql = "ATTACH DATABASE '\(escapedSrc)' AS src KEY \"x'\(hexKey)'\";"
        }
        try db.exec(attachSql)
        defer {
            try? db.exec("DETACH DATABASE src;")
        }

        try db.exec("PRAGMA foreign_keys = OFF;")
        defer {
            try? db.exec("PRAGMA foreign_keys = ON;")
        }

        try db.transaction {
            // Find all tables in main
            let stmt = try db.prepare("SELECT name FROM main.sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';")
            var tables: [String] = []
            while try stmt.step() {
                if let t = stmt.columnText(at: 0) {
                    tables.append(t)
                }
            }

            for table in tables {
                // Get column list
                let colStmt = try db.prepare("SELECT name FROM pragma_table_info('\(table)', 'main');")
                var cols: [String] = []
                while try colStmt.step() {
                    if let c = colStmt.columnText(at: 0) {
                        cols.append("\"" + c + "\"")
                    }
                }
                let colString = cols.joined(separator: ", ")
                try db.exec("DELETE FROM main.\"\(table)\";")
                try db.exec("INSERT INTO main.\"\(table)\" (\(colString)) SELECT \(colString) FROM src.\"\(table)\";")
            }

            try db.exec("UPDATE sessions SET end_utc = last_seen_utc, end_reason = 'restore' WHERE end_utc IS NULL;")
        }

        let store = SQLiteSessionStore(db: db)
        let accumulator = UsageAccumulator(store: store, timeZone: zone)
        try accumulator.rebuildAll()

        return ReplaceResult(safetyCopy: safetyCopy)
    }

    public static func merge(
        backupFile: String,
        dbPath: String,
        zone: TimeZone = .current,
        key: Data? = nil
    ) throws -> MergeResult {
        let prepared = try prepare(backupFile: backupFile, key: key)
        defer {
            try? FileManager.default.removeItem(at: prepared.dir)
        }

        let db = try SQLiteDatabase(path: dbPath, key: key)
        let escapedSrc = prepared.dbPath.replacingOccurrences(of: "'", with: "''")
        let attachSql: String
        if prepared.isPlaintext {
            attachSql = "ATTACH DATABASE '\(escapedSrc)' AS src KEY '';"
        } else {
            let resolvedKey = try (key ?? db.key ?? KeychainKeyManager.shared.getOrCreateDatabaseKey())
            let hexKey = resolvedKey.map { String(format: "%02x", $0) }.joined()
            attachSql = "ATTACH DATABASE '\(escapedSrc)' AS src KEY \"x'\(hexKey)'\";"
        }
        try db.exec(attachSql)
        defer {
            try? db.exec("DETACH DATABASE src;")
        }

        var appsAdded = 0
        var sessionsAdded = 0
        var sessionsSkipped = 0

        try db.transaction {
            try db.exec("CREATE TEMP TABLE idmap (src_id INTEGER PRIMARY KEY, dst_id INTEGER NOT NULL);")
            defer {
                try? db.exec("DROP TABLE IF EXISTS temp.idmap;")
            }

            // Read apps from src
            let srcAppsStmt = try db.prepare("SELECT id, canonical_id, exe_path, package_family, display_name, category, state, added_utc, retired_utc FROM src.apps ORDER BY id;")
            struct SrcApp {
                let id: Int64
                let canonicalId: Int64?
                let exePath: String
                let packageFamily: String?
                let displayName: String
                let category: String
                let state: String
                let addedUtc: Int64
                let retiredUtc: Int64?
            }

            var srcApps: [SrcApp] = []
            while try srcAppsStmt.step() {
                srcApps.append(SrcApp(
                    id: srcAppsStmt.columnInt64(at: 0),
                    canonicalId: srcAppsStmt.columnIsNull(at: 1) ? nil : srcAppsStmt.columnInt64(at: 1),
                    exePath: srcAppsStmt.columnText(at: 2) ?? "",
                    packageFamily: srcAppsStmt.columnIsNull(at: 3) ? nil : srcAppsStmt.columnText(at: 3),
                    displayName: srcAppsStmt.columnText(at: 4) ?? "",
                    category: srcAppsStmt.columnText(at: 5) ?? "",
                    state: srcAppsStmt.columnText(at: 6) ?? "",
                    addedUtc: srcAppsStmt.columnInt64(at: 7),
                    retiredUtc: srcAppsStmt.columnIsNull(at: 8) ? nil : srcAppsStmt.columnInt64(at: 8)
                ))
            }

            var inserted: [(src: Int64, dst: Int64, srcCanonical: Int64?)] = []
            let devId1 = try? db.scalarText("SELECT value FROM main.settings WHERE key = 'device.id';")
            let devId2 = try? db.scalarText("SELECT device_id FROM main.devices WHERE is_local = 1;")
            let localDevId = devId1 ?? devId2 ?? UUID().uuidString

            for a in srcApps {
                let findStmt = try db.prepare("SELECT id FROM main.apps WHERE exe_path = ? COLLATE NOCASE;")
                try findStmt.bind(text: a.exePath, at: 1)
                let dst: Int64
                if try findStmt.step() {
                    dst = findStmt.columnInt64(at: 0)
                } else {
                    let insStmt = try db.prepare("""
                        INSERT INTO main.apps (exe_path, package_family, display_name, category, state, added_utc, retired_utc, device_id)
                        VALUES (?, ?, ?, ?, ?, ?, ?, ?);
                    """)
                    try insStmt.bind(text: a.exePath, at: 1)
                    if let pf = a.packageFamily { try insStmt.bind(text: pf, at: 2) } else { try insStmt.bindNull(at: 2) }
                    try insStmt.bind(text: a.displayName, at: 3)
                    try insStmt.bind(text: a.category, at: 4)
                    try insStmt.bind(text: a.state, at: 5)
                    try insStmt.bind(int64: a.addedUtc, at: 6)
                    if let ret = a.retiredUtc { try insStmt.bind(int64: ret, at: 7) } else { try insStmt.bindNull(at: 7) }
                    try insStmt.bind(text: localDevId, at: 8)
                    _ = try insStmt.step()
                    dst = db.lastInsertRowid
                    inserted.append((src: a.id, dst: dst, srcCanonical: a.canonicalId))
                }

                let mapStmt = try db.prepare("INSERT INTO temp.idmap VALUES (?, ?);")
                try mapStmt.bind(int64: a.id, at: 1)
                try mapStmt.bind(int64: dst, at: 2)
                _ = try mapStmt.step()
            }

            // Remap canonical IDs for inserted apps
            for ins in inserted {
                if let srcCan = ins.srcCanonical {
                    let updateStmt = try db.prepare("UPDATE main.apps SET canonical_id = (SELECT dst_id FROM temp.idmap WHERE src_id = ?) WHERE id = ?;")
                    try updateStmt.bind(int64: srcCan, at: 1)
                    try updateStmt.bind(int64: ins.dst, at: 2)
                    _ = try updateStmt.step()
                }
            }
            appsAdded = inserted.count

            let totalSessions = Int(try db.scalarInt("SELECT COUNT(*) FROM src.sessions;") ?? 0)
            let beforeSessions = Int(try db.scalarInt("SELECT COUNT(*) FROM main.sessions;") ?? 0)

            try db.exec("""
                INSERT OR IGNORE INTO main.sessions (app_id, start_utc, end_utc, last_seen_utc, end_reason)
                SELECT m.dst_id, s.start_utc, COALESCE(s.end_utc, s.last_seen_utc), s.last_seen_utc, COALESCE(s.end_reason, 'restore')
                FROM src.sessions s JOIN temp.idmap m ON m.src_id = s.app_id;
            """)

            let afterSessions = Int(try db.scalarInt("SELECT COUNT(*) FROM main.sessions;") ?? 0)
            sessionsAdded = afterSessions - beforeSessions
            sessionsSkipped = totalSessions - sessionsAdded

            try db.exec("INSERT OR IGNORE INTO main.icons (app_id, png, hash) SELECT m.dst_id, i.png, i.hash FROM src.icons i JOIN temp.idmap m ON m.src_id = i.app_id;")
            try db.exec("INSERT OR IGNORE INTO main.ignored_suggestions (exe_path, utc) SELECT exe_path, utc FROM src.ignored_suggestions;")
        }

        let store = SQLiteSessionStore(db: db)
        let accumulator = UsageAccumulator(store: store, timeZone: zone)
        try accumulator.rebuildAll()

        return MergeResult(appsAdded: appsAdded, sessionsAdded: sessionsAdded, sessionsSkipped: sessionsSkipped)
    }

    private struct PreparedBackup {
        let dir: URL
        let dbPath: String
        let manifest: BackupManifest
        let migrated: Bool
        let isPlaintext: Bool
    }

    private static func prepare(backupFile: String, key: Data? = nil) throws -> PreparedBackup {
        guard FileManager.default.fileExists(atPath: backupFile) else {
            throw BackupError.generic("Backup file does not exist at \(backupFile)")
        }

        let tempDir = URL(fileURLWithPath: NSTemporaryDirectory())
            .appendingPathComponent("timesink-rs-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: tempDir, withIntermediateDirectories: true)

        let unzip = Process()
        unzip.executableURL = URL(fileURLWithPath: "/usr/bin/unzip")
        unzip.arguments = ["-q", backupFile, "-d", tempDir.path]
        try unzip.run()
        unzip.waitUntilExit()

        guard unzip.terminationStatus == 0 else {
            try? FileManager.default.removeItem(at: tempDir)
            throw BackupError.generic("Failed to unzip backup file")
        }

        let manifestFile = tempDir.appendingPathComponent(BackupManifest.entryName)
        guard FileManager.default.fileExists(atPath: manifestFile.path),
              let manifestData = try? Data(contentsOf: manifestFile),
              let manifest = try? BackupManifest.parse(jsonData: manifestData) else {
            try? FileManager.default.removeItem(at: tempDir)
            throw BackupError.unreadableManifest
        }

        let dbFile = tempDir.appendingPathComponent(BackupManifest.dbEntryName)
        guard FileManager.default.fileExists(atPath: dbFile.path) else {
            try? FileManager.default.removeItem(at: tempDir)
            throw BackupError.missingDatabase
        }

        if manifest.format != 1 {
            try? FileManager.default.removeItem(at: tempDir)
            throw BackupError.unknownFormat
        }

        let latestSupported = Migrator.latestVersion
        if manifest.schemaVersion > Int(latestSupported) {
            try? FileManager.default.removeItem(at: tempDir)
            throw BackupError.newerSchemaVersion(current: manifest.schemaVersion, supported: Int(latestSupported))
        }

        let isPlaintext = EncryptionMigrator.isPlaintextDatabase(at: dbFile.path)
        let db: SQLiteDatabase
        do {
            if isPlaintext {
                db = try SQLiteDatabase(path: dbFile.path, key: nil, allowPlaintext: true)
            } else {
                let resolvedKey = try (key ?? KeychainKeyManager.shared.getDatabaseKey() ?? KeychainKeyManager.shared.getOrCreateDatabaseKey())
                db = try SQLiteDatabase(path: dbFile.path, key: resolvedKey)
            }
        } catch {
            try? FileManager.default.removeItem(at: tempDir)
            throw BackupError.databaseCorrupt("Invalid encryption key or corrupt database: \(error.localizedDescription)")
        }

        let integrity = try db.scalarText("PRAGMA integrity_check;") ?? ""
        if integrity != "ok" {
            db.close()
            try? FileManager.default.removeItem(at: tempDir)
            throw BackupError.databaseCorrupt(integrity)
        }

        var migrated = false
        if db.userVersion > latestSupported {
            db.close()
            try? FileManager.default.removeItem(at: tempDir)
            throw BackupError.newerSchemaVersion(current: Int(db.userVersion), supported: Int(latestSupported))
        }
        if db.userVersion < latestSupported {
            try Migrator.apply(to: db)
            migrated = true
        }
        db.close()

        return PreparedBackup(dir: tempDir, dbPath: dbFile.path, manifest: manifest, migrated: migrated, isPlaintext: isPlaintext)
    }
}
