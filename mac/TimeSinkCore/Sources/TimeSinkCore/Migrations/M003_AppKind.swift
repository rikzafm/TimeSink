import Foundation

public struct M003_AppKind: Migration {
    public let version: Int32 = 3

    public init() {}

    public func apply(to db: SQLiteDatabase) throws {
        try db.exec("ALTER TABLE apps ADD COLUMN kind INTEGER NULL;")
    }
}
