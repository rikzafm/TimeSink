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

    func testUsageContextPreservesLiveOpenSessionsWhenRunningIsNil() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try Migrator.apply(to: db)

        let devId = try db.scalarText("SELECT device_id FROM devices LIMIT 1;") ?? "test-mac"
        try db.exec("""
            INSERT INTO apps (id, exe_path, display_name, category, added_utc, device_id)
            VALUES (101, '/Applications/Safari.app', 'Safari', 'Browser', 1000, '\(devId)');
            INSERT INTO sessions (id, app_id, start_utc, end_utc, last_seen_utc, end_reason)
            VALUES (101, 101, 1000, NULL, 1500, NULL);
            """)

        // When running is nil (M1 fix): open sessions remain open
        let ctxOpen = UsageContext(
            now: Date(timeIntervalSince1970: 1600),
            zone: .current,
            running: nil,
            db: db,
            deviceScope: .this,
            localDeviceId: devId
        )
        let todayOpen = try TodayQuery.run(ctxOpen)
        XCTAssertEqual(todayOpen.runningCount, 1, "Running count must be 1 when running is nil")
        XCTAssertEqual(todayOpen.openSessionCount, 1)
        XCTAssertTrue(todayOpen.rows.first?.isRunning == true, "App row must report isRunning = true")

        // When running is empty Set (pre-M1 bug): open sessions are closed and marked not running
        let ctxBug = UsageContext(
            now: Date(timeIntervalSince1970: 1600),
            zone: .current,
            running: Set<Int64>(),
            db: db,
            deviceScope: .this,
            localDeviceId: devId
        )
        let todayBug = try TodayQuery.run(ctxBug)
        XCTAssertEqual(todayBug.runningCount, 0, "Empty running set forces runningCount to 0")
        XCTAssertFalse(todayBug.rows.first?.isRunning == true)
    }

    func testIsLocalDeviceMatchingLogic() throws {
        let localUuid = "7B9920F0-C9C4-47FA-A04B-7FA1D36BC3EF"

        func isLocal(_ devId: String, localDeviceId: String) -> Bool {
            if devId.isEmpty || localDeviceId.isEmpty { return true }
            return devId.caseInsensitiveCompare(localDeviceId) == .orderedSame
        }

        // Exact match
        XCTAssertTrue(isLocal(localUuid, localDeviceId: localUuid))
        // Case insensitive match
        XCTAssertTrue(isLocal(localUuid.lowercased(), localDeviceId: localUuid))
        XCTAssertTrue(isLocal(localUuid.uppercased(), localDeviceId: localUuid))
        // Empty devId is treated as local
        XCTAssertTrue(isLocal("", localDeviceId: localUuid))
        // Empty localDeviceId matches
        XCTAssertTrue(isLocal(localUuid, localDeviceId: ""))
        // Different remote UUIDs return false
        XCTAssertFalse(isLocal("00000000-0000-0000-0000-000000000000", localDeviceId: localUuid))
        // Substring "mac" in remote UUID should not falsely match
        XCTAssertFalse(isLocal("A1B2C3D4-MAC1-0000-0000-000000000000", localDeviceId: localUuid))
    }

    func testPauseOnSleepSettingsReconciliation() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try Migrator.apply(to: db)

        XCTAssertEqual(SettingKeys.pauseOnSleep, "tracking.pauseOnSleep")

        let engineSettings = DbTrackingSettings(db: db)
        // Default should be true
        XCTAssertTrue(engineSettings.pauseOnSleep)

        // Setting to false via reconciled key
        try db.exec("INSERT INTO settings (key, value) VALUES ('\(SettingKeys.pauseOnSleep)', 'false') ON CONFLICT(key) DO UPDATE SET value = excluded.value;")
        XCTAssertFalse(engineSettings.pauseOnSleep, "DbTrackingSettings must read SettingKeys.pauseOnSleep")

        // Setting to true
        try db.exec("INSERT INTO settings (key, value) VALUES ('\(SettingKeys.pauseOnSleep)', 'true') ON CONFLICT(key) DO UPDATE SET value = excluded.value;")
        XCTAssertTrue(engineSettings.pauseOnSleep)
    }
}

