import Foundation
import CryptoKit

public enum SyncWriter {
    public static let `extension` = ".tssync"

    public static func write(
        db: SQLiteDatabase,
        syncFolder: String,
        now: Date = Date(),
        appVersion: String = "1.0.0"
    ) throws -> String {
        let fm = FileManager.default
        try fm.createDirectory(atPath: syncFolder, withIntermediateDirectories: true)

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

        // 1. Create snapshot.db
        try createSnapshotDatabase(sourceDb: db, snapshotDbPath: snapshotDbPath, localDeviceId: localDeviceId)

        // 2. Count sessions and calculate SHA-256
        let snapDb = try SQLiteDatabase(path: snapshotDbPath)
        let sessionCount = Int((try snapDb.scalarInt("SELECT COUNT(*) FROM sessions;")) ?? 0)

        let snapData = try Data(contentsOf: URL(fileURLWithPath: snapshotDbPath))
        let hashBytes = SHA256.hash(data: snapData)
        let hashHex = hashBytes.map { String(format: "%02x", $0) }.joined()

        // 3. Create manifest.json
        let isoFormatter = ISO8601DateFormatter()
        let manifest = SyncManifest(
            format: 1,
            schemaVersion: 4,
            appVersion: appVersion,
            deviceId: localDeviceId,
            deviceName: localDeviceName,
            platform: platform,
            createdUtc: isoFormatter.string(from: now),
            sessionCount: sessionCount,
            hash: hashHex
        )

        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        let manifestData = try encoder.encode(manifest)
        try manifestData.write(to: URL(fileURLWithPath: manifestPath))

        // 4. Create ZIP archive using /usr/bin/zip
        if fm.fileExists(atPath: tmpFile) {
            try fm.removeItem(atPath: tmpFile)
        }

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
        if fm.fileExists(atPath: targetFile) {
            try fm.removeItem(atPath: targetFile)
        }
        try fm.moveItem(atPath: tmpFile, toPath: targetFile)

        return targetFile
    }

    private static func createSnapshotDatabase(sourceDb: SQLiteDatabase, snapshotDbPath: String, localDeviceId: String) throws {
        let snapDb = try SQLiteDatabase(path: snapshotDbPath)
        try snapDb.exec("""
            PRAGMA user_version = 4;

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
                kind           INTEGER NULL
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
            """)

        let escapedSnapPath = snapshotDbPath.replacingOccurrences(of: "'", with: "''")
        try sourceDb.exec("ATTACH DATABASE '\(escapedSnapPath)' AS snap;")
        defer {
            try? sourceDb.exec("DETACH DATABASE snap;")
        }

        let escapedDev = localDeviceId.replacingOccurrences(of: "'", with: "''")
        try sourceDb.exec("""
            INSERT INTO snap.apps (
                id, canonical_id, exe_path, bundle_id, device_id,
                package_family, display_name, category, state,
                added_utc, retired_utc, is_background, kind
            )
            SELECT
                id, canonical_id, exe_path, bundle_id, device_id,
                package_family, display_name, category, state,
                added_utc, retired_utc, is_background, kind
            FROM main.apps
            WHERE device_id = '\(escapedDev)';

            INSERT INTO snap.sessions (
                id, app_id, start_utc, end_utc, last_seen_utc, pid, proc_start_utc, end_reason
            )
            SELECT
                s.id, s.app_id, s.start_utc, s.end_utc, s.last_seen_utc, s.pid, s.proc_start_utc, s.end_reason
            FROM main.sessions s
            JOIN main.apps a ON a.id = s.app_id
            WHERE a.device_id = '\(escapedDev)';

            INSERT INTO snap.icons (app_id, png, hash)
            SELECT
                i.app_id, i.png, i.hash
            FROM main.icons i
            JOIN main.apps a ON a.id = i.app_id
            WHERE a.device_id = '\(escapedDev)';

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
    }
}

