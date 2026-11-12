import XCTest
import Foundation
@testable import TimeSinkCore

final class SQLiteDatabaseTests: XCTestCase {
    func testOpenInMemoryAndPragmas() throws {
        let db = try SQLiteDatabase.inMemory()
        let wal = try db.scalarText("PRAGMA journal_mode;")
        // In-memory databases report "memory", which is normal
        XCTAssertNotNil(wal)

        let timeout = try db.scalarInt("PRAGMA busy_timeout;")
        XCTAssertEqual(timeout, 5000)

        let fk = try db.scalarInt("PRAGMA foreign_keys;")
        XCTAssertEqual(fk, 1)
    }

    func testTransactionRollbackOnError() throws {
        let db = try SQLiteDatabase.inMemory()
        try db.exec("CREATE TABLE test (val TEXT);")

        XCTAssertThrowsError(
            try db.transaction {
                try db.exec("INSERT INTO test VALUES ('one');")
                throw SQLiteError.generic("Deliberate failure")
            }
        )

        let count = try db.scalarInt("SELECT COUNT(*) FROM test;")
        XCTAssertEqual(count, 0)
    }

    func testTransactionCommitOnSuccess() throws {
        let db = try SQLiteDatabase.inMemory()
        try db.exec("CREATE TABLE test (val TEXT);")

        try db.transaction {
            try db.exec("INSERT INTO test VALUES ('one');")
            try db.exec("INSERT INTO test VALUES ('two');")
        }

        let count = try db.scalarInt("SELECT COUNT(*) FROM test;")
        XCTAssertEqual(count, 2)
    }

    func testPreparedStatementsAndBindings() throws {
        let db = try SQLiteDatabase.inMemory()
        try db.exec("CREATE TABLE items (id INTEGER PRIMARY KEY, name TEXT, weight REAL, data BLOB);")

        let insert = try db.prepare("INSERT INTO items (name, weight, data) VALUES (?1, ?2, ?3);")
        try insert.bind(text: "Apple", at: 1)
        try insert.bind(double: 0.25, at: 2)
        try insert.bind(blob: Data([0xDE, 0xAD, 0xBE, 0xEF]), at: 3)
        let hasRow = try insert.step()
        XCTAssertFalse(hasRow)

        let query = try db.prepare("SELECT name, weight, data FROM items WHERE id = 1;")
        XCTAssertTrue(try query.step())
        XCTAssertEqual(query.columnText(at: 0), "Apple")
        XCTAssertEqual(query.columnDouble(at: 1), 0.25, accuracy: 0.001)
        XCTAssertEqual(query.columnBlob(at: 2), Data([0xDE, 0xAD, 0xBE, 0xEF]))
    }

    func testOnlineBackup() throws {
        let tmpDir = NSTemporaryDirectory() + "/timesink_test_\(UUID().uuidString)"
        try FileManager.default.createDirectory(atPath: tmpDir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(atPath: tmpDir) }

        let sourceFile = "\(tmpDir)/source.db"
        let backupFile = "\(tmpDir)/backup.db"

        let db = try SQLiteDatabase(path: sourceFile)
        try db.exec("CREATE TABLE test (num INT); INSERT INTO test VALUES (42);")

        try db.backup(to: backupFile)
        XCTAssertTrue(FileManager.default.fileExists(atPath: backupFile))

        let backupDb = try SQLiteDatabase(path: backupFile)
        let num = try backupDb.scalarInt("SELECT num FROM test;")
        XCTAssertEqual(num, 42)
    }

    func testErrorHandling() throws {
        let db = try SQLiteDatabase.inMemory()
        XCTAssertThrowsError(try db.exec("SELECT * FROM non_existent_table;"))
    }

    func testTransactionTakesWriteLockImmediately() throws {
        let path = (NSTemporaryDirectory() as NSString).appendingPathComponent("txn-lock-\(UUID().uuidString).db")
        defer {
            for suffix in ["", "-wal", "-shm"] { try? FileManager.default.removeItem(atPath: path + suffix) }
        }
        let key = Data(repeating: 0x11, count: 32)
        let a = try SQLiteDatabase(path: path, key: key)
        let b = try SQLiteDatabase(path: path, key: key)
        try a.exec("CREATE TABLE t (v INTEGER);")
        try b.exec("PRAGMA busy_timeout = 0;")

        // Inside a.transaction nothing has been written yet; BEGIN IMMEDIATE must still hold the write lock.
        try a.transaction {
            XCTAssertThrowsError(try b.exec("BEGIN IMMEDIATE;"))
        }
        // Lock released after commit.
        try b.exec("BEGIN IMMEDIATE;")
        try b.exec("ROLLBACK;")
    }

    func testDetachIfAttachedRemovesLeftoverAliasOnly() throws {
        let db = try SQLiteDatabase.inMemory()
        try db.detachIfAttached("snap") // not attached: no-op, no throw
        try db.exec("ATTACH DATABASE ':memory:' AS snap;")
        try db.detachIfAttached("snap")
        XCTAssertNoThrow(try db.exec("ATTACH DATABASE ':memory:' AS snap;"))
    }
}
