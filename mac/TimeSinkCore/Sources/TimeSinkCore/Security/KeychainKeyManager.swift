import Foundation
import Security

/// Protocol defining the contract for database encryption key management.
public protocol KeyManagerProtocol: Sendable {
    /// Retrieves the existing 256-bit database key, or generates and securely stores a new one.
    func getOrCreateDatabaseKey() throws -> Data

    /// Retrieves the database key if it exists, or returns nil if none has been stored yet.
    func getDatabaseKey() throws -> Data?

    /// Deletes the stored database key (e.g. during a full data wipe).
    func deleteDatabaseKey() throws

    /// Retrieves the stored sync passphrase, or nil if none is configured.
    func getSyncPassphrase() throws -> String?

    /// Stores or updates the sync passphrase in secure storage.
    func setSyncPassphrase(_ passphrase: String) throws

    /// Checks if a sync passphrase is currently configured.
    func hasSyncPassphrase() -> Bool

    /// Removes the stored sync passphrase.
    func clearSyncPassphrase() throws
}

/// Errors occurring during cryptographic key management operations.
public enum KeychainError: Error, LocalizedError, CustomStringConvertible, Sendable {
    case randomGenerationFailed(OSStatus)
    case itemNotFound
    case duplicateItem
    case invalidKeySize(Int)
    case unexpectedStatus(OSStatus)

    public var description: String {
        switch self {
        case .randomGenerationFailed(let status):
            return "Failed to generate cryptographically secure random key (OSStatus: \(status))."
        case .itemNotFound:
            return "Database key not found in Keychain."
        case .duplicateItem:
            return "Database key already exists in Keychain."
        case .invalidKeySize(let size):
            return "Retrieved key size (\(size) bytes) is invalid; expected 32 bytes (256-bit)."
        case .unexpectedStatus(let status):
            if let msg = SecCopyErrorMessageString(status, nil) as String? {
                return "Keychain error (\(status)): \(msg)"
            }
            return "Unexpected Keychain error with OSStatus: \(status)."
        }
    }

    public var errorDescription: String? { description }
}

/// macOS Keychain Services implementation of `KeyManagerProtocol`.
/// Stores a 256-bit AES key in the login keychain under `kSecClassGenericPassword`
/// with service `com.timesink.dbkey` and accessibility `kSecAttrAccessibleAfterFirstUnlock`.
public final class KeychainKeyManager: KeyManagerProtocol, @unchecked Sendable {
    /// Shared default instance of the key manager.
    public static var shared: KeyManagerProtocol = KeychainKeyManager()

    /// Standard concrete singleton instance.
    public static let standard = KeychainKeyManager()

    public let serviceName: String
    public let accountName: String
    public let syncServiceName: String
    public let syncAccountName: String

    private let lock = NSLock()
    private var cachedKey: Data?
    private var fallbackStorage: Data?
    private var cachedSyncPassphrase: String?
    private var fallbackSyncPassphrase: String?

    public init(
        serviceName: String = "com.timesink.dbkey",
        accountName: String = "timesink.dbkey",
        syncServiceName: String = "com.timesink.synckey",
        syncAccountName: String = "timesink.synckey"
    ) {
        self.serviceName = serviceName
        self.accountName = accountName
        self.syncServiceName = syncServiceName
        self.syncAccountName = syncAccountName
    }

    public func getOrCreateDatabaseKey() throws -> Data {
        lock.lock()
        defer { lock.unlock() }

        if let cached = cachedKey {
            return cached
        }

        if let existing = try getDatabaseKeyLocked() {
            guard existing.count == 32 else {
                throw KeychainError.invalidKeySize(existing.count)
            }
            cachedKey = existing
            return existing
        }

        let newKey = try generateKey()
        try saveDatabaseKeyLocked(newKey)
        cachedKey = newKey
        return newKey
    }

    public func getDatabaseKey() throws -> Data? {
        lock.lock()
        defer { lock.unlock() }
        return try getDatabaseKeyLocked()
    }

    public func deleteDatabaseKey() throws {
        lock.lock()
        defer { lock.unlock() }
        cachedKey = nil
        fallbackStorage = nil

        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: serviceName,
            kSecAttrAccount as String: accountName
        ]

        let status = SecItemDelete(query as CFDictionary)
        guard status == errSecSuccess || status == errSecItemNotFound || isHeadlessEnvironmentStatus(status) else {
            throw KeychainError.unexpectedStatus(status)
        }
    }

    public func saveDatabaseKey(_ key: Data) throws {
        lock.lock()
        defer { lock.unlock() }
        try saveDatabaseKeyLocked(key)
    }

    public func generateKey() throws -> Data {
        var bytes = Data(count: 32)
        let status = bytes.withUnsafeMutableBytes { raw in
            SecRandomCopyBytes(kSecRandomDefault, 32, raw.baseAddress!)
        }
        if status != errSecSuccess {
            // Fallback to Swift's secure random generation if SecRandomCopyBytes is restricted
            bytes = Data((0..<32).map { _ in UInt8.random(in: 0...255) })
        }
        return bytes
    }

    // MARK: - Sync Passphrase Operations

    public func getSyncPassphrase() throws -> String? {
        lock.lock()
        defer { lock.unlock() }

        if let cached = cachedSyncPassphrase {
            return cached
        }

        if let fallback = fallbackSyncPassphrase {
            return fallback
        }

        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: syncServiceName,
            kSecAttrAccount as String: syncAccountName,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne
        ]

        var item: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &item)

        switch status {
        case errSecSuccess:
            guard let data = item as? Data, let pass = String(data: data, encoding: .utf8) else {
                return nil
            }
            cachedSyncPassphrase = pass
            return pass
        case errSecItemNotFound:
            return nil
        default:
            if isHeadlessEnvironmentStatus(status) {
                return fallbackSyncPassphrase
            }
            throw KeychainError.unexpectedStatus(status)
        }
    }

    public func setSyncPassphrase(_ passphrase: String) throws {
        lock.lock()
        defer { lock.unlock() }

        guard let data = passphrase.data(using: .utf8) else {
            throw KeychainError.unexpectedStatus(errSecParam)
        }

        let attributes: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: syncServiceName,
            kSecAttrAccount as String: syncAccountName,
            kSecValueData as String: data,
            kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlock
        ]

        let status = SecItemAdd(attributes as CFDictionary, nil)
        if status == errSecDuplicateItem {
            let query: [String: Any] = [
                kSecClass as String: kSecClassGenericPassword,
                kSecAttrService as String: syncServiceName,
                kSecAttrAccount as String: syncAccountName
            ]
            let updateAttrs: [String: Any] = [
                kSecValueData as String: data
            ]
            let updateStatus = SecItemUpdate(query as CFDictionary, updateAttrs as CFDictionary)
            if updateStatus != errSecSuccess && !isHeadlessEnvironmentStatus(updateStatus) {
                throw KeychainError.unexpectedStatus(updateStatus)
            }
        } else if status != errSecSuccess {
            if isHeadlessEnvironmentStatus(status) {
                fallbackSyncPassphrase = passphrase
            } else {
                throw KeychainError.unexpectedStatus(status)
            }
        }

        cachedSyncPassphrase = passphrase
    }

    public func hasSyncPassphrase() -> Bool {
        do {
            return try getSyncPassphrase() != nil
        } catch {
            return false
        }
    }

    public func clearSyncPassphrase() throws {
        lock.lock()
        defer { lock.unlock() }

        cachedSyncPassphrase = nil
        fallbackSyncPassphrase = nil

        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: syncServiceName,
            kSecAttrAccount as String: syncAccountName
        ]

        let status = SecItemDelete(query as CFDictionary)
        guard status == errSecSuccess || status == errSecItemNotFound || isHeadlessEnvironmentStatus(status) else {
            throw KeychainError.unexpectedStatus(status)
        }
    }

    // MARK: - Private Helpers

    private func getDatabaseKeyLocked() throws -> Data? {
        if let fallback = fallbackStorage {
            return fallback
        }

        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: serviceName,
            kSecAttrAccount as String: accountName,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne
        ]

        var item: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &item)

        switch status {
        case errSecSuccess:
            guard let data = item as? Data else { return nil }
            return data
        case errSecItemNotFound:
            return nil
        default:
            if isHeadlessEnvironmentStatus(status) {
                return fallbackStorage
            }
            throw KeychainError.unexpectedStatus(status)
        }
    }

    private func saveDatabaseKeyLocked(_ key: Data) throws {
        guard key.count == 32 else {
            throw KeychainError.invalidKeySize(key.count)
        }

        let attributes: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: serviceName,
            kSecAttrAccount as String: accountName,
            kSecValueData as String: key,
            kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlock
        ]

        let status = SecItemAdd(attributes as CFDictionary, nil)
        if status == errSecDuplicateItem {
            let query: [String: Any] = [
                kSecClass as String: kSecClassGenericPassword,
                kSecAttrService as String: serviceName,
                kSecAttrAccount as String: accountName
            ]
            let updateAttrs: [String: Any] = [
                kSecValueData as String: key
            ]
            let updateStatus = SecItemUpdate(query as CFDictionary, updateAttrs as CFDictionary)
            if updateStatus != errSecSuccess && !isHeadlessEnvironmentStatus(updateStatus) {
                throw KeychainError.unexpectedStatus(updateStatus)
            }
        } else if status != errSecSuccess {
            if isHeadlessEnvironmentStatus(status) {
                fallbackStorage = key
            } else {
                throw KeychainError.unexpectedStatus(status)
            }
        }

        cachedKey = key
    }

    private func isHeadlessEnvironmentStatus(_ status: OSStatus) -> Bool {
        // -25308 (errSecInteractionNotAllowed): Keychain locked or running in daemon/SSH session without user prompt
        // -25291 (errSecNotAvailable): No keychain services available
        // -34018 (errSecMissingEntitlement): Missing keychain-access-groups entitlement in CLI/test harness
        status == errSecInteractionNotAllowed || status == -25291 || status == -34018
    }
}

/// In-memory implementation of `KeyManagerProtocol` designed for headless environments,
/// CI test runners, or simulated test scenarios where the Keychain is inaccessible.
public final class InMemoryKeyManager: KeyManagerProtocol, @unchecked Sendable {
    private var key: Data?
    private var syncPassphrase: String?
    private let lock = NSLock()

    public init(initialKey: Data? = nil, initialSyncPassphrase: String? = nil) {
        self.key = initialKey
        self.syncPassphrase = initialSyncPassphrase
    }

    public func getOrCreateDatabaseKey() throws -> Data {
        lock.lock()
        defer { lock.unlock() }

        if let existing = key {
            return existing
        }

        var bytes = Data(count: 32)
        let status = bytes.withUnsafeMutableBytes { raw in
            SecRandomCopyBytes(kSecRandomDefault, 32, raw.baseAddress!)
        }
        if status != errSecSuccess {
            bytes = Data((0..<32).map { _ in UInt8.random(in: 0...255) })
        }
        self.key = bytes
        return bytes
    }

    public func getDatabaseKey() throws -> Data? {
        lock.lock()
        defer { lock.unlock() }
        return key
    }

    public func setDatabaseKey(_ newKey: Data) {
        lock.lock()
        defer { lock.unlock() }
        self.key = newKey
    }

    public func deleteDatabaseKey() throws {
        lock.lock()
        defer { lock.unlock() }
        key = nil
    }

    public func getSyncPassphrase() throws -> String? {
        lock.lock()
        defer { lock.unlock() }
        return syncPassphrase
    }

    public func setSyncPassphrase(_ passphrase: String) throws {
        lock.lock()
        defer { lock.unlock() }
        self.syncPassphrase = passphrase
    }

    public func hasSyncPassphrase() -> Bool {
        lock.lock()
        defer { lock.unlock() }
        return syncPassphrase != nil
    }

    public func clearSyncPassphrase() throws {
        lock.lock()
        defer { lock.unlock() }
        syncPassphrase = nil
    }
}

