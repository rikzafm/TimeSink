import Foundation
import SQLite3

public enum SQLiteError: Error, LocalizedError, CustomStringConvertible {
    case openFailed(String)
    case prepareFailed(String)
    case stepFailed(String)
    case bindFailed(String)
    case executionFailed(String)
    case backupFailed(String)
    case generic(String)

    public var description: String {
        switch self {
        case .openFailed(let msg): return "SQLite open failed: \(msg)"
        case .prepareFailed(let msg): return "SQLite prepare failed: \(msg)"
        case .stepFailed(let msg): return "SQLite step failed: \(msg)"
        case .bindFailed(let msg): return "SQLite bind failed: \(msg)"
        case .executionFailed(let msg): return "SQLite execution failed: \(msg)"
        case .backupFailed(let msg): return "SQLite backup failed: \(msg)"
        case .generic(let msg): return "SQLite error: \(msg)"
        }
    }

    public var errorDescription: String? { description }
}

public final class SQLiteDatabase: @unchecked Sendable {
    public let path: String
    private var db: OpaquePointer?

    public init(path: String) throws {
        self.path = path
        var handle: OpaquePointer?
        let flags = SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE | SQLITE_OPEN_FULLMUTEX
        let result = sqlite3_open_v2(path, &handle, flags, nil)
        guard result == SQLITE_OK, let validHandle = handle else {
            let msg = handle != nil ? String(cString: sqlite3_errmsg(handle)) : "Unknown error code \(result)"
            if let handle = handle { sqlite3_close(handle) }
            throw SQLiteError.openFailed(msg)
        }
        self.db = validHandle

        // Configure default pragmas: WAL mode, 5s busy timeout, foreign keys enabled
        try exec("PRAGMA journal_mode = WAL;")
        try exec("PRAGMA busy_timeout = 5000;")
        try exec("PRAGMA foreign_keys = ON;")
    }

    public static func inMemory() throws -> SQLiteDatabase {
        try SQLiteDatabase(path: ":memory:")
    }

    deinit {
        close()
    }

    public func close() {
        if let handle = db {
            sqlite3_close_v2(handle)
            db = nil
        }
    }

    public var rawHandle: OpaquePointer? {
        db
    }

    public func exec(_ sql: String) throws {
        guard let handle = db else { throw SQLiteError.generic("Database connection is closed") }
        var errorMsg: UnsafeMutablePointer<CChar>?
        if sqlite3_exec(handle, sql, nil, nil, &errorMsg) != SQLITE_OK {
            let msg = errorMsg != nil ? String(cString: errorMsg!) : String(cString: sqlite3_errmsg(handle))
            sqlite3_free(errorMsg)
            throw SQLiteError.executionFailed(msg)
        }
    }

    public func prepare(_ sql: String) throws -> SQLiteStatement {
        guard let handle = db else { throw SQLiteError.generic("Database connection is closed") }
        var statementHandle: OpaquePointer?
        if sqlite3_prepare_v2(handle, sql, -1, &statementHandle, nil) != SQLITE_OK {
            let msg = String(cString: sqlite3_errmsg(handle))
            throw SQLiteError.prepareFailed(msg)
        }
        guard let validStmt = statementHandle else {
            throw SQLiteError.prepareFailed("Failed to create statement handle")
        }
        return SQLiteStatement(stmt: validStmt, db: self)
    }

    public func scalarInt(_ sql: String) throws -> Int64? {
        let stmt = try prepare(sql)
        if try stmt.step() {
            if stmt.columnIsNull(at: 0) { return nil }
            return stmt.columnInt64(at: 0)
        }
        return nil
    }

    public func scalarText(_ sql: String) throws -> String? {
        let stmt = try prepare(sql)
        if try stmt.step() {
            return stmt.columnText(at: 0)
        }
        return nil
    }

    public var userVersion: Int32 {
        get {
            do {
                let v = try scalarInt("PRAGMA user_version;") ?? 0
                return Int32(v)
            } catch {
                return 0
            }
        }
        set {
            try? exec("PRAGMA user_version = \(newValue);")
        }
    }

    public var lastInsertRowid: Int64 {
        guard let handle = db else { return 0 }
        return sqlite3_last_insert_rowid(handle)
    }

    public func transaction<T>(_ block: () throws -> T) throws -> T {
        try exec("BEGIN TRANSACTION;")
        do {
            let result = try block()
            try exec("COMMIT;")
            return result
        } catch {
            try? exec("ROLLBACK;")
            throw error
        }
    }

    /// Online database backup using SQLite Online Backup API (equivalent to VACUUM INTO)
    public func backup(to destinationPath: String) throws {
        guard let sourceHandle = db else { throw SQLiteError.generic("Source database is closed") }

        // Remove destination file if exists
        try? FileManager.default.removeItem(atPath: destinationPath)
        let destDir = (destinationPath as NSString).deletingLastPathComponent
        try? FileManager.default.createDirectory(atPath: destDir, withIntermediateDirectories: true)

        var destHandle: OpaquePointer?
        let flags = SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE
        if sqlite3_open_v2(destinationPath, &destHandle, flags, nil) != SQLITE_OK {
            let msg = destHandle != nil ? String(cString: sqlite3_errmsg(destHandle)) : "Cannot open dest"
            if let destHandle = destHandle { sqlite3_close(destHandle) }
            throw SQLiteError.backupFailed(msg)
        }
        defer { if let destHandle = destHandle { sqlite3_close(destHandle) } }

        guard let backup = sqlite3_backup_init(destHandle, "main", sourceHandle, "main") else {
            let msg = String(cString: sqlite3_errmsg(destHandle))
            throw SQLiteError.backupFailed("sqlite3_backup_init failed: \(msg)")
        }

        let rc = sqlite3_backup_step(backup, -1)
        sqlite3_backup_finish(backup)

        if rc != SQLITE_DONE {
            let msg = String(cString: sqlite3_errmsg(destHandle))
            throw SQLiteError.backupFailed("sqlite3_backup_step failed: \(msg)")
        }
    }
}

public final class SQLiteStatement {
    private var stmt: OpaquePointer?
    private weak var db: SQLiteDatabase?

    fileprivate init(stmt: OpaquePointer, db: SQLiteDatabase) {
        self.stmt = stmt
        self.db = db
    }

    deinit {
        if let stmt = stmt {
            sqlite3_finalize(stmt)
        }
    }

    public func bind(text: String, at index: Int32) throws {
        guard let stmt = stmt else { throw SQLiteError.generic("Statement finalized") }
        let SQLITE_TRANSIENT = unsafeBitCast(-1, to: sqlite3_destructor_type.self)
        if sqlite3_bind_text(stmt, index, text, -1, SQLITE_TRANSIENT) != SQLITE_OK {
            throw SQLiteError.bindFailed("Failed to bind text at index \(index)")
        }
    }

    public func bind(int64: Int64, at index: Int32) throws {
        guard let stmt = stmt else { throw SQLiteError.generic("Statement finalized") }
        if sqlite3_bind_int64(stmt, index, int64) != SQLITE_OK {
            throw SQLiteError.bindFailed("Failed to bind int64 at index \(index)")
        }
    }

    public func bind(int32: Int32, at index: Int32) throws {
        guard let stmt = stmt else { throw SQLiteError.generic("Statement finalized") }
        if sqlite3_bind_int(stmt, index, int32) != SQLITE_OK {
            throw SQLiteError.bindFailed("Failed to bind int32 at index \(index)")
        }
    }

    public func bind(double: Double, at index: Int32) throws {
        guard let stmt = stmt else { throw SQLiteError.generic("Statement finalized") }
        if sqlite3_bind_double(stmt, index, double) != SQLITE_OK {
            throw SQLiteError.bindFailed("Failed to bind double at index \(index)")
        }
    }

    public func bind(blob: Data, at index: Int32) throws {
        guard let stmt = stmt else { throw SQLiteError.generic("Statement finalized") }
        let SQLITE_TRANSIENT = unsafeBitCast(-1, to: sqlite3_destructor_type.self)
        let count = Int32(blob.count)
        let result = blob.withUnsafeBytes { rawBuffer -> Int32 in
            sqlite3_bind_blob(stmt, index, rawBuffer.baseAddress, count, SQLITE_TRANSIENT)
        }
        if result != SQLITE_OK {
            throw SQLiteError.bindFailed("Failed to bind blob at index \(index)")
        }
    }

    public func bindNull(at index: Int32) throws {
        guard let stmt = stmt else { throw SQLiteError.generic("Statement finalized") }
        if sqlite3_bind_null(stmt, index) != SQLITE_OK {
            throw SQLiteError.bindFailed("Failed to bind null at index \(index)")
        }
    }

    public func step() throws -> Bool {
        guard let stmt = stmt else { throw SQLiteError.generic("Statement finalized") }
        let rc = sqlite3_step(stmt)
        if rc == SQLITE_ROW {
            return true
        } else if rc == SQLITE_DONE {
            return false
        } else {
            let msg = db?.rawHandle != nil ? String(cString: sqlite3_errmsg(db!.rawHandle!)) : "Error code \(rc)"
            throw SQLiteError.stepFailed(msg)
        }
    }

    public func columnInt64(at index: Int32) -> Int64 {
        guard let stmt = stmt else { return 0 }
        return sqlite3_column_int64(stmt, index)
    }

    public func columnInt32(at index: Int32) -> Int32 {
        guard let stmt = stmt else { return 0 }
        return sqlite3_column_int(stmt, index)
    }

    public func columnText(at index: Int32) -> String? {
        guard let stmt = stmt else { return nil }
        guard let cStr = sqlite3_column_text(stmt, index) else { return nil }
        return String(cString: cStr)
    }

    public func columnDouble(at index: Int32) -> Double {
        guard let stmt = stmt else { return 0.0 }
        return sqlite3_column_double(stmt, index)
    }

    public func columnBlob(at index: Int32) -> Data? {
        guard let stmt = stmt else { return nil }
        guard let bytes = sqlite3_column_blob(stmt, index) else { return nil }
        let count = Int(sqlite3_column_bytes(stmt, index))
        return Data(bytes: bytes, count: count)
    }

    public func columnIsNull(at index: Int32) -> Bool {
        guard let stmt = stmt else { return true }
        return sqlite3_column_type(stmt, index) == SQLITE_NULL
    }

    public func reset() {
        if let stmt = stmt {
            sqlite3_reset(stmt)
            sqlite3_clear_bindings(stmt)
        }
    }
}
