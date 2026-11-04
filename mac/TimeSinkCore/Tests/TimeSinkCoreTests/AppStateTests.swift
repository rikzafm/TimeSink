import XCTest
import Foundation
@testable import TimeSinkCore
@testable import TimeSinkApp

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

    func testPeriodCanPrevAcrossViews() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try Migrator.apply(to: db)

        let devId = try db.scalarText("SELECT device_id FROM devices LIMIT 1;") ?? "mac-1"
        // App added on 2026-11-01 (UTC: 1793500000 approx)
        // 2026-11-01 00:00:00 UTC is 1793491200
        let nov1Utc: Int64 = 1793491200
        try db.exec("""
            INSERT INTO apps (id, exe_path, display_name, category, added_utc, device_id)
            VALUES (1, '/Applications/Xcode.app', 'Xcode', 'Developer Tools', \(nov1Utc), '\(devId)');
            """)

        let zone = TimeZone(secondsFromGMT: 0)!

        // 1. On 2026-11-01 (same day tracking started)
        let ctxNov1 = UsageContext(
            now: Date(timeIntervalSince1970: TimeInterval(nov1Utc + 3600)),
            zone: zone,
            db: db,
            deviceScope: .this,
            localDeviceId: devId
        )

        let todayNov1 = try TodayQuery.run(ctxNov1)
        XCTAssertEqual(todayNov1.firstTrackedDay, "2026-11-01")
        XCTAssertFalse(todayNov1.canPrev, "Today chevron should be disabled when viewing the first tracked day")

        let weekNov1 = try WeekQuery.run(ctxNov1, weeksBack: 0)
        XCTAssertEqual(weekNov1.firstTrackedDay, "2026-11-01")
        XCTAssertFalse(weekNov1.canPrev(weekStart: .mon), "Week chevron should be disabled when viewing the week containing the first tracked day")

        let monthNov1 = try MonthQuery.run(ctxNov1, year: 2026, month: 11)
        XCTAssertEqual(monthNov1.firstTracked, "2026-11-01")
        XCTAssertFalse(monthNov1.canPrev, "Month chevron should be disabled when viewing the month containing the first tracked day")

        // 2. Later date: 2026-11-15 (2 weeks later, same month)
        let nov15Utc = nov1Utc + 14 * 86400
        let ctxNov15 = UsageContext(
            now: Date(timeIntervalSince1970: TimeInterval(nov15Utc)),
            zone: zone,
            db: db,
            deviceScope: .this,
            localDeviceId: devId
        )

        let todayNov15 = try TodayQuery.run(ctxNov15)
        XCTAssertTrue(todayNov15.canPrev, "Today chevron should be enabled when viewing a date after the first tracked day")

        let weekNov15 = try WeekQuery.run(ctxNov15, weeksBack: 0)
        XCTAssertTrue(weekNov15.canPrev(weekStart: .mon), "Week chevron should be enabled when viewing a week after the first tracked week")

        let monthNov15 = try MonthQuery.run(ctxNov15, year: 2026, month: 11)
        XCTAssertFalse(monthNov15.canPrev, "Month chevron should still be disabled in November since November contains the first tracked day")

        // 3. Next month: 2026-12-05
        let dec5Utc = nov1Utc + 34 * 86400
        let ctxDec5 = UsageContext(
            now: Date(timeIntervalSince1970: TimeInterval(dec5Utc)),
            zone: zone,
            db: db,
            deviceScope: .this,
            localDeviceId: devId
        )

        let monthDec = try MonthQuery.run(ctxDec5, year: 2026, month: 12)
        XCTAssertTrue(monthDec.canPrev, "Month chevron should be enabled in December when tracking started in November")
    }

    func testPeriodCanPrevWhenNoData() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try Migrator.apply(to: db)

        let devId = try db.scalarText("SELECT device_id FROM devices LIMIT 1;") ?? "mac-1"
        let zone = TimeZone(secondsFromGMT: 0)!
        let ctx = UsageContext(
            now: Date(timeIntervalSince1970: 1793491200),
            zone: zone,
            db: db,
            deviceScope: .this,
            localDeviceId: devId
        )

        let today = try TodayQuery.run(ctx)
        XCTAssertNil(today.firstTrackedDay)
        XCTAssertFalse(today.canPrev, "Today chevron should be disabled when there is no tracking data")

        let week = try WeekQuery.run(ctx, weeksBack: 0)
        XCTAssertNil(week.firstTrackedDay)
        XCTAssertFalse(week.canPrev(weekStart: .mon), "Week chevron should be disabled when there is no tracking data")

        let month = try MonthQuery.run(ctx, year: 2026, month: 11)
        XCTAssertNil(month.firstTracked)
        XCTAssertFalse(month.canPrev, "Month chevron should be disabled when there is no tracking data")
    }

    func testSettingKeysConstants() {
        XCTAssertEqual(SettingKeys.weekStart, "week.start")
        XCTAssertEqual(SettingKeys.minSessionSeconds, "tracking.minSessionSeconds")
        XCTAssertEqual(SettingKeys.theme, "ui.theme")
        XCTAssertEqual(SettingKeys.accent, "ui.accent")
        XCTAssertEqual(SettingKeys.appIcon, "ui.icon")
        XCTAssertEqual(SettingKeys.showMenuBarTime, "ui.showMenuBarTime")
        XCTAssertEqual(SettingKeys.backupMode, "backup.mode")
        XCTAssertEqual(SettingKeys.backupLastUtc, "backup.last_utc")
        XCTAssertEqual(SettingKeys.shortDeviceNames, "devices.shortNames")
    }

    func testComputeDisplayNames() {
        // 1 Mac + 1 PC -> "Mac" and "PC"
        let mixed: [(id: String, name: String, platform: String, isLocal: Bool, firstSeen: String)] = [
            (id: "dev-mac", name: "Rikza’s MacBook Pro", platform: "macos", isLocal: true, firstSeen: "2026-01-01"),
            (id: "dev-win", name: "DESKTOP-B9N00S8", platform: "windows", isLocal: false, firstSeen: "2026-01-02"),
        ]
        let mixedNames = DeviceInfoService.computeDisplayNames(devices: mixed, shortNames: true)
        XCTAssertEqual(mixedNames["dev-mac"], "Mac")
        XCTAssertEqual(mixedNames["dev-win"], "PC")

        // 2 PCs -> "PC 1" and "PC 2" (local is PC 1)
        let twoPCs: [(id: String, name: String, platform: String, isLocal: Bool, firstSeen: String)] = [
            (id: "win-remote", name: "Living Room PC", platform: "windows", isLocal: false, firstSeen: "2026-01-01"),
            (id: "win-local", name: "DESKTOP-B9N00S8", platform: "windows", isLocal: true, firstSeen: "2026-01-02"),
        ]
        let pcNames = DeviceInfoService.computeDisplayNames(devices: twoPCs, shortNames: true)
        XCTAssertEqual(pcNames["win-local"], "PC 1")
        XCTAssertEqual(pcNames["win-remote"], "PC 2")

        // 2 Macs -> "Mac 1" and "Mac 2" (local is Mac 1)
        let twoMacs: [(id: String, name: String, platform: String, isLocal: Bool, firstSeen: String)] = [
            (id: "mac-remote", name: "Studio Mac", platform: "macos", isLocal: false, firstSeen: "2026-01-01"),
            (id: "mac-local", name: "MacBook Pro", platform: "macos", isLocal: true, firstSeen: "2026-01-02"),
        ]
        let macNames = DeviceInfoService.computeDisplayNames(devices: twoMacs, shortNames: true)
        XCTAssertEqual(macNames["mac-local"], "Mac 1")
        XCTAssertEqual(macNames["mac-remote"], "Mac 2")

        // 2 PCs + 1 Mac -> "PC 1", "PC 2", "Mac"
        let multi: [(id: String, name: String, platform: String, isLocal: Bool, firstSeen: String)] = [
            (id: "win-1", name: "Gaming PC", platform: "windows", isLocal: true, firstSeen: "2026-01-01"),
            (id: "win-2", name: "Office PC", platform: "windows", isLocal: false, firstSeen: "2026-01-02"),
            (id: "mac-1", name: "MacBook Pro", platform: "macos", isLocal: false, firstSeen: "2026-01-03"),
        ]
        let multiNames = DeviceInfoService.computeDisplayNames(devices: multi, shortNames: true)
        XCTAssertEqual(multiNames["win-1"], "PC 1")
        XCTAssertEqual(multiNames["win-2"], "PC 2")
        XCTAssertEqual(multiNames["mac-1"], "Mac")

        // Short names disabled -> full device names
        let fullNames = DeviceInfoService.computeDisplayNames(devices: mixed, shortNames: false)
        XCTAssertEqual(fullNames["dev-mac"], "Rikza’s MacBook Pro")
        XCTAssertEqual(fullNames["dev-win"], "DESKTOP-B9N00S8")
    }

    func testBackupRetentionKeepsNewestSevenAndMonthly() {
        let now = Date(timeIntervalSince1970: 1793500000) // late 2026

        // Create 10 daily backup names
        var files: [String] = []
        for i in 1...10 {
            files.append(String(format: "daily-202610%02d.timesink", i))
        }
        // Non-backup or manual files shouldn't be touched
        files.append("manual-20261015-120000.timesink")
        files.append("other.txt")

        let toDelete = BackupRetention.filesToDelete(fileNames: files, now: now)
        // Out of 10 daily backups, 7 newest are kept (10, 09, 08, 07, 06, 05, 04)
        // And the newest of October 2026 is 10 (already kept).
        // Therefore, 01, 02, 03 should be deleted.
        XCTAssertTrue(toDelete.contains("daily-20261001.timesink"))
        XCTAssertTrue(toDelete.contains("daily-20261002.timesink"))
        XCTAssertTrue(toDelete.contains("daily-20261003.timesink"))
        XCTAssertFalse(toDelete.contains("daily-20261010.timesink"))
        XCTAssertFalse(toDelete.contains("daily-20261004.timesink"))
        XCTAssertFalse(toDelete.contains("manual-20261015-120000.timesink"))
        XCTAssertFalse(toDelete.contains("other.txt"))
    }

    func testAutoBackupRunIfDue() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try Migrator.apply(to: db)

        let tempDir = URL(fileURLWithPath: NSTemporaryDirectory()).appendingPathComponent("test-autobackup-\(UUID().uuidString)").path
        try FileManager.default.createDirectory(atPath: tempDir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(atPath: tempDir) }

        // Mode is not set or Off
        try db.exec("INSERT INTO settings (key, value) VALUES ('backup.mode', 'Off');")
        let resOff = AutoBackup.runIfDue(db: db, folder: tempDir)
        XCTAssertNil(resOff)

        // Set mode to Daily, last backup is recent (< 23 hours)
        let now = Date()
        let nowSec = Int64(now.timeIntervalSince1970)
        try db.exec("INSERT INTO settings (key, value) VALUES ('backup.mode', 'Daily') ON CONFLICT(key) DO UPDATE SET value = excluded.value;")
        try db.exec("INSERT INTO settings (key, value) VALUES ('backup.last_utc', '\(nowSec - 3600)') ON CONFLICT(key) DO UPDATE SET value = excluded.value;")

        let resRecent = AutoBackup.runIfDue(db: db, folder: tempDir, now: now)
        XCTAssertNil(resRecent)
    }

    func testCrossDeviceMergedAppDisplaysNameInsteadOfIdInThisDeviceScope() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try Migrator.apply(to: db)

        let devMac = try db.scalarText("SELECT device_id FROM devices LIMIT 1;") ?? "mac-local"
        let devWin = "pc-remote-win"

        try db.exec("""
            INSERT INTO devices (device_id, name, platform, is_local, first_seen_utc, state)
            VALUES ('\(devWin)', 'Windows PC', 'windows', 0, '2026-10-01T00:00:00Z', 'active');

            -- App 19 is on Windows PC (canonical app)
            INSERT INTO apps (id, exe_path, display_name, category, added_utc, device_id)
            VALUES (19, 'C:\\Users\\User\\AppData\\Local\\Programs\\Antigravity\\antigravity.exe', 'Antigravity', 'Developer Tools', 1000, '\(devWin)');

            -- App 65 is on Mac (cross-device merged into App 19)
            INSERT INTO apps (id, canonical_id, exe_path, display_name, category, added_utc, device_id)
            VALUES (65, 19, '/Applications/Antigravity.app', 'Antigravity', 'Developer Tools', 1000, '\(devMac)');

            -- Live open session for local Mac app (65)
            INSERT INTO sessions (id, app_id, start_utc, end_utc, last_seen_utc, end_reason)
            VALUES (1, 65, 1793500000 - 180, NULL, 1793500000, NULL);
            """)

        let ctxThis = UsageContext(
            now: Date(timeIntervalSince1970: 1793500000),
            zone: .current,
            db: db,
            deviceScope: .this,
            localDeviceId: devMac
        )

        let todayRes = try TodayQuery.run(ctxThis)
        XCTAssertEqual(todayRes.rows.count, 1)
        XCTAssertEqual(todayRes.rows.first?.name, "Antigravity", "App name must be 'Antigravity', not '19'")
        XCTAssertEqual(todayRes.longestSession?.appName, "Antigravity")

        let allRes = try AllQuery.run(ctxThis)
        XCTAssertEqual(allRes.apps.count, 1)
        XCTAssertEqual(allRes.apps.first?.name, "Antigravity")

        let detailRes = AppDetailQuery.run(context: ctxThis, appId: todayRes.rows.first?.appId ?? 0, year: 2026)
        XCTAssertNotNil(detailRes)
        XCTAssertEqual(detailRes?.name, "Antigravity")
    }

    @MainActor
    func testTodayExpandedAppIdEscNavigationAndTabSwitch() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try Migrator.apply(to: db)
        let state = TimeSinkApp.AppState(db: db)

        state.selectedTab = .today
        XCTAssertNil(state.todayExpandedAppId)
        XCTAssertFalse(state.isDrilldownOrOverlay)

        // Opening dial sets drilldown
        state.todayExpandedAppId = 42
        XCTAssertTrue(state.isDrilldownOrOverlay)

        // Esc closes dial
        state.handleGoBack()
        XCTAssertNil(state.todayExpandedAppId)
        XCTAssertFalse(state.isDrilldownOrOverlay)

        // Esc priority: dial closes before date drilldown
        state.daysBack = 2
        state.todayExpandedAppId = 42
        state.handleGoBack()
        XCTAssertNil(state.todayExpandedAppId, "First Esc closes the expanded dial")
        XCTAssertEqual(state.daysBack, 2, "Date drilldown remains until next Esc")
        state.handleGoBack()
        XCTAssertEqual(state.daysBack, 0, "Second Esc resets date drilldown")

        // Tab switch collapses dial
        state.todayExpandedAppId = 42
        state.switchToTab(.week)
        XCTAssertNil(state.todayExpandedAppId, "Switching tabs collapses expanded dial")
    }
}

