import Foundation
import CryptoKit
#if canImport(SQLCipher)
import SQLCipher
#elseif canImport(SQLite3)
import SQLite3
#endif

public enum SyncWriter {
    public static let `extension` = ".tssync"

    public static func write(
        db: SQLiteDatabase,
        syncFolder: String,
        now: Date = Date(),
        appVersion: String = "1.0.0",
        keyManager: KeyManagerProtocol = KeychainKeyManager.shared
    ) throws -> String {
        let fm = FileManager.default
        try fm.createDirectory(atPath: syncFolder, withIntermediateDirectories: true)

        let settingEncrypted = (try? db.scalarText("SELECT value FROM settings WHERE key = 'sync.encrypted';"))?.lowercased() == "true"
        let isEncrypted = settingEncrypted || keyManager.hasSyncPassphrase()
        var passphrase: String? = nil
        if isEncrypted {
            passphrase = try keyManager.getSyncPassphrase()
            if passphrase == nil || passphrase!.isEmpty {
                throw SQLiteError.generic("Sync encryption is enabled but no passphrase is configured.")
            }
        }

        let localDeviceId = (try db.scalarText("SELECT value FROM settings WHERE key = 'device.id';")) ?? ""
        let localDeviceName = (try db.scalarText("SELECT value FROM settings WHERE key = 'device.name';")) ?? "Mac"
        let platform = "macos"

        let targetFile = (syncFolder as NSString).appendingPathComponent("\(localDeviceId)\(`extension`)")
        let tmpFile = (syncFolder as NSString).appendingPathComponent("\(localDeviceId)\(`extension`).tmp")

        let scratchDir = (NSTemporaryDirectory() as NSString).appendingPathComponent("timesink-sync-out-\(UUID().uuidString)")
        try fm.createDirectory(atPath: scratchDir, withIntermediateDirectories: true)

        defer {
            try? fm.removeItem(atPath: tmpFile)
            try? fm.removeItem(atPath: scratchDir)
        }

        let snapshotDbPath = (scratchDir as NSString).appendingPathComponent(SyncManifest.dbEntryName)
        let manifestPath = (scratchDir as NSString).appendingPathComponent(SyncManifest.entryName)

        // 1. Create snapshot.db (encrypted with SQLCipher if configured)
        try createSnapshotDatabase(sourceDb: db, snapshotDbPath: snapshotDbPath, localDeviceId: localDeviceId, passphrase: passphrase)

        // 2. Count sessions and calculate SHA-256 over ciphertext file bytes
        let sessionCount = try countSessions(snapshotDbPath: snapshotDbPath, passphrase: passphrase)

        let snapData = try Data(contentsOf: URL(fileURLWithPath: snapshotDbPath))
        let hashBytes = SHA256.hash(data: snapData)
        let hashHex = hashBytes.map { String(format: "%02x", $0) }.joined()

        // 3. Create manifest.json (schemaVersion 5, encrypted flag)
        let isoFormatter = ISO8601DateFormatter()
        let manifest = SyncManifest(
            format: 1,
            schemaVersion: 5,
            appVersion: appVersion,
            deviceId: localDeviceId,
            deviceName: localDeviceName,
            platform: platform,
            createdUtc: isoFormatter.string(from: now),
            sessionCount: sessionCount,
            hash: hashHex,
            encrypted: isEncrypted
        )

        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        let manifestData = try encoder.encode(manifest)
        try manifestData.write(to: URL(fileURLWithPath: manifestPath))

        // 4. Create ZIP archive using /usr/bin/zip
        try? fm.removeItem(atPath: tmpFile)

        let proc = Process()
        proc.executableURL = URL(fileURLWithPath: "/usr/bin/zip")
        proc.currentDirectoryURL = URL(fileURLWithPath: scratchDir)
        proc.arguments = ["-q", "-j", "-r", tmpFile, manifestPath, snapshotDbPath]
        try proc.run()
        proc.waitUntilExit()

        guard proc.terminationStatus == 0 else {
            throw SQLiteError.generic("Failed to create sync archive at \(tmpFile)")
        }

        // 5. Atomic rename to targetFile
        try? fm.removeItem(atPath: targetFile)
        try fm.moveItem(atPath: tmpFile, toPath: targetFile)

        return targetFile
    }

    private static func countSessions(snapshotDbPath: String, passphrase: String?) throws -> Int {
        var handle: OpaquePointer?
        let flags = SQLITE_OPEN_READONLY | SQLITE_OPEN_FULLMUTEX
        guard sqlite3_open_v2(snapshotDbPath, &handle, flags, nil) == SQLITE_OK, let h = handle else {
            let msg = handle != nil ? String(cString: sqlite3_errmsg(handle)) : "Failed to open snapshot to count sessions"
            if let h = handle { sqlite3_close_v2(h) }
            throw SQLiteError.openFailed(msg)
        }
        defer { sqlite3_close_v2(h) }

        if let passphrase = passphrase, !passphrase.isEmpty {
            let escPass = passphrase.replacingOccurrences(of: "'", with: "''")
            var err: UnsafeMutablePointer<CChar>?
            if sqlite3_exec(h, "PRAGMA key = '\(escPass)';", nil, nil, &err) != SQLITE_OK {
                let msg = err != nil ? String(cString: err!) : "Failed to set PRAGMA key"
                sqlite3_free(err)
                throw SQLiteError.executionFailed(msg)
            }
        }

        var stmt: OpaquePointer?
        guard sqlite3_prepare_v2(h, "SELECT COUNT(*) FROM sessions;", -1, &stmt, nil) == SQLITE_OK, let s = stmt else {
            let msg = String(cString: sqlite3_errmsg(h))
            throw SQLiteError.prepareFailed(msg)
        }
        defer { sqlite3_finalize(s) }

        if sqlite3_step(s) == SQLITE_ROW {
            return Int(sqlite3_column_int64(s, 0))
        }
        return 0
    }

    private static func createSnapshotDatabase(
        sourceDb: SQLiteDatabase,
        snapshotDbPath: String,
        localDeviceId: String,
        passphrase: String?
    ) throws {
        // Build empty tables in snapshot.db with SQLCipher keying if encrypted
        var snapHandle: OpaquePointer?
        let flags = SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE | SQLITE_OPEN_FULLMUTEX
        let result = sqlite3_open_v2(snapshotDbPath, &snapHandle, flags, nil)
        guard result == SQLITE_OK, let validHandle = snapHandle else {
            let msg = snapHandle != nil ? String(cString: sqlite3_errmsg(snapHandle)) : "Failed to create snapshot database"
            if let snapHandle = snapHandle { sqlite3_close_v2(snapHandle) }
            throw SQLiteError.openFailed(msg)
        }

        if let passphrase = passphrase, !passphrase.isEmpty {
            let escPass = passphrase.replacingOccurrences(of: "'", with: "''")
            var errMsg: UnsafeMutablePointer<CChar>?
            let pragmaSql = "PRAGMA key = '\(escPass)'; PRAGMA cipher_page_size = 4096; PRAGMA kdf_iter = 256000;"
            if sqlite3_exec(validHandle, pragmaSql, nil, nil, &errMsg) != SQLITE_OK {
                let msg = errMsg != nil ? String(cString: errMsg!) : String(cString: sqlite3_errmsg(validHandle))
                sqlite3_free(errMsg)
                sqlite3_close_v2(validHandle)
                throw SQLiteError.executionFailed(msg)
            }
        }

        let schemaSql = """
            PRAGMA journal_mode = DELETE;
            PRAGMA user_version = 5;

            CREATE TABLE apps (
                id             INTEGER PRIMARY KEY,
                canonical_id   INTEGER NULL,
                exe_path       TEXT NOT NULL COLLATE NOCASE,
                bundle_id      TEXT NULL,
                device_id      TEXT NOT NULL,
                package_family TEXT NULL,
                display_name   TEXT NOT NULL,
                category       TEXT NOT NULL DEFAULT 'Other',
                state          TEXT NOT NULL DEFAULT 'tracked' CHECK (state IN ('tracked','retired')),
                added_utc      INTEGER NOT NULL,
                retired_utc    INTEGER NULL,
                is_background  INTEGER NOT NULL DEFAULT 0,
                kind           INTEGER NULL,
                updated_utc    INTEGER NOT NULL DEFAULT 0
            );
            CREATE UNIQUE INDEX ux_apps_device_exe ON apps(device_id, exe_path COLLATE NOCASE);
            CREATE INDEX ix_apps_device_bundle ON apps(device_id, bundle_id);
            CREATE INDEX ix_apps_canonical ON apps(canonical_id);

            CREATE TABLE sessions (
                id            INTEGER PRIMARY KEY,
                app_id        INTEGER NOT NULL REFERENCES apps(id),
                start_utc     INTEGER NOT NULL,
                end_utc       INTEGER NULL,
                last_seen_utc INTEGER NOT NULL,
                pid           INTEGER NULL,
                proc_start_utc INTEGER NULL,
                end_reason    TEXT NULL
            );
            CREATE INDEX ix_sessions_app_start ON sessions(app_id, start_utc);
            CREATE UNIQUE INDEX ux_sessions_app_start ON sessions(app_id, start_utc);

            CREATE TABLE icons (
                app_id INTEGER PRIMARY KEY REFERENCES apps(id),
                png BLOB NOT NULL,
                hash TEXT NOT NULL
            );

            CREATE TABLE cross_device_merges (
                source_device_id TEXT NOT NULL,
                source_exe_path TEXT NOT NULL DEFAULT '',
                source_bundle_id TEXT NOT NULL DEFAULT '',
                target_device_id TEXT NOT NULL,
                target_exe_path TEXT NOT NULL DEFAULT '',
                target_bundle_id TEXT NOT NULL DEFAULT ''
            );
            """

        var schemaErrMsg: UnsafeMutablePointer<CChar>?
        if sqlite3_exec(validHandle, schemaSql, nil, nil, &schemaErrMsg) != SQLITE_OK {
            let msg = schemaErrMsg != nil ? String(cString: schemaErrMsg!) : String(cString: sqlite3_errmsg(validHandle))
            sqlite3_free(schemaErrMsg)
            sqlite3_close_v2(validHandle)
            throw SQLiteError.executionFailed(msg)
        }
        sqlite3_close_v2(validHandle)

        // Attach snapshotDb to sourceDb with appropriate key
        let escapedSnapPath = snapshotDbPath.replacingOccurrences(of: "'", with: "''")
        if let passphrase = passphrase, !passphrase.isEmpty {
            let escPass = passphrase.replacingOccurrences(of: "'", with: "''")
            try sourceDb.exec("ATTACH DATABASE '\(escapedSnapPath)' AS snap KEY '\(escPass)';")
        } else {
            try sourceDb.exec("ATTACH DATABASE '\(escapedSnapPath)' AS snap KEY '';")
        }
        defer {
            try? sourceDb.exec("DETACH DATABASE snap;")
        }

        let escapedDev = localDeviceId.replacingOccurrences(of: "'", with: "''")
        try sourceDb.exec("""
            INSERT OR IGNORE INTO snap.apps (
                id, canonical_id, exe_path, bundle_id, device_id,
                package_family, display_name, category, state,
                added_utc, retired_utc, is_background, kind, updated_utc
            )
            SELECT DISTINCT
                id, canonical_id, exe_path, bundle_id, device_id,
                package_family, display_name, category, state,
                added_utc, retired_utc, is_background, kind, updated_utc
            FROM main.apps
            WHERE device_id = '\(escapedDev)'
               OR updated_utc > 0
               OR id IN (SELECT canonical_id FROM main.apps WHERE device_id = '\(escapedDev)' AND canonical_id IS NOT NULL)
               OR canonical_id IN (SELECT id FROM main.apps WHERE device_id = '\(escapedDev)');

            INSERT INTO snap.sessions (
                id, app_id, start_utc, end_utc, last_seen_utc, pid, proc_start_utc, end_reason
            )
            SELECT
                s.id, s.app_id, s.start_utc, s.end_utc, s.last_seen_utc, s.pid, s.proc_start_utc, s.end_reason
            FROM main.sessions s
            JOIN main.apps a ON a.id = s.app_id
            WHERE a.device_id = '\(escapedDev)';

            INSERT OR IGNORE INTO snap.icons (app_id, png, hash)
            SELECT
                i.app_id, i.png, i.hash
            FROM main.icons i
            JOIN snap.apps a ON a.id = i.app_id;

            INSERT INTO snap.cross_device_merges (
                source_device_id, source_exe_path, source_bundle_id,
                target_device_id, target_exe_path, target_bundle_id
            )
            SELECT
                a.device_id, a.exe_path, COALESCE(a.bundle_id, ''),
                c.device_id, c.exe_path, COALESCE(c.bundle_id, '')
            FROM main.apps a
            JOIN main.apps c ON c.id = a.canonical_id
            WHERE (a.device_id = '\(escapedDev)' OR c.device_id = '\(escapedDev)')
              AND a.device_id != c.device_id;
            """)

        let selectCmd = try sourceDb.prepare("SELECT id, exe_path, display_name FROM snap.apps WHERE kind IS NULL;")
        var appsToResolve: [(Int64, String, String)] = []
        while try selectCmd.step() {
            appsToResolve.append((
                selectCmd.columnInt64(at: 0),
                selectCmd.columnText(at: 1) ?? "",
                selectCmd.columnText(at: 2) ?? ""
            ))
        }
        for (id, path, name) in appsToResolve {
            let resolved = AppKinds.resolve(kind: nil, exePath: path, displayName: name)
            let upd = try sourceDb.prepare("UPDATE snap.apps SET kind = ?1 WHERE id = ?2;")
            try upd.bind(int64: resolved.rawValue, at: 1)
            try upd.bind(int64: id, at: 2)
            _ = try upd.step()
        }
    }
}

