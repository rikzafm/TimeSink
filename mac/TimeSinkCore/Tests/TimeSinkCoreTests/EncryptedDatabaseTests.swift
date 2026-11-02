import XCTest
import Foundation
@testable import TimeSinkCore

final class EncryptedDatabaseTests: XCTestCase {
    var tempDir: URL!
    var mockKeyManager: InMemoryKeyManager!
    var originalSharedKeyManager: KeyManagerProtocol!

    override func setUpWithError() throws {
        tempDir = URL(fileURLWithPath: NSTemporaryDirectory()).appendingPathComponent("timesink-enc-test-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: tempDir, withIntermediateDirectories: true)

        mockKeyManager = InMemoryKeyManager()
        originalSharedKeyManager = KeychainKeyManager.shared
        KeychainKeyManager.shared = mockKeyManager
    }

    override func tearDownWithError() throws {
        KeychainKeyManager.shared = originalSharedKeyManager
        try? FileManager.default.removeItem(at: tempDir)
    }

    func testCreateAndReopenEncryptedDatabase() throws {
        let dbPath = tempDir.appendingPathComponent("encrypted.db").path
        let key = Data((0..<32).map { UInt8($0) })

        let db = try SQLiteDatabase(path: dbPath, key: key)
        try db.exec("CREATE TABLE secure_data (id INTEGER PRIMARY KEY, secret TEXT);")
        try db.exec("INSERT INTO secure_data (id, secret) VALUES (1, 'classified');")
        db.close()

        // Reopen with the identical key
        let reopenedDb = try SQLiteDatabase(path: dbPath, key: key)
        let secret = try reopenedDb.scalarText("SELECT secret FROM secure_data WHERE id = 1;")
        reopenedDb.close()

        XCTAssertEqual(secret, "classified")
    }

    func testEncryptedDatabaseHeaderIsNotPlaintext() throws {
        let dbPath = tempDir.appendingPathComponent("secure_header.db").path
        let key = try mockKeyManager.getOrCreateDatabaseKey()

        let db = try SQLiteDatabase(path: dbPath, key: key)
        try db.exec("CREATE TABLE test (num INT); INSERT INTO test VALUES (100);")
        db.close()

        // Plaintext magic check must fail
        XCTAssertFalse(EncryptionMigrator.isPlaintextDatabase(at: dbPath))

        // Read raw first 16 bytes
        let fileHandle = try FileHandle(forReadingFrom: URL(fileURLWithPath: dbPath))
        let header = fileHandle.readData(ofLength: 16)
        try fileHandle.close()

        let magic = "SQLite format 3\0".data(using: .utf8)!
        XCTAssertNotEqual(header, magic, "SQLCipher page 1 header must not match standard SQLite 3 magic header")
    }

    func testOpenEncryptedDatabaseWithWrongKeyFails() throws {
        let dbPath = tempDir.appendingPathComponent("wrong_key.db").path
        let correctKey = Data(repeating: 0x41, count: 32)
        let wrongKey = Data(repeating: 0x42, count: 32)

        let db = try SQLiteDatabase(path: dbPath, key: correctKey)
        try db.exec("CREATE TABLE protected (val INT); INSERT INTO protected VALUES (42);")
        db.close()

        // Attempting to open with mismatched key must throw
        XCTAssertThrowsError(try SQLiteDatabase(path: dbPath, key: wrongKey)) { error in
            guard let sqliteError = error as? SQLiteError else {
                XCTFail("Expected SQLiteError, got \(error)")
                return
            }
            if case .openFailed(let msg) = sqliteError {
                XCTAssertTrue(msg.contains("decrypt") || msg.contains("database"), "Error message should indicate decryption failure: \(msg)")
            } else {
                XCTFail("Expected openFailed, got \(sqliteError)")
            }
        }
    }

    func testOpenEncryptedDatabaseWithoutKeyFails() throws {
        let dbPath = tempDir.appendingPathComponent("no_key.db").path
        let key = Data(repeating: 0x55, count: 32)

        let db = try SQLiteDatabase(path: dbPath, key: key)
        try db.exec("CREATE TABLE test (val TEXT); INSERT INTO test VALUES ('hidden');")
        db.close()

        // Attempting to open unencrypted must throw because file is encrypted
        XCTAssertThrowsError(
            try {
                let unencDb = try SQLiteDatabase(path: dbPath, key: nil, allowPlaintext: true)
                _ = try unencDb.scalarInt("SELECT COUNT(*) FROM test;")
            }()
        )
    }

    func testEncryptedDatabaseBackupInheritsKey() throws {
        let sourcePath = tempDir.appendingPathComponent("source.db").path
        let backupPath = tempDir.appendingPathComponent("backup.db").path
        let key = try mockKeyManager.getOrCreateDatabaseKey()

        let sourceDb = try SQLiteDatabase(path: sourcePath, key: key)
        try sourceDb.exec("CREATE TABLE records (id INTEGER PRIMARY KEY, note TEXT);")
        try sourceDb.exec("INSERT INTO records VALUES (1, 'backup test');")

        try sourceDb.backup(to: backupPath)
        sourceDb.close()

        // Destination file must be encrypted with source key
        XCTAssertFalse(EncryptionMigrator.isPlaintextDatabase(at: backupPath))

        let backupDb = try SQLiteDatabase(path: backupPath, key: key)
        let note = try backupDb.scalarText("SELECT note FROM records WHERE id = 1;")
        backupDb.close()

        XCTAssertEqual(note, "backup test")
    }
}

