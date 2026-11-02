import XCTest
import Foundation
@testable import TimeSinkCore

final class KeychainKeyManagerTests: XCTestCase {
    func testInMemoryKeyManagerGenerationAndIdempotency() throws {
        let manager = InMemoryKeyManager()
        let key1 = try manager.getOrCreateDatabaseKey()
        XCTAssertEqual(key1.count, 32, "Database key must be exactly 32 bytes (256-bit)")

        let key2 = try manager.getOrCreateDatabaseKey()
        XCTAssertEqual(key1, key2, "Subsequent calls to getOrCreateDatabaseKey must return the identical key")

        let retrieved = try manager.getDatabaseKey()
        XCTAssertEqual(retrieved, key1)
    }

    func testInMemoryKeyManagerDeletion() throws {
        let manager = InMemoryKeyManager()
        let key1 = try manager.getOrCreateDatabaseKey()
        XCTAssertEqual(key1.count, 32)

        try manager.deleteDatabaseKey()
        let afterDelete = try manager.getDatabaseKey()
        XCTAssertNil(afterDelete, "Key must be nil after deleteDatabaseKey()")

        let key2 = try manager.getOrCreateDatabaseKey()
        XCTAssertEqual(key2.count, 32)
        XCTAssertNotEqual(key1, key2, "A new key must be generated after deletion")
    }

    func testKeychainKeyManagerIdempotencyAndSize() throws {
        let uniqueService = "com.timesink.test.\(UUID().uuidString)"
        let manager = KeychainKeyManager(serviceName: uniqueService, accountName: "test.dbkey")
        defer {
            try? manager.deleteDatabaseKey()
        }

        let key1 = try manager.getOrCreateDatabaseKey()
        XCTAssertEqual(key1.count, 32, "Generated key must be 32 bytes")

        let key2 = try manager.getOrCreateDatabaseKey()
        XCTAssertEqual(key1, key2, "Keychain key must be idempotent across calls")
    }

    func testKeychainKeyManagerDeletion() throws {
        let uniqueService = "com.timesink.test.\(UUID().uuidString)"
        let manager = KeychainKeyManager(serviceName: uniqueService, accountName: "test.dbkey")
        defer {
            try? manager.deleteDatabaseKey()
        }

        let key1 = try manager.getOrCreateDatabaseKey()
        try manager.deleteDatabaseKey()

        let key2 = try manager.getOrCreateDatabaseKey()
        XCTAssertEqual(key2.count, 32)
        XCTAssertNotEqual(key1, key2, "Regenerated key must be distinct from deleted key")
    }

    func testSaveDatabaseKeyEnforces32Bytes() throws {
        let manager = KeychainKeyManager(serviceName: "com.timesink.test.invalid", accountName: "test")
        defer { try? manager.deleteDatabaseKey() }

        let shortKey = Data([0x01, 0x02, 0x03])
        XCTAssertThrowsError(try manager.saveDatabaseKey(shortKey)) { error in
            guard let keychainError = error as? KeychainError else {
                XCTFail("Expected KeychainError, got \(error)")
                return
            }
            if case .invalidKeySize(let size) = keychainError {
                XCTAssertEqual(size, 3)
            } else {
                XCTFail("Expected invalidKeySize, got \(keychainError)")
            }
        }
    }

    func testSharedKeyManagerInjection() throws {
        let originalShared = KeychainKeyManager.shared
        defer {
            KeychainKeyManager.shared = originalShared
        }

        let mockManager = InMemoryKeyManager()
        KeychainKeyManager.shared = mockManager

        let key = try KeychainKeyManager.shared.getOrCreateDatabaseKey()
        XCTAssertEqual(key.count, 32)
        XCTAssertEqual(try mockManager.getDatabaseKey(), key)
    }
}

