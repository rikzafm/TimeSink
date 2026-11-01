import XCTest
import Foundation
@testable import TimeSinkCore

final class AppStateTests: XCTestCase {
    func testAppStateQueriesAndTabSwitching() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try Migrator.apply(to: db)

        // Seed some test data
        let devId = try db.scalarText("SELECT device_id FROM devices LIMIT 1;") ?? "mac-1"
        try db.exec("""
            INSERT INTO apps (id, exe_path, display_name, category, added_utc, device_id)
            VALUES (1, '/Applications/Xcode.app', 'Xcode', 'Developer Tools', 1000, '\(devId)');
            INSERT INTO daily_usage (app_id, day, seconds)
            VALUES (1, '2026-11-01', 7200);
            INSERT INTO sessions (id, app_id, start_utc, end_utc, last_seen_utc, end_reason)
            VALUES (1, 1, 1000, 8200, 8200, 'exit');
            """)

        let ctx = UsageContext(
            now: Date(timeIntervalSince1970: 1793500000),
            zone: .current,
            db: db,
            deviceScope: .this,
            localDeviceId: devId
        )

        let todayRes = try TodayQuery.run(ctx)
        XCTAssertNotNil(todayRes)

        let weekRes = try WeekQuery.run(ctx, weeksBack: 0)
        XCTAssertNotNil(weekRes)

        let monthRes = try MonthQuery.run(ctx, year: 2026, month: 11)
        XCTAssertNotNil(monthRes)

        let yearRes = try YearQuery.run(ctx)
        XCTAssertNotNil(yearRes)

        let allRes = try AllQuery.run(ctx)
        XCTAssertEqual(allRes.apps.count, 1)
        XCTAssertEqual(allRes.apps.first?.name, "Xcode")

        let detailRes = AppDetailQuery.run(context: ctx, appId: 1, year: 2026)
        XCTAssertNotNil(detailRes)
        XCTAssertEqual(detailRes?.name, "Xcode")
    }

    func testAppStateDeviceScopeToggle() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try Migrator.apply(to: db)

        let devA = try db.scalarText("SELECT device_id FROM devices LIMIT 1;") ?? "mac-1"
        let devB = "pc-remote-2"

        try db.exec("""
            INSERT INTO devices (device_id, name, platform, is_local, first_seen_utc, state)
            VALUES ('\(devB)', 'Gaming PC', 'windows', 0, '2026-10-01T00:00:00Z', 'active');

            INSERT INTO apps (id, exe_path, display_name, category, added_utc, device_id)
            VALUES (1, '/Applications/Safari.app', 'Safari', 'Browser', 1000, '\(devA)');
            INSERT INTO apps (id, exe_path, display_name, category, added_utc, device_id)
            VALUES (2, 'C:\\Program Files\\Edge\\edge.exe', 'Edge', 'Browser', 1000, '\(devB)');

            INSERT INTO daily_usage (app_id, day, seconds) VALUES (1, '2026-11-01', 3600);
            INSERT INTO daily_usage (app_id, day, seconds) VALUES (2, '2026-11-01', 1800);
            """)

        let ctxThis = UsageContext(
            now: Date(timeIntervalSince1970: 1793500000),
            zone: .current,
            db: db,
            deviceScope: .this,
            localDeviceId: devA
        )
        let allThis = try AllQuery.run(ctxThis)
        XCTAssertEqual(allThis.apps.count, 1)

        let ctxAll = UsageContext(
            now: Date(timeIntervalSince1970: 1793500000),
            zone: .current,
            db: db,
            deviceScope: .all,
            localDeviceId: devA
        )
        let allAll = try AllQuery.run(ctxAll)
        XCTAssertEqual(allAll.apps.count, 2)
    }
}
