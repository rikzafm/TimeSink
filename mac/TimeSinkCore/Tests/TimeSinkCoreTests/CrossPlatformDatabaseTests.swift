import XCTest
import Foundation
@testable import TimeSinkCore

final class CrossPlatformDatabaseTests: XCTestCase {
    func testSchemaCompatibilityWithCSharpSchema() throws {
        let db = try SQLiteDatabase.inMemory()
        try Migrator.apply(to: db)

        XCTAssertEqual(db.userVersion, 5)

        // Verify index names match M004 in C#
        let indexesStmt = try db.prepare("SELECT name FROM sqlite_master WHERE type = 'index' ORDER BY name;")
        var indexes: [String] = []
        while try indexesStmt.step() {
            if let name = indexesStmt.columnText(at: 0) {
                indexes.append(name)
            }
        }

        XCTAssertTrue(indexes.contains("ux_apps_device_exe"))
        XCTAssertTrue(indexes.contains("ix_apps_device_bundle"))
        XCTAssertTrue(indexes.contains("ix_apps_canonical"))
        XCTAssertTrue(indexes.contains("ix_sessions_app_start"))
        XCTAssertTrue(indexes.contains("ux_sessions_app_start"))
    }
}
