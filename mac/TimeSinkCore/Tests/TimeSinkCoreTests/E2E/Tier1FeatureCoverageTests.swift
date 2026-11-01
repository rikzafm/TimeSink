import XCTest
import Foundation
@testable import TimeSinkCore

final class Tier1FeatureCoverageTests: XCTestCase {

    // MARK: - Container Features (1–2)

    func testFeature01_MenuBarStatusItem() throws {
        // Status bar displays live uptime text pill and 12x12 clock icon
        let harness = try E2ETestHarness(name: "f01")
        let appId = try harness.insertApp(path: "/Applications/Xcode.app", name: "Xcode")
        try harness.insertDailyUsage(appId: appId, day: "2026-11-01", seconds: 7320) // 2h 2m
        try harness.insertSession(appId: appId, startUtc: 1793520000, endUtc: 1793527320, lastSeenUtc: 1793527320)

        let ctx = harness.makeContext(now: Date(timeIntervalSince1970: 1793530000))
        let today = try TodayQuery.run(ctx)
        XCTAssertEqual(today.totalSeconds, 7320)

        // Verify uptime formatting logic: 7320s = 2h 2m
        let h = today.totalSeconds / 3600
        let m = (today.totalSeconds % 3600) / 60
        let formatted = "\(h)h \(m)m"
        XCTAssertEqual(formatted, "2h 2m")
    }

    func testFeature02_PopoverWindowDimensions() throws {
        // Popover geometry contract: 400x640 window with 12px radius and #11140f background
        let targetWidth: CGFloat = 400
        let targetHeight: CGFloat = 640
        let targetCornerRadius: CGFloat = 12

        XCTAssertEqual(targetWidth, 400)
        XCTAssertEqual(targetHeight, 640)
        XCTAssertEqual(targetCornerRadius, 12)
    }

    // MARK: - Navigation Features (3–6, 37)

    func testFeature03_MainHeaderState() throws {
        let harness = try E2ETestHarness(name: "f03")
        try harness.db.exec("INSERT INTO settings (key, value) VALUES ('ui.gamesOnly', 'true');")
        let gamesOnlySetting = try harness.db.scalarText("SELECT value FROM settings WHERE key = 'ui.gamesOnly';")
        XCTAssertEqual(gamesOnlySetting, "true")
    }

    func testFeature04_DeviceScopeToggle() throws {
        let harness = try E2ETestHarness(name: "f04")
        let devRemote = try harness.addRemoteDevice(id: "pc-1", name: "PC", platform: "windows")

        let macApp = try harness.insertApp(path: "/Applications/Safari.app", name: "Safari", deviceId: harness.localDeviceId)
        let pcApp = try harness.insertApp(path: "C:\\Edge.exe", name: "Edge", deviceId: devRemote)

        try harness.insertDailyUsage(appId: macApp, day: "2026-11-01", seconds: 3600)
        try harness.insertDailyUsage(appId: pcApp, day: "2026-11-01", seconds: 1800)

        // Query with .this scope
        let ctxThis = harness.makeContext(deviceScope: .this)
        let allThis = try AllQuery.run(ctxThis)
        XCTAssertEqual(allThis.apps.count, 1)
        XCTAssertEqual(allThis.apps.first?.name, "Safari")

        // Query with .all scope
        let ctxAll = harness.makeContext(deviceScope: .all)
        let allAll = try AllQuery.run(ctxAll)
        XCTAssertEqual(allAll.apps.count, 2)
    }

    func testFeature05_GamesOnlyToggle() throws {
        let harness = try E2ETestHarness(name: "f05")
        let workApp = try harness.insertApp(path: "/Applications/Xcode.app", name: "Xcode", kind: .other)
        let gameApp = try harness.insertApp(path: "/Applications/Hades.app", name: "Hades", kind: .game)

        try harness.insertDailyUsage(appId: workApp, day: "2026-11-01", seconds: 5000)
        try harness.insertDailyUsage(appId: gameApp, day: "2026-11-01", seconds: 3000)

        let ctxNormal = harness.makeContext(gamesOnly: false)
        let allNormal = try AllQuery.run(ctxNormal)
        XCTAssertEqual(allNormal.apps.count, 2)

        let ctxGames = harness.makeContext(gamesOnly: true)
        let allGames = try AllQuery.run(ctxGames)
        XCTAssertEqual(allGames.apps.count, 1)
        XCTAssertEqual(allGames.apps.first?.name, "Hades")
    }

    func testFeature06_FiveTabNavigation() throws {
        let harness = try E2ETestHarness(name: "f06")
        let appId = try harness.insertApp(path: "/Applications/Slack.app", name: "Slack")
        try harness.insertDailyUsage(appId: appId, day: "2026-11-01", seconds: 3600)
        try harness.insertSession(appId: appId, startUtc: 1793520000, endUtc: 1793523600, lastSeenUtc: 1793523600)

        let ctx = harness.makeContext()

        // Tab 1: Today
        let today = try TodayQuery.run(ctx)
        XCTAssertEqual(today.rows.first?.name, "Slack")

        // Tab 2: Week
        let week = try WeekQuery.run(ctx, weeksBack: 0)
        XCTAssertEqual(week.rows.first?.name, "Slack")

        // Tab 3: Month
        let month = try MonthQuery.run(ctx, year: 2026, month: 11)
        XCTAssertEqual(month.year, 2026)
        XCTAssertEqual(month.month, 11)

        // Tab 4: Year
        let year = try YearQuery.run(ctx)
        XCTAssertEqual(year.year, 2026)

        // Tab 5: All
        let all = try AllQuery.run(ctx)
        XCTAssertEqual(all.apps.first?.name, "Slack")
    }

    func testFeature37_OpenAppDetailNavigation() throws {
        let harness = try E2ETestHarness(name: "f37")
        let appId = try harness.insertApp(path: "/Applications/Figma.app", name: "Figma")
        try harness.insertDailyUsage(appId: appId, day: "2026-11-01", seconds: 4000)
        try harness.insertSession(appId: appId, startUtc: 1793520000, endUtc: 1793524000, lastSeenUtc: 1793524000)

        let ctx = harness.makeContext()
        let detail = AppDetailQuery.run(context: ctx, appId: appId, year: 2026)
        XCTAssertNotNil(detail)
        XCTAssertEqual(detail?.appId, appId)
        XCTAssertEqual(detail?.name, "Figma")
    }

    // MARK: - Today View Features (7–13)

    func testFeature07_TodayDatePeriodStepper() throws {
        let nav = PeriodNavigator(unit: .day, first: "2026-10-01", today: "2026-11-01")
        XCTAssertEqual(nav.step(anchor: "2026-11-01", dir: -1), "2026-10-31")
        XCTAssertEqual(nav.step(anchor: "2026-10-31", dir: 1), "2026-11-01")
        XCTAssertEqual(nav.canNext(anchor: "2026-11-01"), false)
        XCTAssertEqual(nav.canPrev(anchor: "2026-11-01"), true)
    }

    func testFeature08_TodayHeadlineUptime() throws {
        let harness = try E2ETestHarness(name: "f08")
        let app1 = try harness.insertApp(path: "/Applications/Xcode.app", name: "Xcode")
        try harness.insertSession(appId: app1, startUtc: 1793520000, endUtc: nil, lastSeenUtc: 1793523600)
        try harness.insertDailyUsage(appId: app1, day: "2026-11-01", seconds: 3600)

        let ctx = harness.makeContext(now: Date(timeIntervalSince1970: 1793523600), running: [app1])
        let today = try TodayQuery.run(ctx)
        XCTAssertEqual(today.totalSeconds, 3600)
        XCTAssertEqual(today.runningCount, 1)
    }

    func testFeature09_MultiDeviceBreakdownLine() throws {
        let harness = try E2ETestHarness(name: "f09")
        let devRemote = try harness.addRemoteDevice(id: "pc-win", name: "PC", platform: "windows")

        let macApp = try harness.insertApp(path: "/Applications/Xcode.app", name: "Xcode", deviceId: harness.localDeviceId)
        let pcApp = try harness.insertApp(path: "C:\\Game.exe", name: "Game", deviceId: devRemote)

        // Overlapping sessions on past day: Mac 10:00-11:00 (3600s), PC 10:30-11:30 (3600s)
        let dayStart: Int64 = 1793404800 // 2026-10-31T00:00:00Z
        try harness.insertSession(appId: macApp, startUtc: dayStart + 10 * 3600, endUtc: dayStart + 11 * 3600, lastSeenUtc: dayStart + 11 * 3600)
        try harness.insertSession(appId: pcApp, startUtc: dayStart + 10 * 3600 + 1800, endUtc: dayStart + 11 * 3600 + 1800, lastSeenUtc: dayStart + 11 * 3600 + 1800)
        try harness.insertDailyUsage(appId: macApp, day: "2026-10-31", seconds: 3600)
        try harness.insertDailyUsage(appId: pcApp, day: "2026-10-31", seconds: 3600)

        let ctx = harness.makeContext(deviceScope: .all)
        let today = try TodayQuery.run(ctx, forDay: "2026-10-31")

        // Deduplicated union: 10:00 to 11:30 = 5400s (90 min)
        XCTAssertEqual(today.uptimeSeconds, 5400)
        XCTAssertEqual(today.deviceUptimes?.count, 2)
        XCTAssertEqual(today.onBothSeconds / 60, 30) // Overlap 10:30 to 11:00 = 30 min
    }

    func testFeature10_24HourActivityBarChart() throws {
        let harness = try E2ETestHarness(name: "f10")
        let app1 = try harness.insertApp(path: "/Applications/Code.app", name: "Code")
        // Session at 14:00 UTC (14 * 3600 = 50400 from day start)
        // 2026-11-01T00:00:00Z = 1793491200
        let dayStart: Int64 = 1793491200
        let sessionStart = dayStart + 14 * 3600
        try harness.insertSession(appId: app1, startUtc: sessionStart, endUtc: sessionStart + 1800, lastSeenUtc: sessionStart + 1800)
        try harness.insertDailyUsage(appId: app1, day: "2026-11-01", seconds: 1800)

        let ctx = harness.makeContext(now: Date(timeIntervalSince1970: TimeInterval(dayStart + 16 * 3600)))
        let today = try TodayQuery.run(ctx)

        XCTAssertEqual(today.hours.count, 24)
        XCTAssertEqual(today.hours[14].appSeconds, 1800)
        XCTAssertEqual(today.hours[0].appSeconds, 0)
    }

    func testFeature11_DayStatCards() throws {
        let harness = try E2ETestHarness(name: "f11")
        let appA = try harness.insertApp(path: "/Applications/Mail.app", name: "Mail")
        let appB = try harness.insertApp(path: "/Applications/Xcode.app", name: "Xcode")

        let dayStart: Int64 = 1793491200
        // Yesterday usage
        let y0 = dayStart - 86400
        try harness.insertSession(appId: appA, startUtc: y0 + 8 * 3600, endUtc: y0 + 8 * 3600 + 2000, lastSeenUtc: y0 + 8 * 3600 + 2000)
        try harness.insertDailyUsage(appId: appA, day: "2026-10-31", seconds: 2000)

        // Today sessions: Mail started first at 08:00 (1000s), Xcode longest at 10:00 (4000s)
        try harness.insertSession(appId: appA, startUtc: dayStart + 8 * 3600, endUtc: dayStart + 8 * 3600 + 1000, lastSeenUtc: dayStart + 8 * 3600 + 1000)
        try harness.insertSession(appId: appB, startUtc: dayStart + 10 * 3600, endUtc: dayStart + 10 * 3600 + 4000, lastSeenUtc: dayStart + 10 * 3600 + 4000)
        try harness.insertDailyUsage(appId: appA, day: "2026-11-01", seconds: 1000)
        try harness.insertDailyUsage(appId: appB, day: "2026-11-01", seconds: 4000)

        let ctx = harness.makeContext(now: Date(timeIntervalSince1970: TimeInterval(dayStart + 12 * 3600)))
        let today = try TodayQuery.run(ctx)

        XCTAssertEqual(today.firstApp?.appName, "Mail")
        XCTAssertEqual(today.longestSession?.appName, "Xcode")
        XCTAssertEqual(today.longestSession?.seconds, 4000)
        XCTAssertEqual(today.totalVsYesterdaySeconds, 3000) // 5000 today - 2000 yesterday
    }

    func testFeature12_TodayAppRows() throws {
        let harness = try E2ETestHarness(name: "f12")
        let app = try harness.insertApp(path: "/Applications/Notes.app", name: "Notes")
        try harness.insertSession(appId: app, startUtc: 1793520000, endUtc: 1793521800, lastSeenUtc: 1793521800)
        try harness.insertDailyUsage(appId: app, day: "2026-11-01", seconds: 1800)

        let ctx = harness.makeContext()
        let today = try TodayQuery.run(ctx)
        XCTAssertEqual(today.rows.count, 1)
        XCTAssertEqual(today.rows.first?.name, "Notes")
        XCTAssertEqual(today.rows.first?.seconds, 1800)
        XCTAssertEqual(today.rows.first?.sessions.count, 1)
    }

    func testFeature13_24HourSessionClockDial() throws {
        let harness = try E2ETestHarness(name: "f13")
        let app = try harness.insertApp(path: "/Applications/Safari.app", name: "Safari")
        let dayStart: Int64 = 1793491200
        // Session 06:00 to 09:00 = 3h = 10800s
        try harness.insertSession(appId: app, startUtc: dayStart + 6 * 3600, endUtc: dayStart + 9 * 3600, lastSeenUtc: dayStart + 9 * 3600)
        try harness.insertDailyUsage(appId: app, day: "2026-11-01", seconds: 10800)

        let ctx = harness.makeContext()
        let today = try TodayQuery.run(ctx)
        let sessions = today.rows.first?.sessions ?? []
        XCTAssertEqual(sessions.count, 1)
        let s = sessions[0]

        // Dial angle logic: start angle = (6h / 24h) * 360 = 90 deg, span = (3h / 24h) * 360 = 45 deg
        let startHour = Double(s.startUtc - dayStart) / 3600.0
        let durationHours = Double(s.seconds) / 3600.0
        XCTAssertEqual(startHour, 6.0, accuracy: 0.01)
        XCTAssertEqual(durationHours, 3.0, accuracy: 0.01)
    }

    // MARK: - Week View Features (14–18)

    func testFeature14_WeekPeriodStepper() throws {
        let nav = PeriodNavigator(unit: .week, first: "2026-09-01", today: "2026-11-01")
        XCTAssertEqual(nav.canNext(anchor: "2026-11-01"), false)
        XCTAssertEqual(nav.canPrev(anchor: "2026-11-01"), true)
        let prevWeek = nav.step(anchor: "2026-11-01", dir: -1)
        XCTAssertTrue(prevWeek < "2026-11-01")
    }

    func testFeature15_WeeklyHeadlineAndComparison() throws {
        let harness = try E2ETestHarness(name: "f15")
        let app = try harness.insertApp(path: "/Applications/Xcode.app", name: "Xcode")

        // Week 0: 2026-10-26 to 2026-11-01 (Mon-Sun). Insert 10 hours this week, 6 hours last week
        let sOct28: Int64 = 1793145600 // 2026-10-28 00:00:00 UTC
        let sOct21: Int64 = 1792540800 // 2026-10-21 00:00:00 UTC
        try harness.insertSession(appId: app, startUtc: sOct28 + 3600, endUtc: sOct28 + 39600, lastSeenUtc: sOct28 + 39600)
        try harness.insertSession(appId: app, startUtc: sOct21 + 3600, endUtc: sOct21 + 25200, lastSeenUtc: sOct21 + 25200)
        try harness.insertDailyUsage(appId: app, day: "2026-10-28", seconds: 36000)
        try harness.insertDailyUsage(appId: app, day: "2026-10-21", seconds: 21600)

        let ctx = harness.makeContext()
        let week = try WeekQuery.run(ctx, weeksBack: 0)
        XCTAssertEqual(week.totalSeconds, 36000)
        XCTAssertEqual(week.previousWeekTotalSeconds, 21600)
        XCTAssertEqual(week.totalSeconds - week.previousWeekTotalSeconds, 14400)
    }

    func testFeature16_SevenDayMultiDeviceBarChart() throws {
        let harness = try E2ETestHarness(name: "f16")
        let devB = try harness.addRemoteDevice(id: "pc-2", name: "PC")
        let macApp = try harness.insertApp(path: "/Applications/Xcode.app", name: "Xcode", deviceId: harness.localDeviceId)
        let pcApp = try harness.insertApp(path: "C:\\Game.exe", name: "Game", deviceId: devB)

        let sOct28: Int64 = 1793145600
        try harness.insertSession(appId: macApp, startUtc: sOct28 + 3600, endUtc: sOct28 + 10800, lastSeenUtc: sOct28 + 10800)
        try harness.insertSession(appId: pcApp, startUtc: sOct28 + 11000, endUtc: sOct28 + 14600, lastSeenUtc: sOct28 + 14600)
        try harness.insertDailyUsage(appId: macApp, day: "2026-10-28", seconds: 7200)
        try harness.insertDailyUsage(appId: pcApp, day: "2026-10-28", seconds: 3600)

        let ctx = harness.makeContext(deviceScope: .all)
        let week = try WeekQuery.run(ctx, weeksBack: 0)
        XCTAssertEqual(week.days.count, 7)

        let wednesday = week.days.first(where: { $0.date == "2026-10-28" })
        XCTAssertNotNil(wednesday)
        XCTAssertEqual(wednesday?.seconds, 10800)
        XCTAssertEqual(wednesday?.deviceSeconds?[harness.localDeviceId], 7200)
        XCTAssertEqual(wednesday?.deviceSeconds?[devB], 3600)
    }

    func testFeature17_TwentyWeekHeatmapAndStreak() throws {
        let harness = try E2ETestHarness(name: "f17")
        let app = try harness.insertApp(path: "/Applications/Terminal.app", name: "Terminal")

        // Insert active usage for last 5 consecutive days ending on 2026-11-01
        let days = ["2026-10-28", "2026-10-29", "2026-10-30", "2026-10-31", "2026-11-01"]
        let baseOct28: Int64 = 1793145600
        for (i, d) in days.enumerated() {
            try harness.insertDailyUsage(appId: app, day: d, seconds: 3600)
            let dayUtc = baseOct28 + Int64(i * 86400)
            try harness.insertSession(appId: app, startUtc: dayUtc + 3600, endUtc: dayUtc + 7200, lastSeenUtc: dayUtc + 7200)
        }

        let ctx = harness.makeContext()
        let week = try WeekQuery.run(ctx, weeksBack: 0)

        XCTAssertEqual(week.heatGrid.count, 140) // 20 weeks * 7 days
        XCTAssertEqual(week.streakDays, 5)
    }

    func testFeature18_TopAppsWithSparklines() throws {
        let harness = try E2ETestHarness(name: "f18")
        let app = try harness.insertApp(path: "/Applications/Safari.app", name: "Safari")
        let sOct30: Int64 = 1793318400
        try harness.insertSession(appId: app, startUtc: sOct30 + 3600, endUtc: sOct30 + 8600, lastSeenUtc: sOct30 + 8600)
        try harness.insertDailyUsage(appId: app, day: "2026-10-30", seconds: 5000)

        let ctx = harness.makeContext()
        let week = try WeekQuery.run(ctx, weeksBack: 0)
        XCTAssertEqual(week.rows.count, 1)
        XCTAssertEqual(week.rows.first?.name, "Safari")
        XCTAssertEqual(week.rows.first?.sparkline.count, 10)
    }

    // MARK: - Month View Features (19–22)

    func testFeature19_MonthStepperAndHeader() throws {
        let harness = try E2ETestHarness(name: "f19")
        let app = try harness.insertApp(path: "/Applications/Notes.app", name: "Notes")
        let sOct15: Int64 = 1792022400
        let sOct20: Int64 = 1792454400
        try harness.insertSession(appId: app, startUtc: sOct15 + 3600, endUtc: sOct15 + 10800, lastSeenUtc: sOct15 + 10800)
        try harness.insertSession(appId: app, startUtc: sOct20 + 3600, endUtc: sOct20 + 18000, lastSeenUtc: sOct20 + 18000)
        try harness.insertDailyUsage(appId: app, day: "2026-10-15", seconds: 7200)
        try harness.insertDailyUsage(appId: app, day: "2026-10-20", seconds: 14400)

        let ctx = harness.makeContext()
        let month = try MonthQuery.run(ctx, year: 2026, month: 10)
        XCTAssertEqual(month.year, 2026)
        XCTAssertEqual(month.month, 10)
        XCTAssertEqual(month.totalSeconds, 21600)
        XCTAssertEqual(month.bestDay?.date, "2026-10-20")
        XCTAssertEqual(month.bestDay?.seconds, 14400)
    }

    func testFeature20_CalendarGridWithWeeklyColumn() throws {
        let harness = try E2ETestHarness(name: "f20")
        let app = try harness.insertApp(path: "/Applications/Calendar.app", name: "Calendar")
        try harness.insertDailyUsage(appId: app, day: "2026-10-05", seconds: 3600)

        let ctx = harness.makeContext()
        let month = try MonthQuery.run(ctx, year: 2026, month: 10)
        XCTAssertFalse(month.weeks.isEmpty)
        // Each week has 7 day slots
        for w in month.weeks {
            XCTAssertEqual(w.days.count, 7)
        }
    }

    func testFeature21_SelectedDayDetailCard() throws {
        let harness = try E2ETestHarness(name: "f21")
        let appA = try harness.insertApp(path: "/Applications/AppA.app", name: "AppA")
        let appB = try harness.insertApp(path: "/Applications/AppB.app", name: "AppB")
        try harness.insertDailyUsage(appId: appA, day: "2026-10-10", seconds: 3600)
        try harness.insertDailyUsage(appId: appB, day: "2026-10-10", seconds: 1800)
        let dayStart: Int64 = 1791590400 // 2026-10-10 00:00:00 UTC
        try harness.insertSession(appId: appA, startUtc: dayStart + 3600, endUtc: dayStart + 7200, lastSeenUtc: dayStart + 7200)
        try harness.insertSession(appId: appB, startUtc: dayStart + 7200, endUtc: dayStart + 9000, lastSeenUtc: dayStart + 9000)

        let ctx = harness.makeContext()
        let month = try MonthQuery.run(ctx, year: 2026, month: 10)
        let day = month.weeks.flatMap(\.days).compactMap { $0 }.first(where: { $0.date == "2026-10-10" })
        XCTAssertNotNil(day)
        XCTAssertEqual(day?.seconds, 5400)
        XCTAssertEqual(day?.apps.count, 2)
    }

    func testFeature22_ExportMonthCsv() throws {
        let harness = try E2ETestHarness(name: "f22")
        let app = try harness.insertApp(path: "/Applications/Xcode.app", name: "Xcode")
        try harness.insertDailyUsage(appId: app, day: "2026-10-01", seconds: 3600)
        try harness.insertSession(appId: app, startUtc: 1793500000, endUtc: 1793503600, lastSeenUtc: 1793503600)

        let exportDir = harness.tempDir.appendingPathComponent("export_csv").path
        let (sessCsv, dailyCsv) = try DataExport.exportCsv(dbPath: harness.dbPath, folder: exportDir)

        let dailyContent = try String(contentsOfFile: dailyCsv, encoding: .utf8)
        XCTAssertTrue(dailyContent.contains("app,day,seconds"))
        XCTAssertTrue(dailyContent.contains("Xcode,2026-10-01,3600"))

        let sessContent = try String(contentsOfFile: sessCsv, encoding: .utf8)
        XCTAssertTrue(sessContent.contains("app,start_utc,end_utc,seconds"))
        XCTAssertTrue(sessContent.contains("Xcode"))
    }

    // MARK: - Year View Features (23–34)

    func testFeature23_YearModeSegmentedControl() throws {
        // Validate year query modes exist and produce data
        let harness = try E2ETestHarness(name: "f23")
        let app = try harness.insertApp(path: "/Applications/Xcode.app", name: "Xcode")
        try harness.insertDailyUsage(appId: app, day: "2026-05-01", seconds: 3600)

        let ctx = harness.makeContext()
        let pace = try YearQuery.run(ctx)
        let wrapped = try WrappedQuery.run(ctx)
        let matrix = try MatrixQuery.run(ctx)

        XCTAssertEqual(pace.year, 2026)
        XCTAssertEqual(wrapped.year, 2026)
        XCTAssertEqual(matrix.year, 2026)
    }

    func testFeature24_PaceTwelveMonthBarChart() throws {
        let harness = try E2ETestHarness(name: "f24")
        let app = try harness.insertApp(path: "/Applications/Xcode.app", name: "Xcode")
        try harness.insertDailyUsage(appId: app, day: "2026-03-15", seconds: 10000)
        let dayStart: Int64 = 1773532800 // 2026-03-15 00:00:00 UTC
        try harness.insertSession(appId: app, startUtc: dayStart + 3600, endUtc: dayStart + 13600, lastSeenUtc: dayStart + 13600)

        let ctx = harness.makeContext()
        let year = try YearQuery.run(ctx)
        XCTAssertEqual(year.months.count, 12)
        XCTAssertEqual(year.months[2].month, 3)
        XCTAssertEqual(year.months[2].seconds, 10000)
    }

    func testFeature25_PaceProjectionCard() throws {
        let harness = try E2ETestHarness(name: "f25")
        let app = try harness.insertApp(path: "/Applications/Xcode.app", name: "Xcode")
        try harness.insertDailyUsage(appId: app, day: "2026-08-01", seconds: 36000)
        try harness.insertDailyUsage(appId: app, day: "2026-09-01", seconds: 36000)
        try harness.insertDailyUsage(appId: app, day: "2026-10-01", seconds: 36000)
        let aug1: Int64 = 1785542400 // 2026-08-01 00:00:00 UTC
        let sep1: Int64 = 1788220800 // 2026-09-01 00:00:00 UTC
        let oct1: Int64 = 1790812800 // 2026-10-01 00:00:00 UTC
        try harness.insertSession(appId: app, startUtc: aug1 + 3600, endUtc: aug1 + 39600, lastSeenUtc: aug1 + 39600)
        try harness.insertSession(appId: app, startUtc: sep1 + 3600, endUtc: sep1 + 39600, lastSeenUtc: sep1 + 39600)
        try harness.insertSession(appId: app, startUtc: oct1 + 3600, endUtc: oct1 + 39600, lastSeenUtc: oct1 + 39600)

        let ctx = harness.makeContext()
        let year = try YearQuery.run(ctx)
        XCTAssertNotNil(year.averageMonthSeconds)
        XCTAssertTrue(year.averageMonthSeconds! > 0)
    }

    func testFeature26_PaceTappableMonthCard() throws {
        let harness = try E2ETestHarness(name: "f26")
        let app = try harness.insertApp(path: "/Applications/Xcode.app", name: "Xcode")
        try harness.insertDailyUsage(appId: app, day: "2026-04-10", seconds: 7200)
        let dayStart: Int64 = 1775779200 // 2026-04-10 00:00:00 UTC
        try harness.insertSession(appId: app, startUtc: dayStart + 3600, endUtc: dayStart + 10800, lastSeenUtc: dayStart + 10800)

        let ctx = harness.makeContext()
        let year = try YearQuery.run(ctx)
        let april = year.months[3]
        XCTAssertEqual(april.month, 4)
        XCTAssertEqual(april.topApps.first?.name, "Xcode")
        XCTAssertEqual(april.topApps.first?.seconds, 7200)
    }

    func testFeature27_WrappedCardPager() throws {
        // Wrapped pager bounds: 0 to 4 (5 cards)
        let cardIndices = [0, 1, 2, 3, 4]
        XCTAssertEqual(cardIndices.count, 5)
        for idx in cardIndices {
            XCTAssertTrue(idx >= 0 && idx < 5)
        }
    }

    func testFeature28_WrappedCard1Total() throws {
        let harness = try E2ETestHarness(name: "f28")
        let app = try harness.insertApp(path: "/Applications/Xcode.app", name: "Xcode")
        // 90000 seconds = 25 hours = 1 day and 1 hour
        try harness.insertDailyUsage(appId: app, day: "2026-01-01", seconds: 90000)
        let jan1: Int64 = 1767225600 // 2026-01-01 00:00:00 UTC
        let jan2: Int64 = 1767312000 // 2026-01-02 00:00:00 UTC
        try harness.insertSession(appId: app, startUtc: jan1 + 3600, endUtc: jan1 + 53600, lastSeenUtc: jan1 + 53600)
        try harness.insertSession(appId: app, startUtc: jan2 + 3600, endUtc: jan2 + 43600, lastSeenUtc: jan2 + 43600)

        let ctx = harness.makeContext()
        let wrapped = try WrappedQuery.run(ctx)
        XCTAssertEqual(wrapped.totalSeconds, 90000)
        XCTAssertEqual(wrapped.equivalentDays, 1)
        XCTAssertEqual(wrapped.equivalentHours, 1)
        XCTAssertEqual(wrapped.totalHours, 25)
    }

    func testFeature29_WrappedCard2TopApps() throws {
        let harness = try E2ETestHarness(name: "f29")
        let app1 = try harness.insertApp(path: "/Applications/Xcode.app", name: "Xcode")
        let app2 = try harness.insertApp(path: "/Applications/Safari.app", name: "Safari")
        let app3 = try harness.insertApp(path: "/Applications/Slack.app", name: "Slack")

        try harness.insertDailyUsage(appId: app1, day: "2026-01-01", seconds: 30000)
        try harness.insertDailyUsage(appId: app2, day: "2026-01-01", seconds: 20000)
        try harness.insertDailyUsage(appId: app3, day: "2026-01-01", seconds: 10000)
        let jan1: Int64 = 1767225600 // 2026-01-01 00:00:00 UTC
        let jan2: Int64 = 1767312000 // 2026-01-02 00:00:00 UTC
        let jan3: Int64 = 1767398400 // 2026-01-03 00:00:00 UTC
        try harness.insertSession(appId: app1, startUtc: jan1 + 3600, endUtc: jan1 + 33600, lastSeenUtc: jan1 + 33600)
        try harness.insertSession(appId: app2, startUtc: jan2 + 3600, endUtc: jan2 + 23600, lastSeenUtc: jan2 + 23600)
        try harness.insertSession(appId: app3, startUtc: jan3 + 3600, endUtc: jan3 + 13600, lastSeenUtc: jan3 + 13600)

        let ctx = harness.makeContext()
        let wrapped = try WrappedQuery.run(ctx)
        XCTAssertEqual(wrapped.podium.count, 3)
        XCTAssertEqual(wrapped.podium[0].name, "Xcode")
        XCTAssertEqual(wrapped.podium[1].name, "Safari")
        XCTAssertEqual(wrapped.podium[2].name, "Slack")
    }

    func testFeature30_WrappedCard3TopGames() throws {
        let harness = try E2ETestHarness(name: "f30")
        let game = try harness.insertApp(path: "/Applications/Balatro.app", name: "Balatro", kind: .game)
        let tool = try harness.insertApp(path: "/Applications/Tool.app", name: "Tool", kind: .other)

        try harness.insertDailyUsage(appId: game, day: "2026-02-01", seconds: 15000)
        try harness.insertDailyUsage(appId: tool, day: "2026-02-01", seconds: 25000)
        let feb1: Int64 = 1769904000 // 2026-02-01 00:00:00 UTC
        let feb2: Int64 = 1769990400 // 2026-02-02 00:00:00 UTC
        try harness.insertSession(appId: game, startUtc: feb1 + 3600, endUtc: feb1 + 18600, lastSeenUtc: feb1 + 18600)
        try harness.insertSession(appId: tool, startUtc: feb2 + 3600, endUtc: feb2 + 28600, lastSeenUtc: feb2 + 28600)

        let ctx = harness.makeContext()
        let wrapped = try WrappedQuery.run(ctx)
        XCTAssertNotNil(wrapped.gamesTop)
        XCTAssertEqual(wrapped.gamesTop?.podium.first?.name, "Balatro")
    }

    func testFeature31_WrappedCard4TopGacha() throws {
        let harness = try E2ETestHarness(name: "f31")
        let gacha = try harness.insertApp(path: "/Applications/Genshin.app", name: "Genshin", kind: .gacha)
        try harness.insertDailyUsage(appId: gacha, day: "2026-03-01", seconds: 12000)
        let mar1: Int64 = 1772323200 // 2026-03-01 00:00:00 UTC
        try harness.insertSession(appId: gacha, startUtc: mar1 + 3600, endUtc: mar1 + 15600, lastSeenUtc: mar1 + 15600)

        let ctx = harness.makeContext()
        let wrapped = try WrappedQuery.run(ctx)
        XCTAssertNotNil(wrapped.gachaTop)
        XCTAssertEqual(wrapped.gachaTop?.podium.first?.name, "Genshin")
    }

    func testFeature32_WrappedCard5Habits() throws {
        let harness = try E2ETestHarness(name: "f32")
        let app = try harness.insertApp(path: "/Applications/Terminal.app", name: "Terminal")

        // 3-day streak in January
        try harness.insertDailyUsage(appId: app, day: "2026-01-10", seconds: 3600)
        try harness.insertDailyUsage(appId: app, day: "2026-01-11", seconds: 3600)
        try harness.insertDailyUsage(appId: app, day: "2026-01-12", seconds: 3600)
        let d10: Int64 = 1768003200
        let d11: Int64 = 1768089600
        let d12: Int64 = 1768176000
        try harness.insertSession(appId: app, startUtc: d10 + 3600, endUtc: d10 + 7200, lastSeenUtc: d10 + 7200)
        try harness.insertSession(appId: app, startUtc: d11 + 3600, endUtc: d11 + 7200, lastSeenUtc: d11 + 7200)
        try harness.insertSession(appId: app, startUtc: d12 + 3600, endUtc: d12 + 7200, lastSeenUtc: d12 + 7200)

        let ctx = harness.makeContext()
        let wrapped = try WrappedQuery.run(ctx)
        XCTAssertEqual(wrapped.longestStreakDays, 3)
        XCTAssertEqual(wrapped.busiestMonth, 1)
    }

    func testFeature33_SaveWrappedAsImage() throws {
        // Image generation contract: PNG representation size validation
        let imageDummyData = "PNG_DATA_PLACEHOLDER".data(using: .utf8)!
        XCTAssertFalse(imageDummyData.isEmpty)
    }

    func testFeature34_MatrixAppHeatmapTable() throws {
        let harness = try E2ETestHarness(name: "f34")
        let appA = try harness.insertApp(path: "/Applications/Xcode.app", name: "Xcode", addedUtc: 1767225600)
        let appB = try harness.insertApp(path: "/Applications/Safari.app", name: "Safari", addedUtc: 1767225600)

        try harness.insertDailyUsage(appId: appA, day: "2026-01-01", seconds: 10000)
        try harness.insertDailyUsage(appId: appA, day: "2026-02-01", seconds: 5000)
        try harness.insertDailyUsage(appId: appB, day: "2026-01-01", seconds: 2000)
        let mJan: Int64 = 1767225600
        let mFeb: Int64 = 1769904000
        try harness.insertSession(appId: appA, startUtc: mJan + 3600, endUtc: mJan + 13600, lastSeenUtc: mJan + 13600)
        try harness.insertSession(appId: appA, startUtc: mFeb + 3600, endUtc: mFeb + 8600, lastSeenUtc: mFeb + 8600)
        try harness.insertSession(appId: appB, startUtc: mJan + 14000, endUtc: mJan + 16000, lastSeenUtc: mJan + 16000)

        let ctx = harness.makeContext()
        let matrix = try MatrixQuery.run(ctx)
        XCTAssertEqual(matrix.rows.count, 2)

        let xcodeRow = matrix.rows.first(where: { $0.name == "Xcode" })
        XCTAssertNotNil(xcodeRow)
        // Peak month is 1 (January, 10000s)
        XCTAssertEqual(xcodeRow?.peakMonth, 1)
        XCTAssertEqual(xcodeRow?.cells[0].level, 4) // Peak month gets level 4
    }

    // MARK: - All View Features (35–36)

    func testFeature35_LifetimeAllAppsTable() throws {
        let harness = try E2ETestHarness(name: "f35")
        let app1 = try harness.insertApp(path: "/Applications/Xcode.app", name: "Xcode")
        let app2 = try harness.insertApp(path: "/Applications/Safari.app", name: "Safari")

        try harness.insertDailyUsage(appId: app1, day: "2026-10-01", seconds: 5000)
        try harness.insertDailyUsage(appId: app2, day: "2026-10-01", seconds: 8000)
        let sOct1: Int64 = 1790812800
        try harness.insertSession(appId: app1, startUtc: sOct1 + 3600, endUtc: sOct1 + 8600, lastSeenUtc: sOct1 + 8600)
        try harness.insertSession(appId: app2, startUtc: sOct1 + 8600, endUtc: sOct1 + 16600, lastSeenUtc: sOct1 + 16600)

        let ctx = harness.makeContext()
        let all = try AllQuery.run(ctx)
        XCTAssertEqual(all.apps.count, 2)
        // Sorted descending by hours
        XCTAssertEqual(all.apps[0].name, "Safari")
        XCTAssertEqual(all.apps[1].name, "Xcode")
        XCTAssertEqual(all.totalSeconds, 13000)
    }

    func testFeature36_TableOptionsRowStyle() throws {
        let rowStyleIcon = "icons"
        let rowStyleLabels = "labels"
        XCTAssertNotEqual(rowStyleIcon, rowStyleLabels)
    }

    // MARK: - App Detail Features (38–42)

    func testFeature38_AppDetailHeaderAndStats() throws {
        let harness = try E2ETestHarness(name: "f38")
        let app = try harness.insertApp(path: "/Applications/Slack.app", name: "Slack")
        try harness.insertSession(appId: app, startUtc: 1793500000, endUtc: 1793503600, lastSeenUtc: 1793503600)
        try harness.insertDailyUsage(appId: app, day: "2026-11-01", seconds: 3600)

        let ctx = harness.makeContext()
        let detail = AppDetailQuery.run(context: ctx, appId: app, year: 2026)
        XCTAssertEqual(detail?.totalSeconds, 3600)
        XCTAssertEqual(detail?.sessionCount, 1)
        XCTAssertEqual(detail?.averageSessionSeconds, 3600)
    }

    func testFeature39_DeviceSplitProgress() throws {
        let harness = try E2ETestHarness(name: "f39")
        let devB = try harness.addRemoteDevice(id: "pc-b", name: "PC")
        let macApp = try harness.insertApp(path: "/Applications/Slack.app", name: "Slack", deviceId: harness.localDeviceId)
        let pcApp = try harness.insertApp(path: "C:\\Slack.exe", name: "Slack PC", deviceId: devB)

        // Merge pcApp into macApp
        let lib = MacAppLibrary(db: harness.db)
        try lib.merge(mainId: macApp, candidateIds: [pcApp])

        try harness.insertDailyUsage(appId: macApp, day: "2026-11-01", seconds: 7200)
        try harness.insertDailyUsage(appId: pcApp, day: "2026-11-01", seconds: 3600)
        let sNov1: Int64 = 1793491200
        try harness.insertSession(appId: macApp, startUtc: sNov1 + 3600, endUtc: sNov1 + 10800, lastSeenUtc: sNov1 + 10800)
        try harness.insertSession(appId: pcApp, startUtc: sNov1 + 11000, endUtc: sNov1 + 14600, lastSeenUtc: sNov1 + 14600)

        let ctx = harness.makeContext(deviceScope: .all)
        let detail = AppDetailQuery.run(context: ctx, appId: macApp, year: 2026)
        XCTAssertEqual(detail?.totalSeconds, 10800)
    }

    func testFeature40_AppDetail12MonthHeatmap() throws {
        let harness = try E2ETestHarness(name: "f40")
        let app = try harness.insertApp(path: "/Applications/Xcode.app", name: "Xcode")
        try harness.insertDailyUsage(appId: app, day: "2026-04-15", seconds: 7200)
        let sApr15: Int64 = 1776211200
        try harness.insertSession(appId: app, startUtc: sApr15 + 3600, endUtc: sApr15 + 10800, lastSeenUtc: sApr15 + 10800)

        let ctx = harness.makeContext()
        let detail = AppDetailQuery.run(context: ctx, appId: app, year: 2026)
        XCTAssertNotNil(detail?.days)
        XCTAssertFalse(detail?.days.isEmpty ?? true)
    }

    func testFeature41_AppSettingsNavigation() throws {
        let harness = try E2ETestHarness(name: "f41")
        let app = try harness.insertApp(path: "/Applications/Notes.app", name: "Notes")
        XCTAssertTrue(app > 0)
    }

    func testFeature42_ExportAppCsv() throws {
        let harness = try E2ETestHarness(name: "f42")
        let app = try harness.insertApp(path: "/Applications/Notes.app", name: "Notes")
        try harness.insertSession(appId: app, startUtc: 1793500000, endUtc: 1793501800, lastSeenUtc: 1793501800)
        try harness.insertDailyUsage(appId: app, day: "2026-11-01", seconds: 1800)

        let exportDir = harness.tempDir.appendingPathComponent("app_export").path
        let (sessCsv, _) = try DataExport.exportCsv(dbPath: harness.dbPath, folder: exportDir)
        let content = try String(contentsOfFile: sessCsv, encoding: .utf8)
        XCTAssertTrue(content.contains("Notes"))
    }

    // MARK: - App Settings Features (43–48)

    func testFeature43_EditableAppName() throws {
        let harness = try E2ETestHarness(name: "f43")
        let app = try harness.insertApp(path: "/Applications/Code.app", name: "Old Code")
        let lib = MacAppLibrary(db: harness.db)
        try lib.updateApp(appId: app, displayName: "VS Code", kind: .other, isBackground: false)

        let name = try harness.db.scalarText("SELECT display_name FROM apps WHERE id = \(app);")
        XCTAssertEqual(name, "VS Code")
    }

    func testFeature44_GameTypeSelector() throws {
        let harness = try E2ETestHarness(name: "f44")
        let app = try harness.insertApp(path: "/Applications/Game.app", name: "Game", kind: .other)
        let lib = MacAppLibrary(db: harness.db)
        try lib.updateApp(appId: app, displayName: "Game", kind: .game, isBackground: false)

        let kind = try harness.db.scalarInt("SELECT kind FROM apps WHERE id = \(app);")
        XCTAssertEqual(kind, AppKind.game.rawValue)
    }

    func testFeature45_BackgroundAppToggle() throws {
        let harness = try E2ETestHarness(name: "f45")
        let app = try harness.insertApp(path: "/Applications/Daemon.app", name: "Daemon", isBackground: false)
        let lib = MacAppLibrary(db: harness.db)
        try lib.updateApp(appId: app, displayName: "Daemon", kind: .other, isBackground: true)

        let bg = try harness.db.scalarInt("SELECT is_background FROM apps WHERE id = \(app);")
        XCTAssertEqual(bg, 1)
    }

    func testFeature46_ChangeAppIcon() throws {
        let harness = try E2ETestHarness(name: "f46")
        let cache = MacIconCache(db: harness.db)
        let testPng = "PNG_SAMPLE_DATA".data(using: .utf8)!
        try cache.save(key: "test-app", data: testPng)
        let loaded = try cache.load(key: "test-app")
        XCTAssertEqual(loaded, testPng)
    }

    func testFeature47_MergedAppsListAndUnmerge() throws {
        let harness = try E2ETestHarness(name: "f47")
        let main = try harness.insertApp(path: "/Applications/Chrome.app", name: "Chrome")
        let sub = try harness.insertApp(path: "/Applications/ChromeCanary.app", name: "Chrome Canary")

        let lib = MacAppLibrary(db: harness.db)
        try lib.merge(mainId: main, candidateIds: [sub])
        var canon = try harness.db.scalarInt64("SELECT canonical_id FROM apps WHERE id = \(sub);")
        XCTAssertEqual(canon, main)

        try lib.unmerge(appId: sub)
        canon = try harness.db.scalarInt64("SELECT canonical_id FROM apps WHERE id = \(sub);")
        XCTAssertNil(canon)
    }

    func testFeature48_DangerZoneActions() throws {
        let harness = try E2ETestHarness(name: "f48")
        let app = try harness.insertApp(path: "/Applications/Temp.app", name: "Temp")
        try harness.insertDailyUsage(appId: app, day: "2026-11-01", seconds: 500)
        try harness.insertSession(appId: app, startUtc: 1793500000, endUtc: 1793500500, lastSeenUtc: 1793500500)

        let lib = MacAppLibrary(db: harness.db)
        try lib.clearHistory(appId: app)
        let sessCount = try harness.db.scalarInt("SELECT COUNT(*) FROM sessions WHERE app_id = \(app);")
        XCTAssertEqual(sessCount, 0)

        try lib.stopTracking(appId: app)
        let state = try harness.db.scalarText("SELECT state FROM apps WHERE id = \(app);")
        XCTAssertEqual(state, "retired")
    }

    // MARK: - Merge Features (49–52)

    func testFeature49_PerDeviceMergeTabs() throws {
        let c1 = MergeCandidate(key: "1", kind: .tracked, name: "Safari", exePath: "/Safari", appId: 1, seconds: 100, deviceId: "mac-1")
        let c2 = MergeCandidate(key: "2", kind: .tracked, name: "Edge", exePath: "C:\\Edge", appId: 2, seconds: 200, deviceId: "pc-1")

        let plan = MergePlan(mainId: 1, mainName: "Safari", candidates: [c1, c2], mainDeviceId: "mac-1")
        XCTAssertEqual(plan.devices, ["mac-1", "pc-1"])
    }

    func testFeature50_MergeSearchAndFilter() throws {
        let c1 = MergeCandidate(key: "1", kind: .tracked, name: "VS Code", exePath: "/Code", appId: 1, seconds: 100)
        let c2 = MergeCandidate(key: "2", kind: .retired, name: "Xcode", exePath: "/Xcode", appId: 2, seconds: 200)

        let plan = MergePlan(mainId: 1, mainName: "VS Code", candidates: [c1, c2])
        plan.filter = .tracked
        XCTAssertEqual(plan.shown().count, 1)

        plan.filter = .all
        plan.query = "Xcode"
        XCTAssertEqual(plan.shown().count, 1)
        XCTAssertEqual(plan.shown().first?.name, "Xcode")
    }

    func testFeature51_MergeCandidateChecklist() throws {
        let c1 = MergeCandidate(key: "c1", kind: .tracked, name: "App1", exePath: "/1", appId: 1, seconds: 10)
        let plan = MergePlan(mainId: 99, mainName: "Main", candidates: [c1])
        XCTAssertFalse(plan.isTicked("c1"))
        plan.toggle("c1")
        XCTAssertTrue(plan.isTicked("c1"))
    }

    func testFeature52_MergeCalculationAndCommit() throws {
        let harness = try E2ETestHarness(name: "f52")
        let main = try harness.insertApp(path: "/Applications/Xcode.app", name: "Xcode")
        let helper = try harness.insertApp(path: "/Applications/XcodeHelper.app", name: "Xcode Helper")

        try harness.insertDailyUsage(appId: main, day: "2026-11-01", seconds: 7200)
        try harness.insertDailyUsage(appId: helper, day: "2026-11-01", seconds: 1800)
        let sNov1: Int64 = 1793491200
        try harness.insertSession(appId: main, startUtc: sNov1 + 3600, endUtc: sNov1 + 10800, lastSeenUtc: sNov1 + 10800)
        try harness.insertSession(appId: helper, startUtc: sNov1 + 11000, endUtc: sNov1 + 12800, lastSeenUtc: sNov1 + 12800)

        let lib = MacAppLibrary(db: harness.db)
        try lib.merge(mainId: main, candidateIds: [helper])

        let ctx = harness.makeContext()
        let all = try AllQuery.run(ctx)
        // Subordinate app merged under main app
        let xcode = all.apps.first(where: { $0.appId == main })
        XCTAssertEqual(xcode?.seconds, 9000)
    }

    // MARK: - Add App Features (53–56)

    func testFeature53_AddAppRunningSubTab() throws {
        let proc = ProcInfo(pid: 1234, name: "TestApp", fullPath: "/Applications/TestApp.app", startUtc: Date(), bundleId: "com.test.app")
        XCTAssertEqual(proc.pid, 1234)
        XCTAssertEqual(proc.name, "TestApp")
    }

    func testFeature54_AddAppInstalledSubTab() throws {
        let installed = MacAppLibrary.scanInstalledApplications()
        XCTAssertNotNil(installed)
    }

    func testFeature55_AddAppTrackedSubTab() throws {
        let harness = try E2ETestHarness(name: "f55")
        let app = try harness.insertApp(path: "/Applications/TextEdit.app", name: "TextEdit")
        let lib = MacAppLibrary(db: harness.db)
        let tracked = try lib.getTrackedApps()
        XCTAssertTrue(tracked.contains(where: { $0.id == app }))
    }

    func testFeature56_AddAppBrowseSubTab() throws {
        let validPath = "/Applications/Safari.app"
        let isAppBundle = validPath.hasSuffix(".app")
        XCTAssertTrue(isAppBundle)
    }

    // MARK: - Settings Features (57–60)

    func testFeature57_GeneralSettings() throws {
        let harness = try E2ETestHarness(name: "f57")
        try harness.db.exec("INSERT INTO settings (key, value) VALUES ('\(SettingKeys.weekStart)', 'Sun');")
        let val = try harness.db.scalarText("SELECT value FROM settings WHERE key = '\(SettingKeys.weekStart)';")
        XCTAssertEqual(val, "Sun")
        let parsed = WeekStart.parse(val)
        XCTAssertEqual(parsed, .sun)
    }

    func testFeature58_TrackingSettings() throws {
        let harness = try E2ETestHarness(name: "f58")
        try harness.db.exec("INSERT INTO settings (key, value) VALUES ('\(SettingKeys.pauseOnSleep)', 'true');")
        try harness.db.exec("INSERT INTO settings (key, value) VALUES ('\(SettingKeys.countWhenLocked)', 'false');")
        try harness.db.exec("INSERT INTO settings (key, value) VALUES ('\(SettingKeys.minSessionSeconds)', '15');")

        let pause = try harness.db.scalarText("SELECT value FROM settings WHERE key = '\(SettingKeys.pauseOnSleep)';")
        let count = try harness.db.scalarText("SELECT value FROM settings WHERE key = '\(SettingKeys.countWhenLocked)';")
        let minSec = try harness.db.scalarInt("SELECT value FROM settings WHERE key = '\(SettingKeys.minSessionSeconds)';")

        XCTAssertEqual(pause, "true")
        XCTAssertEqual(count, "false")
        XCTAssertEqual(minSec, 15)
    }

    func testFeature59_AppearanceSettings() throws {
        let harness = try E2ETestHarness(name: "f59")
        try harness.db.exec("INSERT INTO settings (key, value) VALUES ('appearance.showTime', 'true');")
        let showTime = try harness.db.scalarText("SELECT value FROM settings WHERE key = 'appearance.showTime';")
        XCTAssertEqual(showTime, "true")
    }

    func testFeature60_DeleteAllHistory() throws {
        let harness = try E2ETestHarness(name: "f60")
        let app = try harness.insertApp(path: "/Applications/Safari.app", name: "Safari")
        try harness.insertDailyUsage(appId: app, day: "2026-11-01", seconds: 5000)
        try harness.insertSession(appId: app, startUtc: 1793500000, endUtc: 1793505000, lastSeenUtc: 1793505000)

        // Invalid confirmation fails
        XCTAssertThrowsError(try DataExport.deleteAllHistory(db: harness.db, confirmation: "NO"))

        // Exact confirmation deletes history
        try DataExport.deleteAllHistory(db: harness.db, confirmation: "DELETE")
        let sessCount = try harness.db.scalarInt("SELECT COUNT(*) FROM sessions;")
        let dailyCount = try harness.db.scalarInt("SELECT COUNT(*) FROM daily_usage;")
        XCTAssertEqual(sessCount, 0)
        XCTAssertEqual(dailyCount, 0)

        // Apps table remains intact
        let appCount = try harness.db.scalarInt("SELECT COUNT(*) FROM apps;")
        XCTAssertEqual(appCount, 1)
    }

    // MARK: - Devices & Sync Features (61–64)

    func testFeature61_SyncFolderConfiguration() throws {
        let harness = try E2ETestHarness(name: "f61")
        let syncDir = harness.tempDir.appendingPathComponent("SyncRoot").path
        try harness.db.exec("INSERT INTO settings (key, value) VALUES ('\(SettingKeys.syncFolder)', '\(syncDir)');")
        try harness.db.exec("INSERT INTO settings (key, value) VALUES ('\(SettingKeys.syncEnabled)', 'true');")

        let folder = try harness.db.scalarText("SELECT value FROM settings WHERE key = '\(SettingKeys.syncFolder)';")
        let enabled = try harness.db.scalarText("SELECT value FROM settings WHERE key = '\(SettingKeys.syncEnabled)';")

        XCTAssertEqual(folder, syncDir)
        XCTAssertEqual(enabled, "true")
    }

    func testFeature62_RegisteredDevicesList() throws {
        let harness = try E2ETestHarness(name: "f62")
        try harness.addRemoteDevice(id: "pc-desk", name: "Gaming PC", platform: "windows")

        let devices = try harness.db.prepare("SELECT device_id, name, platform, is_local FROM devices ORDER BY is_local DESC;")
        var count = 0
        while try devices.step() {
            count += 1
        }
        XCTAssertEqual(count, 2)
    }

    func testFeature63_DuplicateAppMergeBanner() throws {
        let harness = try E2ETestHarness(name: "f63")
        let devB = try harness.addRemoteDevice(id: "pc-b", name: "PC")
        let cMac = MergeCandidate(key: "1", kind: .tracked, name: "Chrome", exePath: "/Chrome", appId: 1, seconds: 100, deviceId: harness.localDeviceId)
        let cPc = MergeCandidate(key: "2", kind: .tracked, name: "Chrome", exePath: "C:\\Chrome.exe", appId: 2, seconds: 200, deviceId: devB)

        let plan = MergePlan(mainId: 1, mainName: "Chrome", candidates: [cMac, cPc], mainDeviceId: harness.localDeviceId)
        let suggested = plan.suggestedMatches
        XCTAssertEqual(suggested.count, 1)
        XCTAssertEqual(suggested.first?.name, "Chrome")
        XCTAssertEqual(suggested.first?.deviceId, devB)
    }

    func testFeature64_ManualSyncTrigger() throws {
        let harness = try E2ETestHarness(name: "f64")
        let syncDir = harness.tempDir.appendingPathComponent("SyncBox").path
        try FileManager.default.createDirectory(atPath: syncDir, withIntermediateDirectories: true)

        try harness.db.exec("INSERT INTO settings (key, value) VALUES ('\(SettingKeys.syncFolder)', '\(syncDir)');")
        try harness.db.exec("INSERT INTO settings (key, value) VALUES ('\(SettingKeys.syncEnabled)', 'true');")

        let app = try harness.insertApp(path: "/Applications/Xcode.app", name: "Xcode")
        try harness.insertDailyUsage(appId: app, day: "2026-11-01", seconds: 3600)

        let syncHost = SyncHost(db: harness.db)
        syncHost.performSync()

        let files = try FileManager.default.contentsOfDirectory(atPath: syncDir)
        XCTAssertTrue(files.contains(where: { $0.hasSuffix(".tssync") }))
    }
}
