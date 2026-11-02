import XCTest
import Foundation
@testable import TimeSinkCore

final class EncryptionMigrationTests: XCTestCase {
    var tempDir: URL!
    var mockKeyManager: InMemoryKeyManager!
    var originalSharedKeyManager: KeyManagerProtocol!

    override func setUpWithError() throws {
        tempDir = URL(fileURLWithPath: NSTemporaryDirectory()).appendingPathComponent("timesink-mig-test-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: tempDir, withIntermediateDirectories: true)

        mockKeyManager = InMemoryKeyManager()
        originalSharedKeyManager = KeychainKeyManager.shared
        KeychainKeyManager.shared = mockKeyManager
    }

    override func tearDownWithError() throws {
        KeychainKeyManager.shared = originalSharedKeyManager
        try? FileManager.default.removeItem(at: tempDir)
    }

    func testSyntheticPlaintextDetection() throws {
        let dbPath = tempDir.appendingPathComponent("plain.db").path
        let db = try SQLiteDatabase(path: dbPath, key: nil, allowPlaintext: true)
        try db.exec("CREATE TABLE test (val INT);")
        db.close()

        XCTAssertTrue(EncryptionMigrator.isPlaintextDatabase(at: dbPath))
    }

    func testSyntheticPlaintextMigrationFlow() throws {
        let dbPath = tempDir.appendingPathComponent("legacy.db").path
        let backupFolder = tempDir.appendingPathComponent("backups").path
        let key = try mockKeyManager.getOrCreateDatabaseKey()

        // 1. Create synthetic unencrypted SQLite database
        let plainDb = try SQLiteDatabase(path: dbPath, key: nil, allowPlaintext: true)
        try Migrator.apply(to: plainDb)
        try plainDb.exec("""
            INSERT OR IGNORE INTO devices (device_id, name, platform, is_local, first_seen_utc, state)
            VALUES ('dev-1', 'MacBook', 'macos', 1, '2026-10-23T12:00:00Z', 'active');
            INSERT INTO apps (exe_path, display_name, category, state, added_utc, device_id)
            VALUES ('/Applications/Safari.app', 'Safari', 'Browsers', 'tracked', 1700000000, 'dev-1');
            INSERT INTO sessions (app_id, start_utc, end_utc, last_seen_utc, end_reason)
            VALUES (1, 1700000000, 1700001800, 1700001800, 'exit');
            INSERT INTO settings (key, value) VALUES ('device.id', 'dev-1');
        """)
        let originalVersion = plainDb.userVersion
        plainDb.close()

        XCTAssertTrue(EncryptionMigrator.isPlaintextDatabase(at: dbPath))

        // 2. Perform migration
        try EncryptionMigrator.migrate(at: dbPath, key: key, backupFolder: backupFolder)

        // 3. Verify pre-encrypt safety backup was created
        let backupFiles = try FileManager.default.contentsOfDirectory(atPath: backupFolder)
        let preEncryptBackups = backupFiles.filter { $0.hasPrefix("pre-encrypt-") && $0.hasSuffix(".timesink") }
        XCTAssertEqual(preEncryptBackups.count, 1, "Pre-migration safety backup must exist in backups folder")

        // 4. Verify file on disk is now encrypted
        XCTAssertFalse(EncryptionMigrator.isPlaintextDatabase(at: dbPath))

        // 5. Open migrated database with encryption key and verify integrity
        let encDb = try SQLiteDatabase(path: dbPath, key: key)
        XCTAssertEqual(encDb.userVersion, originalVersion, "Schema user_version must be strictly preserved across migration")

        let appCount = try encDb.scalarInt("SELECT COUNT(*) FROM apps;")
        let sessionCount = try encDb.scalarInt("SELECT COUNT(*) FROM sessions;")
        let settingVal = try encDb.scalarText("SELECT value FROM settings WHERE key = 'device.id';")
        encDb.close()

        XCTAssertEqual(appCount, 1)
        XCTAssertEqual(sessionCount, 1)
        XCTAssertEqual(settingVal, "dev-1")
    }

    func testAutomaticMigrationOnSQLiteDatabaseInit() throws {
        let dbPath = tempDir.appendingPathComponent("auto_migrate.db").path

        // Create plaintext database
        let plainDb = try SQLiteDatabase(path: dbPath, key: nil, allowPlaintext: true)
        try plainDb.exec("CREATE TABLE test (num INT); INSERT INTO test VALUES (99);")
        plainDb.close()

        XCTAssertTrue(EncryptionMigrator.isPlaintextDatabase(at: dbPath))

        // Calling standard SQLiteDatabase(path:) must detect plaintext, migrate, and open encrypted
        let db = try SQLiteDatabase(path: dbPath)
        let num = try db.scalarInt("SELECT num FROM test;")
        db.close()

        XCTAssertEqual(num, 99)
        XCTAssertFalse(EncryptionMigrator.isPlaintextDatabase(at: dbPath), "Database must now be encrypted")
    }

    func testInterruptionRecoveryRestoresBackup() throws {
        let dbPath = tempDir.appendingPathComponent("recover.db").path
        let backupPath = dbPath + ".plain.bak"

        // Simulate crash right after rename: dbPath is missing, backupPath exists
        let backupDb = try SQLiteDatabase(path: backupPath, key: nil, allowPlaintext: true)
        try backupDb.exec("CREATE TABLE recovered (data TEXT); INSERT INTO recovered VALUES ('saved');")
        backupDb.close()

        XCTAssertFalse(FileManager.default.fileExists(atPath: dbPath))
        XCTAssertTrue(FileManager.default.fileExists(atPath: backupPath))

        // Clean stale files
        EncryptionMigrator.cleanStaleMigrationFiles(at: dbPath)

        XCTAssertTrue(FileManager.default.fileExists(atPath: dbPath), "Main DB must be restored from backup file")
        XCTAssertFalse(FileManager.default.fileExists(atPath: backupPath))

        let restoredDb = try SQLiteDatabase(path: dbPath, key: nil, allowPlaintext: true)
        let data = try restoredDb.scalarText("SELECT data FROM recovered;")
        restoredDb.close()
        XCTAssertEqual(data, "saved")
    }
}

