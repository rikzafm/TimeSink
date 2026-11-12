import Foundation

public struct SyncManifest: Codable, Sendable, Equatable {
    public static let entryName = "manifest.json"
    public static let dbEntryName = "snapshot.db"

    public let format: Int
    public let schemaVersion: Int32
    public let appVersion: String
    public let deviceId: String
    public let deviceName: String
    public let platform: String
    public let createdUtc: String
    public let sessionCount: Int
    public let hash: String
    public let encrypted: Bool

    private enum CodingKeys: String, CodingKey {
        case format
        case schemaVersion
        case appVersion
        case deviceId
        case deviceName
        case platform
        case createdUtc
        case sessionCount
        case hash
        case encrypted
    }

    public init(
        format: Int = 1,
        schemaVersion: Int32 = 5,
        appVersion: String = "1.0.0",
        deviceId: String,
        deviceName: String,
        platform: String = "macos",
        createdUtc: String,
        sessionCount: Int,
        hash: String,
        encrypted: Bool = false
    ) {
        self.format = format
        self.schemaVersion = schemaVersion
        self.appVersion = appVersion
        self.deviceId = deviceId
        self.deviceName = deviceName
        self.platform = platform
        self.createdUtc = createdUtc
        self.sessionCount = sessionCount
        self.hash = hash
        self.encrypted = encrypted
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        self.format = try container.decode(Int.self, forKey: .format)
        self.schemaVersion = try container.decode(Int32.self, forKey: .schemaVersion)
        self.appVersion = try container.decode(String.self, forKey: .appVersion)
        self.deviceId = try container.decode(String.self, forKey: .deviceId)
        self.deviceName = try container.decode(String.self, forKey: .deviceName)
        self.platform = try container.decode(String.self, forKey: .platform)
        self.createdUtc = try container.decode(String.self, forKey: .createdUtc)
        self.sessionCount = try container.decode(Int.self, forKey: .sessionCount)
        self.hash = try container.decode(String.self, forKey: .hash)
        self.encrypted = try container.decodeIfPresent(Bool.self, forKey: .encrypted) ?? false
    }

    public func encode(to encoder: Encoder) throws {
        var container = encoder.container(keyedBy: CodingKeys.self)
        try container.encode(format, forKey: .format)
        try container.encode(schemaVersion, forKey: .schemaVersion)
        try container.encode(appVersion, forKey: .appVersion)
        try container.encode(deviceId, forKey: .deviceId)
        try container.encode(deviceName, forKey: .deviceName)
        try container.encode(platform, forKey: .platform)
        try container.encode(createdUtc, forKey: .createdUtc)
        try container.encode(sessionCount, forKey: .sessionCount)
        try container.encode(hash, forKey: .hash)
        try container.encode(encrypted, forKey: .encrypted)
    }
}

public enum SyncStatus: String, Sendable, Equatable {
    case success
    case badHash
    case newerFormat
    case conflictCopy
    case pending
    case selfFile
    case deviceForgotten
    case thirdDeviceIgnored
    case failed
    case encryptionKeyMissing = "encryption_key_missing"
    case encryptionKeyWrong = "encryption_key_wrong"
    case remoteNotEncrypted = "remote_not_encrypted"
    case unchanged
}

public struct SyncImportResult: Sendable, Equatable {
    public let status: SyncStatus
    public let message: String?
    public let appsAdded: Int
    public let sessionsAdded: Int
    public let sessionsUpdated: Int
    public let sessionsSkipped: Int

    public init(
        status: SyncStatus,
        message: String? = nil,
        appsAdded: Int = 0,
        sessionsAdded: Int = 0,
        sessionsUpdated: Int = 0,
        sessionsSkipped: Int = 0
    ) {
        self.status = status
        self.message = message
        self.appsAdded = appsAdded
        self.sessionsAdded = sessionsAdded
        self.sessionsUpdated = sessionsUpdated
        self.sessionsSkipped = sessionsSkipped
    }
}

