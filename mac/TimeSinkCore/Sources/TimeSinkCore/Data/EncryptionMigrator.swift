import Foundation

/// Handles one-time migration from an unencrypted SQLite 3 database to a SQLCipher 4 encrypted database.
/// Provides pre-migration safety backup, WAL truncation, schema version preservation, integrity verification,
/// atomic file replacement, and crash-resilient cleanup.
public enum EncryptionMigrator {
    private static let sqliteMagicHeader: Data = "SQLite format 3\0".data(using: .utf8)!

    /// Checks whether the file at `path` exists and begins with the standard 16-byte unencrypted SQLite 3 magic header.
    public static func isPlaintextDatabase(at path: String) -> Bool {
        guard !path.isEmpty, path != ":memory:", FileManager.default.fileExists(atPath: path) else {
            return false
        }
        guard let fileHandle = FileHandle(forReadingAtPath: path) else {
            return false
        }
        defer { try? fileHandle.close() }
        let header = fileHandle.readData(ofLength: 16)
        guard header.count == 16 else { return false }
        return header == sqliteMagicHeader
    }

    /// Cleans up any stale temporary files from an interrupted migration or recovers the original database
    /// if an interruption occurred during file replacement.
    public static func cleanStaleMigrationFiles(at path: String) {
        guard !path.isEmpty, path != ":memory:" else { return }

        let tempDbPath = path + ".migrating"
        let backupPath = path + ".plain.bak"

        try? FileManager.default.removeItem(atPath: tempDbPath)
        try? FileManager.default.removeItem(atPath: tempDbPath + "-wal")
        try? FileManager.default.removeItem(atPath: tempDbPath + "-shm")

        // If the main database file is missing but the backup exists, restore it
        if !FileManager.default.fileExists(atPath: path) && FileManager.default.fileExists(atPath: backupPath) {
            try? FileManager.default.moveItem(atPath: backupPath, toPath: path)
        } else if FileManager.default.fileExists(atPath: path) && FileManager.default.fileExists(atPath: backupPath) {
            try? FileManager.default.removeItem(atPath: backupPath)
        }
    }

    /// Creates a safety backup before performing encryption migration, stored in `backups/`
    /// named `pre-encrypt-<timestamp>.timesink`.
    public static func createSafetyBackup(at path: String, backupFolder: String? = nil) throws -> String {
        let folder: String
        if let customFolder = backupFolder, !customFolder.isEmpty {
            folder = customFolder
        } else {
            folder = (path as NSString).deletingLastPathComponent + "/backups"
        }
        try FileManager.default.createDirectory(atPath: folder, withIntermediateDirectories: true)

        let formatter = DateFormatter()
        formatter.dateFormat = "yyyyMMdd-HHmmss"
        formatter.timeZone = TimeZone(secondsFromGMT: 0)
        let timestamp = formatter.string(from: Date())
        let backupFile = "\(folder)/pre-encrypt-\(timestamp)\(BackupService.fileExtension)"

        try BackupService.create(dbPath: path, destFile: backupFile, now: Date(), key: nil)
        return backupFile
    }

    /// Migrates an unencrypted SQLite database at `path` to SQLCipher encryption using the provided `key`.
    public static func migrate(at path: String, key: Data, backupFolder: String? = nil) throws {
        guard !path.isEmpty, path != ":memory:" else { return }

        cleanStaleMigrationFiles(at: path)

        guard isPlaintextDatabase(at: path) else {
            return
        }

        // 1. Create safety backup before touching anything
        _ = try createSafetyBackup(at: path, backupFolder: backupFolder)

        let tempDbPath = path + ".migrating"
        let backupPath = path + ".plain.bak"
        let hexKey = key.map { String(format: "%02x", $0) }.joined()
        var originalVersion: Int32 = 0

        // 2. Open plaintext database unencrypted, checkpoint WAL, and export to encrypted temp database
        let plainDb = try SQLiteDatabase(path: path, key: nil, allowPlaintext: true)
        do {
            try plainDb.exec("PRAGMA wal_checkpoint(TRUNCATE);")
            originalVersion = plainDb.userVersion

            try? FileManager.default.removeItem(atPath: tempDbPath)
            try? FileManager.default.removeItem(atPath: tempDbPath + "-wal")
            try? FileManager.default.removeItem(atPath: tempDbPath + "-shm")

            let escapedTemp = tempDbPath.replacingOccurrences(of: "'", with: "''")
            try plainDb.exec("ATTACH DATABASE '\(escapedTemp)' AS encrypted KEY \"x'\(hexKey)'\";")
            try plainDb.exec("PRAGMA encrypted.cipher_page_size = 4096;")
            try plainDb.exec("PRAGMA encrypted.kdf_iter = 256000;")
            try plainDb.exec("SELECT sqlcipher_export('encrypted');")
            try plainDb.exec("PRAGMA encrypted.user_version = \(originalVersion);")
            try plainDb.exec("DETACH DATABASE encrypted;")
            plainDb.close()
        } catch {
            plainDb.close()
            try? FileManager.default.removeItem(atPath: tempDbPath)
            try? FileManager.default.removeItem(atPath: tempDbPath + "-wal")
            try? FileManager.default.removeItem(atPath: tempDbPath + "-shm")
            throw SQLiteError.generic("sqlcipher_export migration failed: \(error.localizedDescription)")
        }

        // 3. Verify integrity and user_version of newly encrypted temp database
        let verifyDb = try SQLiteDatabase(path: tempDbPath, key: key)
        let integrity = try verifyDb.scalarText("PRAGMA integrity_check;") ?? ""
        guard integrity == "ok" else {
            verifyDb.close()
            try? FileManager.default.removeItem(atPath: tempDbPath)
            throw SQLiteError.generic("Encrypted database integrity check failed: \(integrity)")
        }
        guard verifyDb.userVersion == originalVersion else {
            let actualVersion = verifyDb.userVersion
            verifyDb.close()
            try? FileManager.default.removeItem(atPath: tempDbPath)
            throw SQLiteError.generic("User version mismatch after migration: expected \(originalVersion), got \(actualVersion)")
        }
        verifyDb.close()

        // 4. Remove plaintext WAL and SHM
        try? FileManager.default.removeItem(atPath: path + "-wal")
        try? FileManager.default.removeItem(atPath: path + "-shm")

        // 5. Atomic file replacement
        try? FileManager.default.removeItem(atPath: backupPath)
        try FileManager.default.moveItem(atPath: path, toPath: backupPath)
        try FileManager.default.moveItem(atPath: tempDbPath, toPath: path)
        try? FileManager.default.removeItem(atPath: backupPath)
    }

    /// Convenience migration check that obtains the key from `keyManager` if migration is needed.
    public static func migrateIfNeeded(
        dbPath: String,
        keyManager: KeyManagerProtocol = KeychainKeyManager.shared,
        backupFolder: String? = nil
    ) throws {
        guard !dbPath.isEmpty, dbPath != ":memory:" else { return }
        cleanStaleMigrationFiles(at: dbPath)
        guard isPlaintextDatabase(at: dbPath) else { return }
        let key = try keyManager.getOrCreateDatabaseKey()
        try migrate(at: dbPath, key: key, backupFolder: backupFolder)
    }
}

