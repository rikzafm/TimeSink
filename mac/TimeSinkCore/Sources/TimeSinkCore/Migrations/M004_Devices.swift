import Foundation

public struct M004_Devices: Migration {
    public let version: Int32 = 4

    public init() {}

    public func apply(to db: SQLiteDatabase) throws {
        // 1. Safety backup if physical file exists
        createSafetyBackup(db: db)

        // 2. Create devices table
        try db.exec("""
        CREATE TABLE IF NOT EXISTS devices (
            device_id       TEXT PRIMARY KEY,
            name            TEXT NOT NULL,
            platform        TEXT NOT NULL,
            is_local        INTEGER NOT NULL,
            first_seen_utc  TEXT NOT NULL,
            last_import_utc TEXT,
            last_file_utc   TEXT,
            state           TEXT NOT NULL DEFAULT 'active' CHECK (state IN ('active','forgotten'))
        );
        """)

        // 3. Resolve or create local device id and name
        var deviceId = try db.scalarText("SELECT value FROM settings WHERE key = 'device.id';") ?? ""
        var deviceName = try db.scalarText("SELECT value FROM settings WHERE key = 'device.name';") ?? ""

        if deviceId.isEmpty {
            deviceId = UUID().uuidString
            let stmt = try db.prepare("INSERT INTO settings (key, value) VALUES ('device.id', ?1) ON CONFLICT(key) DO UPDATE SET value = ?1;")
            try stmt.bind(text: deviceId, at: 1)
            _ = try stmt.step()
        }

        if deviceName.isEmpty {
            deviceName = Host.current().localizedName ?? "Mac"
            if deviceName.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                deviceName = "Mac"
            }
            let stmt = try db.prepare("INSERT INTO settings (key, value) VALUES ('device.name', ?1) ON CONFLICT(key) DO UPDATE SET value = ?1;")
            try stmt.bind(text: deviceName, at: 1)
            _ = try stmt.step()
        }

        let nowIso = ISO8601DateFormatter().string(from: Date())
        let regStmt = try db.prepare("""
        INSERT INTO devices (device_id, name, platform, is_local, first_seen_utc, state)
        VALUES (?1, ?2, 'macos', 1, ?3, 'active')
        ON CONFLICT(device_id) DO UPDATE SET is_local = 1, state = 'active';
        """)
        try regStmt.bind(text: deviceId, at: 1)
        try regStmt.bind(text: deviceName, at: 2)
        try regStmt.bind(text: nowIso, at: 3)
        _ = try regStmt.step()

        // 4. Rebuild apps table if device_id column does not exist yet
        let hasDeviceId = (try db.scalarInt("SELECT COUNT(*) FROM pragma_table_info('apps') WHERE name = 'device_id';") ?? 0) > 0
        if !hasDeviceId {
            try rebuildAppsTable(db: db, defaultDeviceId: deviceId)
        }
    }

    private func rebuildAppsTable(db: SQLiteDatabase, defaultDeviceId: String) throws {
        try db.exec("PRAGMA foreign_keys = OFF;")

        try db.exec("""
        CREATE TABLE apps_new (
            id             INTEGER PRIMARY KEY,
            canonical_id   INTEGER NULL REFERENCES apps_new(id),
            exe_path       TEXT NOT NULL COLLATE NOCASE,
            bundle_id      TEXT NULL,
            device_id      TEXT NOT NULL REFERENCES devices(device_id),
            package_family TEXT NULL,
            display_name   TEXT NOT NULL,
            category       TEXT NOT NULL DEFAULT 'Other',
            state          TEXT NOT NULL DEFAULT 'tracked' CHECK (state IN ('tracked','retired')),
            added_utc      INTEGER NOT NULL,
            retired_utc    INTEGER NULL,
            is_background  INTEGER NOT NULL DEFAULT 0,
            kind           INTEGER NULL
        );

        CREATE UNIQUE INDEX ux_apps_device_exe ON apps_new(device_id, exe_path COLLATE NOCASE);
        CREATE INDEX ix_apps_device_bundle ON apps_new(device_id, bundle_id);
        CREATE INDEX ix_apps_canonical ON apps_new(canonical_id);
        """)

        let copyStmt = try db.prepare("""
        INSERT INTO apps_new (
            id, canonical_id, exe_path, bundle_id, device_id,
            package_family, display_name, category, state,
            added_utc, retired_utc, is_background, kind
        )
        SELECT
            id, canonical_id, exe_path, NULL, ?1,
            package_family, display_name, category, state,
            added_utc, retired_utc, is_background, kind
        FROM apps;
        """)
        try copyStmt.bind(text: defaultDeviceId, at: 1)
        _ = try copyStmt.step()

        try db.exec("""
        DROP TABLE apps;
        ALTER TABLE apps_new RENAME TO apps;
        PRAGMA foreign_keys = ON;
        """)
    }

    private func createSafetyBackup(db: SQLiteDatabase) {
        let dbPath = db.path
        guard !dbPath.isEmpty, dbPath != ":memory:", FileManager.default.fileExists(atPath: dbPath) else {
            return
        }

        do {
            let backupDir = (dbPath as NSString).deletingLastPathComponent + "/backups"
            try FileManager.default.createDirectory(atPath: backupDir, withIntermediateDirectories: true)
            let formatter = DateFormatter()
            formatter.dateFormat = "yyyyMMdd-HHmmss"
            let timestamp = formatter.string(from: Date())
            let destFile = "\(backupDir)/pre-migrate-\(timestamp).timesink"
            try db.backup(to: destFile)
        } catch {
            // Non-fatal safety backup failure
        }
    }
}
