import Foundation

public protocol Migration: Sendable {
    var version: Int32 { get }
    func apply(to db: SQLiteDatabase) throws
}

public enum MigrationError: Error, LocalizedError {
    case databaseNewerThanShipped(current: Int32, latest: Int32)
    case migrationFailed(version: Int32, reason: String)

    public var errorDescription: String? {
        switch self {
        case .databaseNewerThanShipped(let current, let latest):
            return "Database schema version (\(current)) is newer than this version of TimeSink understands (\(latest)). Please update the app."
        case .migrationFailed(let version, let reason):
            return "Migration M\(String(format: "%03d", version)) failed: \(reason)"
        }
    }
}

public final class Migrator {
    public static let shipped: [Migration] = [
        M001_Initial(),
        M002_BackgroundApps(),
        M003_AppKind(),
        M004_Devices(),
        M005_AppUpdatedUtc()
    ]

    public static var latestVersion: Int32 {
        shipped.map(\.version).max() ?? 0
    }

    public static func apply(to db: SQLiteDatabase, migrations: [Migration]? = nil) throws {
        let list = migrations ?? shipped
        let latest = list.map(\.version).max() ?? 0
        let current = db.userVersion

        if current > latest {
            throw MigrationError.databaseNewerThanShipped(current: current, latest: latest)
        }

        for m in list.sorted(by: { $0.version < $1.version }) {
            if db.userVersion < m.version {
                try db.transaction {
                    try m.apply(to: db)
                    db.userVersion = m.version
                }
            }
        }
    }
}
