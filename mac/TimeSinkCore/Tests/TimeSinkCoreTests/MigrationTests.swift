import XCTest
import Foundation
@testable import TimeSinkCore

final class MigrationTests: XCTestCase {
    func testExistingVersion4DatabaseGainsUpdatedUtcColumn() throws {
        let db = try SQLiteDatabase.inMemory()
        try Migrator.apply(to: db, migrations: [M001_Initial(), M002_BackgroundApps(), M003_AppKind(), M004_Devices()])
        XCTAssertEqual(db.userVersion, 4)
        XCTAssertEqual(try db.scalarInt("SELECT COUNT(*) FROM pragma_table_info('apps') WHERE name = 'updated_utc';"), 0)

        try Migrator.apply(to: db)
        XCTAssertEqual(db.userVersion, 5)
        XCTAssertEqual(try db.scalarInt("SELECT COUNT(*) FROM pragma_table_info('apps') WHERE name = 'updated_utc';"), 1)
    }

    func testMigrationsApplyCleanlyToVersion4() throws {
        let db = try SQLiteDatabase.inMemory()
        XCTAssertEqual(db.userVersion, 0)

        try Migrator.apply(to: db)
        XCTAssertEqual(db.userVersion, 5)

        // Verify tables exist
        let tablesStmt = try db.prepare("SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;")
        var tables: [String] = []
        while try tablesStmt.step() {
            if let name = tablesStmt.columnText(at: 0) {
                tables.append(name)
            }
        }

        XCTAssertTrue(tables.contains("apps"))
        XCTAssertTrue(tables.contains("sessions"))
        XCTAssertTrue(tables.contains("daily_usage"))
        XCTAssertTrue(tables.contains("devices"))
        XCTAssertTrue(tables.contains("settings"))
        XCTAssertTrue(tables.contains("icons"))
        XCTAssertTrue(tables.contains("goals"))
        XCTAssertTrue(tables.contains("ignored_suggestions"))

        // Verify apps columns
        let appsColsStmt = try db.prepare("SELECT name FROM pragma_table_info('apps');")
        var appCols: [String] = []
        while try appsColsStmt.step() {
            if let name = appsColsStmt.columnText(at: 0) {
                appCols.append(name)
            }
        }

        XCTAssertTrue(appCols.contains("device_id"))
        XCTAssertTrue(appCols.contains("bundle_id"))
        XCTAssertTrue(appCols.contains("is_background"))
        XCTAssertTrue(appCols.contains("kind"))

        // Verify local device was created
        let localDeviceCount = try db.scalarInt("SELECT COUNT(*) FROM devices WHERE is_local = 1;")
        XCTAssertEqual(localDeviceCount, 1)

        let platform = try db.scalarText("SELECT platform FROM devices WHERE is_local = 1;")
        XCTAssertEqual(platform, "macos")
    }

    func testRebuildAppsTablePreservesExistingApps() throws {
        let db = try SQLiteDatabase.inMemory()

        // Apply M001 to M003
        try M001_Initial().apply(to: db)
        db.userVersion = 1
        try M002_BackgroundApps().apply(to: db)
        db.userVersion = 2
        try M003_AppKind().apply(to: db)
        db.userVersion = 3

        // Insert legacy app
        try db.exec("""
        INSERT INTO apps (exe_path, display_name, category, state, added_utc, is_background, kind)
        VALUES ('/Applications/Test.app', 'Test App', 'Productivity', 'tracked', 1700000000, 1, 0);
        """)

        // Apply M004
        try M004_Devices().apply(to: db)
        db.userVersion = 4

        // Verify legacy app was preserved with device_id
        let count = try db.scalarInt("SELECT COUNT(*) FROM apps WHERE exe_path = '/Applications/Test.app';")
        XCTAssertEqual(count, 1)

        let devId = try db.scalarText("SELECT device_id FROM apps WHERE exe_path = '/Applications/Test.app';")
        XCTAssertNotNil(devId)
        XCTAssertFalse(devId!.isEmpty)

        let isBg = try db.scalarInt("SELECT is_background FROM apps WHERE exe_path = '/Applications/Test.app';")
        XCTAssertEqual(isBg, 1)
    }

    func testNewerDatabaseVersionIsRejected() throws {
        let db = try SQLiteDatabase.inMemory()
        db.userVersion = 99

        XCTAssertThrowsError(try Migrator.apply(to: db)) { error in
            guard case MigrationError.databaseNewerThanShipped(let cur, let latest) = error else {
                XCTFail("Expected databaseNewerThanShipped error")
                return
            }
            XCTAssertEqual(cur, 99)
            XCTAssertEqual(latest, 5)
        }
    }
}
