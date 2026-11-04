import Foundation

/// Adds `apps.updated_utc` for last-write-wins classification sync. Never edit once shipped.
public struct M005_AppUpdatedUtc: Migration {
    public let version: Int32 = 5

    public init() {}

    public func apply(to db: SQLiteDatabase) throws {
        let has = (try db.scalarInt("SELECT COUNT(*) FROM pragma_table_info('apps') WHERE name = 'updated_utc';") ?? 0) > 0
        if !has {
            try db.exec("ALTER TABLE apps ADD COLUMN updated_utc INTEGER NOT NULL DEFAULT 0;")
        }
    }
}

