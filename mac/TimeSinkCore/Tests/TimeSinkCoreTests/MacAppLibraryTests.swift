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

        try lib.updateApp(appId: appId, displayName: "Hades II", kind: .game, isBackground: false)

        var tracked = try lib.getTrackedApps()
        XCTAssertEqual(tracked.first?.displayName, "Hades II")
        XCTAssertEqual(tracked.first?.kind, .game)

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
}

