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

    public init(
        format: Int = 1,
        schemaVersion: Int32 = 4,
        appVersion: String = "1.0.0",
        deviceId: String,
        deviceName: String,
        platform: String = "macos",
        createdUtc: String,
        sessionCount: Int,
        hash: String
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

