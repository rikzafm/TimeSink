import Foundation

public struct M002_BackgroundApps: Migration {
    public let version: Int32 = 2

    public init() {}

    public func apply(to db: SQLiteDatabase) throws {
        try db.exec("ALTER TABLE apps ADD COLUMN is_background INTEGER NOT NULL DEFAULT 0;")
    }
}
