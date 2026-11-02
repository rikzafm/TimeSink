import XCTest
import Foundation
@testable import TimeSinkCore

final class MacAppLibraryTests: XCTestCase {
    func testTrackAndListApps() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try Migrator.apply(to: db)
        let devId = try db.scalarText("SELECT device_id FROM devices LIMIT 1;") ?? "mac-1"

        let lib = MacAppLibrary(db: db)
        let appId = try lib.trackApp(
            exePath: "/Applications/Ghostty.app",
            displayName: "Ghostty",
            category: "Developer Tools",
            kind: .other,
            isBackground: false,
            deviceId: devId
        )
        XCTAssertGreaterThan(appId, 0)

        let tracked = try lib.getTrackedApps()
        XCTAssertEqual(tracked.count, 1)
        XCTAssertEqual(tracked.first?.displayName, "Ghostty")
        XCTAssertEqual(tracked.first?.kind, .other)
        XCTAssertFalse(tracked.first?.isBackground ?? true)
    }

    func testUpdateAndStopTrackingApp() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try Migrator.apply(to: db)
        let devId = try db.scalarText("SELECT device_id FROM devices LIMIT 1;") ?? "mac-1"

        let lib = MacAppLibrary(db: db)
        let appId = try lib.trackApp(
            exePath: "/Applications/Hades.app",
            displayName: "Hades",
            category: "Games",
            kind: .other,
            deviceId: devId
        )

        try lib.updateApp(appId: appId, displayName: "Hades II", kind: .game, isBackground: true)

        var tracked = try lib.getTrackedApps()
        XCTAssertEqual(tracked.first?.displayName, "Hades II")
        XCTAssertEqual(tracked.first?.kind, .game)
        XCTAssertEqual(tracked.first?.isBackground, true)

        try lib.updateApp(appId: appId, displayName: "Hades II", kind: .game, isBackground: false)
        tracked = try lib.getTrackedApps()
        XCTAssertEqual(tracked.first?.isBackground, false)

        try lib.stopTracking(appId: appId)
        tracked = try lib.getTrackedApps()
        XCTAssertTrue(tracked.isEmpty)
    }

    func testMergeAndUnmergeAcrossDevices() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try Migrator.apply(to: db)
        let devMac = try db.scalarText("SELECT device_id FROM devices LIMIT 1;") ?? "mac-1"
        let devPC = "pc-remote-2"

        try db.exec("""
            INSERT INTO devices (device_id, name, platform, is_local, first_seen_utc, state)
            VALUES ('\(devPC)', 'Gaming PC', 'windows', 0, '2026-10-01T00:00:00Z', 'active');
            """)

        let lib = MacAppLibrary(db: db)
        let macChrome = try lib.trackApp(
            exePath: "/Applications/Google Chrome.app",
            displayName: "Chrome",
            category: "Browser",
            deviceId: devMac
        )

        let pcChrome = try lib.trackApp(
            exePath: "C:\\Program Files\\Google\\Chrome\\chrome.exe",
            displayName: "Chrome",
            category: "Browser",
            deviceId: devPC
        )

        let plan = try lib.openMergePlan(mainId: macChrome)
        XCTAssertEqual(plan.mainName, "Chrome")
        XCTAssertEqual(plan.devices.count, 2)
        XCTAssertEqual(plan.suggestedMatches.count, 1)
        XCTAssertEqual(plan.suggestedMatches.first?.appId, pcChrome)

        // Merge PC Chrome into Mac Chrome
        try lib.merge(mainId: macChrome, candidateIds: [pcChrome])

        let children = try lib.getMergedChildren(mainId: macChrome)
        XCTAssertEqual(children.count, 1)
        XCTAssertEqual(children.first?.id, pcChrome)

        // Unmerge
        try lib.unmerge(appId: pcChrome)
        let childrenAfter = try lib.getMergedChildren(mainId: macChrome)
        XCTAssertTrue(childrenAfter.isEmpty)
    }

    func testClearAppHistory() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try Migrator.apply(to: db)
        let devId = try db.scalarText("SELECT device_id FROM devices LIMIT 1;") ?? "mac-1"

        let lib = MacAppLibrary(db: db)
        let appId = try lib.trackApp(
            exePath: "/Applications/Safari.app",
            displayName: "Safari",
            deviceId: devId
        )

        try db.exec("""
            INSERT INTO sessions (id, app_id, start_utc, end_utc, last_seen_utc, end_reason)
            VALUES (1, \(appId), 1000, 2000, 2000, 'exit');
            INSERT INTO daily_usage (app_id, day, seconds)
            VALUES (\(appId), '2026-11-01', 1000);
            """)

        let countSessions = (try db.scalarInt("SELECT COUNT(*) FROM sessions WHERE app_id = \(appId);")) ?? 0
        XCTAssertEqual(countSessions, 1)

        try lib.clearHistory(appId: appId)

        let countSessionsAfter = (try db.scalarInt("SELECT COUNT(*) FROM sessions WHERE app_id = \(appId);")) ?? 0
        let countUsageAfter = (try db.scalarInt("SELECT COUNT(*) FROM daily_usage WHERE app_id = \(appId);")) ?? 0
        XCTAssertEqual(countSessionsAfter, 0)
        XCTAssertEqual(countUsageAfter, 0)
    }

    func testMergedAppOverlappingSessionsDeduplication() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try Migrator.apply(to: db)
        let devId = try db.scalarText("SELECT device_id FROM devices LIMIT 1;") ?? "mac-1"

        let lib = MacAppLibrary(db: db)
        let main = try lib.trackApp(
            exePath: "/Applications/Deskflow.app/Contents/MacOS/Deskflow",
            displayName: "Deskflow",
            deviceId: devId
        )
        let core = try lib.trackApp(
            exePath: "/Applications/Deskflow.app/Contents/MacOS/deskflow-core",
            displayName: "Deskflow",
            deviceId: devId
        )
        try lib.merge(mainId: main, candidateIds: [core])

        // Main runs 1000...2000 (1000s), Core runs 1500...2500 (1000s) -> Overlap 500s, total union 1500s
        try db.exec("""
            INSERT INTO sessions (id, app_id, start_utc, end_utc, last_seen_utc, end_reason)
            VALUES (1, \(main), 1000, 2000, 2000, 'exit');
            INSERT INTO sessions (id, app_id, start_utc, end_utc, last_seen_utc, end_reason)
            VALUES (2, \(core), 1500, 2500, 2500, 'exit');
            """)

        let activeSec = try lib.getActiveSeconds(appId: main)
        XCTAssertEqual(activeSec, 1500)

        let tracked = try lib.getTrackedApps()
        XCTAssertEqual(tracked.first?.id, main)
        XCTAssertEqual(tracked.first?.totalSeconds, 1500)

        // Verify UsageDays.slices unions overlapping intervals for the same group
        let segs = [
            UsageSeg(group: main, appId: main, start: 1000, end: 2000, open: false, sleep: false),
            UsageSeg(group: main, appId: core, start: 1500, end: 2500, open: false, sleep: false)
        ]
        let slices = UsageDays.slices(segs, zone: TimeZone(secondsFromGMT: 0)!)
        XCTAssertEqual(slices.count, 1)
        XCTAssertEqual(slices[0].start, 1000)
        XCTAssertEqual(slices[0].end, 2500)

        // Verify TodayQuery reports union seconds
        let ctx = UsageContext(now: Date(timeIntervalSince1970: 3000), zone: TimeZone(secondsFromGMT: 0)!, db: db)
        let today = try TodayQuery.run(ctx, forDay: "1970-01-01")
        XCTAssertEqual(today.rows.first?.appId, main)
        XCTAssertEqual(today.rows.first?.seconds, 1500)
        XCTAssertEqual(today.totalSeconds, 1500)
    }

    func testCrossDeviceMergedAppsTrackedQuery() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try Migrator.apply(to: db)
        let devMac = try db.scalarText("SELECT device_id FROM devices LIMIT 1;") ?? "mac-1"
        let devPC = "pc-synced-2"

        try db.exec("""
            INSERT INTO devices (device_id, name, platform, is_local, first_seen_utc, state)
            VALUES ('\(devPC)', 'Gaming PC', 'windows', 0, '2026-10-01T00:00:00Z', 'active');
            """)

        let lib = MacAppLibrary(db: db)
        let macApp = try lib.trackApp(
            exePath: "/Applications/Chrome.app",
            displayName: "Google Chrome",
            category: "Productivity",
            kind: .other,
            deviceId: devMac
        )
        let pcApp = try lib.trackApp(
            exePath: "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
            displayName: "Chrome (PC)",
            category: "Productivity",
            kind: .other,
            deviceId: devPC
        )

        try lib.merge(mainId: macApp, candidateIds: [pcApp])

        let allTracked = try lib.getTrackedApps()
        XCTAssertEqual(allTracked.count, 1)
        let main = try XCTUnwrap(allTracked.first)
        XCTAssertEqual(main.id, macApp)
        XCTAssertEqual(main.deviceId, devMac)
        XCTAssertEqual(main.mergedCount, 1)
        XCTAssertEqual(main.mergedNames, ["Chrome (PC)"])
        XCTAssertEqual(main.mergedDeviceIds, [devPC])
    }
}

