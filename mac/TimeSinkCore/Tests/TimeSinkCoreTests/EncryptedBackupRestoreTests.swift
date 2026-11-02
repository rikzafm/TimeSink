import XCTest
import Foundation
@testable import TimeSinkCore

final class EncryptedBackupRestoreTests: XCTestCase {
    var tempDir: URL!
    var mockKeyManager: InMemoryKeyManager!
    var originalSharedKeyManager: KeyManagerProtocol!

    override func setUpWithError() throws {
        tempDir = URL(fileURLWithPath: NSTemporaryDirectory()).appendingPathComponent("timesink-bk-enc-test-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: tempDir, withIntermediateDirectories: true)

        mockKeyManager = InMemoryKeyManager()
        originalSharedKeyManager = KeychainKeyManager.shared
        KeychainKeyManager.shared = mockKeyManager
    }

    override func tearDownWithError() throws {
        KeychainKeyManager.shared = originalSharedKeyManager
        try? FileManager.default.removeItem(at: tempDir)
    }

    func testEncryptedBackupContainsEncryptedSnapshot() throws {
        let liveDbPath = tempDir.appendingPathComponent("live.db").path
        let backupFile = tempDir.appendingPathComponent("backup.timesink").path
        let key = try mockKeyManager.getOrCreateDatabaseKey()

        let db = try SQLiteDatabase(path: liveDbPath, key: key)
        try Migrator.apply(to: db)
        try db.exec("""
            INSERT OR IGNORE INTO devices (device_id, name, platform, is_local, first_seen_utc, state)
            VALUES ('dev-1', 'Device', 'macos', 1, '2026-10-23T12:00:00Z', 'active');
            INSERT INTO apps (exe_path, display_name, category, state, added_utc, device_id)
            VALUES ('/Applications/Test.app', 'Test App', 'Other', 'tracked', 1700000000, 'dev-1');
        """)
        db.close()

        try BackupService.create(dbPath: liveDbPath, destFile: backupFile, key: key)
        XCTAssertTrue(FileManager.default.fileExists(atPath: backupFile))

        // Unzip and inspect raw snapshot database file
        let extractDir = tempDir.appendingPathComponent("extracted")
        try FileManager.default.createDirectory(at: extractDir, withIntermediateDirectories: true)

        let unzip = Process()
        unzip.executableURL = URL(fileURLWithPath: "/usr/bin/unzip")
        unzip.arguments = ["-q", backupFile, "-d", extractDir.path]
        try unzip.run()
        unzip.waitUntilExit()

        let extractedDb = extractDir.appendingPathComponent(BackupManifest.dbEntryName).path
        XCTAssertTrue(FileManager.default.fileExists(atPath: extractedDb))

        // Must NOT be plaintext
        XCTAssertFalse(EncryptionMigrator.isPlaintextDatabase(at: extractedDb), "Snapshot inside backup archive must be encrypted")

        // Must be openable with the encryption key
        let snapshotDb = try SQLiteDatabase(path: extractedDb, key: key)
        let count = try snapshotDb.scalarInt("SELECT COUNT(*) FROM apps;")
        snapshotDb.close()
        XCTAssertEqual(count, 1)
    }

    func testReplaceEncryptedBackupRoundTrip() throws {
        let liveDbPath = tempDir.appendingPathComponent("live.db").path
        let targetDbPath = tempDir.appendingPathComponent("target.db").path
        let backupFile = tempDir.appendingPathComponent("source.timesink").path
        let safetyFolder = tempDir.appendingPathComponent("safety").path
        let key = try mockKeyManager.getOrCreateDatabaseKey()

        // 1. Setup source live database
        let db = try SQLiteDatabase(path: liveDbPath, key: key)
        try Migrator.apply(to: db)
        try db.exec("""
            INSERT OR IGNORE INTO devices (device_id, name, platform, is_local, first_seen_utc, state)
            VALUES ('dev-1', 'Device', 'macos', 1, '2026-10-23T12:00:00Z', 'active');
            INSERT INTO apps (exe_path, display_name, category, state, added_utc, device_id)
            VALUES ('/Applications/App1.app', 'App 1', 'Work', 'tracked', 1700000000, 'dev-1');
            INSERT INTO apps (exe_path, display_name, category, state, added_utc, device_id)
            VALUES ('/Applications/App2.app', 'App 2', 'Games', 'tracked', 1700000000, 'dev-1');
            INSERT INTO sessions (app_id, start_utc, end_utc, last_seen_utc, end_reason)
            VALUES (1, 1700000000, 1700003600, 1700003600, 'exit');
            INSERT INTO sessions (app_id, start_utc, end_utc, last_seen_utc, end_reason)
            VALUES (2, 1700010000, 1700011800, 1700011800, 'exit');
        """)
        let acc = UsageAccumulator(store: SQLiteSessionStore(db: db), timeZone: TimeZone(secondsFromGMT: 0)!)
        try acc.rebuildAll()
        db.close()

        // 2. Create backup
        try BackupService.create(dbPath: liveDbPath, destFile: backupFile, key: key)

        // 3. Inspect backup
        let inspection = try BackupService.inspect(backupFile: backupFile, key: key)
        XCTAssertEqual(inspection.apps, 2)
        XCTAssertEqual(inspection.sessions, 2)

        // 4. Initialize target database
        let targetDb = try SQLiteDatabase(path: targetDbPath, key: key)
        try Migrator.apply(to: targetDb)
        targetDb.close()

        // 5. Replace into target database
        let result = try BackupService.replace(
            backupFile: backupFile,
            dbPath: targetDbPath,
            safetyFolder: safetyFolder,
            now: Date(),
            zone: TimeZone(secondsFromGMT: 0)!,
            key: key
        )
        XCTAssertNotNil(result.safetyCopy)

        // 6. Verify target database content
        let restoredDb = try SQLiteDatabase(path: targetDbPath, key: key)
        let appsCount = try restoredDb.scalarInt("SELECT COUNT(*) FROM apps;")
        let sessionsCount = try restoredDb.scalarInt("SELECT COUNT(*) FROM sessions;")
        let totalUsage = try restoredDb.scalarInt("SELECT SUM(seconds) FROM daily_usage;")
        restoredDb.close()

        XCTAssertEqual(appsCount, 2)
        XCTAssertEqual(sessionsCount, 2)
        XCTAssertEqual(totalUsage, 5400) // 3600 + 1800
    }

    func testReplaceLegacyPlaintextBackupIntoEncryptedDatabase() throws {
        let legacyBackup = tempDir.appendingPathComponent("legacy.timesink").path
        let targetDbPath = tempDir.appendingPathComponent("target_encrypted.db").path
        let safetyFolder = tempDir.appendingPathComponent("safety").path
        let key = try mockKeyManager.getOrCreateDatabaseKey()

        // 1. Create a legacy unencrypted backup archive
        let pkgDir = tempDir.appendingPathComponent("legacy-pkg")
        try FileManager.default.createDirectory(at: pkgDir, withIntermediateDirectories: true)

        let plainDbPath = pkgDir.appendingPathComponent(BackupManifest.dbEntryName).path
        let plainDb = try SQLiteDatabase(path: plainDbPath, key: nil, allowPlaintext: true)
        try Migrator.apply(to: plainDb)
        try plainDb.exec("""
            INSERT OR IGNORE INTO devices (device_id, name, platform, is_local, first_seen_utc, state)
            VALUES ('dev-plain', 'Plain Device', 'macos', 1, '2026-10-23T12:00:00Z', 'active');
            INSERT INTO apps (exe_path, display_name, category, state, added_utc, device_id)
            VALUES ('/Applications/Legacy.app', 'Legacy App', 'Utilities', 'tracked', 1700000000, 'dev-plain');
            INSERT INTO sessions (app_id, start_utc, end_utc, last_seen_utc, end_reason)
            VALUES (1, 1700000000, 1700000600, 1700000600, 'exit');
        """)
        let version = plainDb.userVersion
        plainDb.close()

        let manifest = BackupManifest(format: 1, schemaVersion: Int(version), appVersion: "1.0.0", createdUtc: "2026-10-23T12:00:00Z")
        let manifestData = try manifest.toJsonData()
        let manifestPath = pkgDir.appendingPathComponent(BackupManifest.entryName).path
        try manifestData.write(to: URL(fileURLWithPath: manifestPath))

        let zip = Process()
        zip.executableURL = URL(fileURLWithPath: "/usr/bin/zip")
        zip.arguments = ["-q", "-j", legacyBackup, manifestPath, plainDbPath]
        try zip.run()
        zip.waitUntilExit()

        // 2. Setup target encrypted database
        let targetDb = try SQLiteDatabase(path: targetDbPath, key: key)
        try Migrator.apply(to: targetDb)
        targetDb.close()

        // 3. Restore legacy unencrypted backup into encrypted database
        _ = try BackupService.replace(
            backupFile: legacyBackup,
            dbPath: targetDbPath,
            safetyFolder: safetyFolder,
            now: Date(),
            zone: TimeZone(secondsFromGMT: 0)!,
            key: key
        )

        // 4. Verify target database has restored data AND remains encrypted
        XCTAssertFalse(EncryptionMigrator.isPlaintextDatabase(at: targetDbPath))

        let restoredDb = try SQLiteDatabase(path: targetDbPath, key: key)
        let appsCount = try restoredDb.scalarInt("SELECT COUNT(*) FROM apps;")
        let sessionsCount = try restoredDb.scalarInt("SELECT COUNT(*) FROM sessions;")
        let appName = try restoredDb.scalarText("SELECT display_name FROM apps WHERE id = 1;")
        restoredDb.close()

        XCTAssertEqual(appsCount, 1)
        XCTAssertEqual(sessionsCount, 1)
        XCTAssertEqual(appName, "Legacy App")
    }

    func testMergeEncryptedBackupIntoEncryptedDatabase() throws {
        let liveDbPath = tempDir.appendingPathComponent("live.db").path
        let otherDbPath = tempDir.appendingPathComponent("other.db").path
        let otherBackup = tempDir.appendingPathComponent("other.timesink").path
        let key = try mockKeyManager.getOrCreateDatabaseKey()

        // Live database
        let liveDb = try SQLiteDatabase(path: liveDbPath, key: key)
        try Migrator.apply(to: liveDb)
        try liveDb.exec("""
            INSERT OR IGNORE INTO devices (device_id, name, platform, is_local, first_seen_utc, state)
            VALUES ('dev-1', 'Device 1', 'macos', 1, '2026-10-23T12:00:00Z', 'active');
            INSERT INTO apps (exe_path, display_name, category, state, added_utc, device_id)
            VALUES ('/Applications/Shared.app', 'Shared App', 'Other', 'tracked', 1700000000, 'dev-1');
            INSERT INTO sessions (app_id, start_utc, end_utc, last_seen_utc, end_reason)
            VALUES (1, 1700000000, 1700001000, 1700001000, 'exit');
        """)
        liveDb.close()

        // Other database
        let otherDb = try SQLiteDatabase(path: otherDbPath, key: key)
        try Migrator.apply(to: otherDb)
        try otherDb.exec("""
            INSERT OR IGNORE INTO devices (device_id, name, platform, is_local, first_seen_utc, state)
            VALUES ('dev-2', 'Device 2', 'macos', 0, '2026-10-23T12:00:00Z', 'active');
            INSERT INTO apps (exe_path, display_name, category, state, added_utc, device_id)
            VALUES ('/Applications/Shared.app', 'Shared App', 'Other', 'tracked', 1700000000, 'dev-2');
            INSERT INTO apps (exe_path, display_name, category, state, added_utc, device_id)
            VALUES ('/Applications/New.app', 'New App', 'Productivity', 'tracked', 1700000000, 'dev-2');
            INSERT INTO sessions (app_id, start_utc, end_utc, last_seen_utc, end_reason)
            VALUES (1, 1700000000, 1700001000, 1700001000, 'exit');
            INSERT INTO sessions (app_id, start_utc, end_utc, last_seen_utc, end_reason)
            VALUES (2, 1700002000, 1700002500, 1700002500, 'exit');
        """)
        otherDb.close()

        try BackupService.create(dbPath: otherDbPath, destFile: otherBackup, key: key)

        // Merge into live
        let mergeResult = try BackupService.merge(
            backupFile: otherBackup,
            dbPath: liveDbPath,
            zone: TimeZone(secondsFromGMT: 0)!,
            key: key
        )

        XCTAssertEqual(mergeResult.appsAdded, 1) // New.app
        XCTAssertEqual(mergeResult.sessionsAdded, 1) // New.app session
        XCTAssertEqual(mergeResult.sessionsSkipped, 1) // Shared.app duplicate start_utc

        let verifyDb = try SQLiteDatabase(path: liveDbPath, key: key)
        let totalApps = try verifyDb.scalarInt("SELECT COUNT(*) FROM apps;")
        let totalSessions = try verifyDb.scalarInt("SELECT COUNT(*) FROM sessions;")
        verifyDb.close()

        XCTAssertEqual(totalApps, 2)
        XCTAssertEqual(totalSessions, 2)
    }
}

