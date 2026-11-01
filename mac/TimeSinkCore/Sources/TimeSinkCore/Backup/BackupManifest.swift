import Foundation

public struct BackupManifest: Codable, Sendable {
    public static let entryName = "manifest.json"
    public static let dbEntryName = "timesink.db"

    public let format: Int
    public let schemaVersion: Int
    public let appVersion: String
    public let createdUtc: String

    public init(format: Int = 1, schemaVersion: Int, appVersion: String = "1.0.0", createdUtc: String) {
        self.format = format
        self.schemaVersion = schemaVersion
        self.appVersion = appVersion
        self.createdUtc = createdUtc
    }

    public static func parse(jsonData: Data) throws -> BackupManifest {
        let decoder = JSONDecoder()
        return try decoder.decode(BackupManifest.self, from: jsonData)
    }

    public func toJsonData() throws -> Data {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        return try encoder.encode(self)
    }
}

public struct BackupInspection: Sendable {
    public let manifest: BackupManifest
    public let apps: Int
    public let sessions: Int
    public let needsMigration: Bool

    public init(manifest: BackupManifest, apps: Int, sessions: Int, needsMigration: Bool) {
        self.manifest = manifest
        self.apps = apps
        self.sessions = sessions
        self.needsMigration = needsMigration
    }
}

public struct ReplaceResult: Sendable {
    public let safetyCopy: String?

    public init(safetyCopy: String?) {
        self.safetyCopy = safetyCopy
    }
}

public struct MergeResult: Sendable, Equatable {
    public let appsAdded: Int
    public let sessionsAdded: Int
    public let sessionsSkipped: Int

    public init(appsAdded: Int, sessionsAdded: Int, sessionsSkipped: Int) {
        self.appsAdded = appsAdded
        self.sessionsAdded = sessionsAdded
        self.sessionsSkipped = sessionsSkipped
    }
}

public enum BackupError: Error, LocalizedError {
    case unreadableManifest
    case missingDatabase
    case unknownFormat
    case newerSchemaVersion(current: Int, supported: Int)
    case databaseCorrupt(String)
    case executionFailed(String)
    case generic(String)

    public var errorDescription: String? {
        switch self {
        case .unreadableManifest:
            return "This is not a TimeSink backup (the manifest is unreadable)."
        case .missingDatabase:
            return "This backup has no database inside."
        case .unknownFormat:
            return "This backup uses an unknown format."
        case .newerSchemaVersion(let current, let supported):
            return "This backup was made by a newer version of TimeSink (schema \(current), this version understands \(supported)). Update TimeSink to restore it."
        case .databaseCorrupt(let reason):
            return "The database inside this backup is damaged: \(reason)"
        case .executionFailed(let msg):
            return "Backup operation failed: \(msg)"
        case .generic(let msg):
            return msg
        }
    }
}

