import XCTest
@testable import TimeSinkCore

final class TodayBarAndDialTests: XCTestCase {
    func testBarDetailForHourSlicesAppsAndSessions() {
        let dayStart: Int64 = 1700006400 // 2023-11-15 00:00:00 UTC
        let dayEnd: Int64 = dayStart + 86400

        // App 1 has 2 sessions: one in hour 2 (1700007200 to 1700009000 = 1800s),
        // and one spanning hour 2 and 3 (1700010000 to 1700012000; in hour 2: 1700010000 to 1700010800 = 800s)
        let s1 = TodaySession(startUtc: dayStart + 7200, endUtc: dayStart + 9000, isOpen: false)
        let s2 = TodaySession(startUtc: dayStart + 10000, endUtc: dayStart + 12000, isOpen: false)
        let row1 = TodayAppRow(
            appId: 1,
            name: "Xcode",
            seconds: 3800,
            isRunning: false,
            runningSinceUtc: nil,
            lastClosedUtc: nil,
            sessions: [s1, s2],
            sevenDayAverageSeconds: 3000,
            sevenDayDays: 7
        )

        // App 2 has 1 session in hour 2 (1700008000 to 1700010000 = 2000s)
        let s3 = TodaySession(startUtc: dayStart + 8000, endUtc: dayStart + 10000, isOpen: false)
        let row2 = TodayAppRow(
            appId: 2,
            name: "Safari",
            seconds: 2000,
            isRunning: false,
            runningSinceUtc: nil,
            lastClosedUtc: nil,
            sessions: [s3],
            sevenDayAverageSeconds: 1500,
            sevenDayDays: 7
        )

        var hours: [HourBucket] = []
        for h in 0..<24 {
            let appSecs: Int64 = (h == 2) ? 4600 : (h == 3 ? 1200 : 0)
            let typ: Double = (h == 2) ? 3600 : 0
            hours.append(HourBucket(hour: h, appSeconds: appSecs, typicalAppSeconds: typ))
        }

        let result = TodayResult(
            date: "2023-11-14",
            dayStartUtc: dayStart,
            dayEndUtc: dayEnd,
            nowUtc: dayStart + 15000,
            totalSeconds: 5800,
            runningCount: 0,
            openSessionCount: 0,
            hours: hours,
            hasTypical: true,
            sleepBands: [],
            firstApp: nil,
            longestSession: nil,
            totalVsYesterdaySeconds: 0,
            rows: [row1, row2],
            uptimeSeconds: 5800,
            uptimeVsYesterdaySeconds: 0,
            firstTrackedDay: "2023-11-01",
            gamesOnly: false,
            deviceUptimes: nil,
            onBothSeconds: 0
        )

        let detail = BarDetail.forHour(result: result, hour: 2, zone: TimeZone(secondsFromGMT: 0)!)
        XCTAssertEqual(detail.hour, 2)
        XCTAssertEqual(detail.typicalAppSeconds, 3600)
        // Xcode: 1800 + 800 = 2600. Safari: 2000. Total = 4600
        XCTAssertEqual(detail.appSeconds, 4600)
        XCTAssertEqual(detail.apps.count, 2)
        XCTAssertEqual(detail.apps[0].name, "Xcode")
        XCTAssertEqual(detail.apps[0].seconds, 2600)
        XCTAssertEqual(detail.apps[1].name, "Safari")
        XCTAssertEqual(detail.apps[1].seconds, 2000)

        // Sessions in hour 2:
        // s1: 7200..9000 (1800s)
        // s3: 8000..10000 (2000s)
        // s2 clipped: 10000..10800 (800s)
        XCTAssertEqual(detail.sessions.count, 3)
        XCTAssertEqual(detail.sessions[0].startUtc, dayStart + 7200)
        XCTAssertEqual(detail.sessions[0].endUtc, dayStart + 9000)
        XCTAssertEqual(detail.sessions[1].startUtc, dayStart + 8000)
        XCTAssertEqual(detail.sessions[1].endUtc, dayStart + 10000)
        XCTAssertEqual(detail.sessions[2].startUtc, dayStart + 10000)
        XCTAssertEqual(detail.sessions[2].endUtc, dayStart + 10800)
    }

    func testRowDetailsSeparatesBlipsAndShown() {
        let sLong = TodaySession(startUtc: 1000, endUtc: 1300, isOpen: false) // 300s
        let sBlip1 = TodaySession(startUtc: 2000, endUtc: 2030, isOpen: false) // 30s
        let sBlip2 = TodaySession(startUtc: 3000, endUtc: 3059, isOpen: false) // 59s
        let sExactMin = TodaySession(startUtc: 4000, endUtc: 4060, isOpen: false) // 60s

        let row = TodayAppRow(
            appId: 10,
            name: "Mail",
            seconds: 300 + 30 + 59 + 60,
            isRunning: false,
            runningSinceUtc: nil,
            lastClosedUtc: nil,
            sessions: [sLong, sBlip1, sBlip2, sExactMin],
            sevenDayAverageSeconds: 7200,
            sevenDayDays: 7
        )

        let detail = RowDetails.of(row)
        XCTAssertEqual(detail.shown.count, 2)
        XCTAssertEqual(detail.blips, 2)
        XCTAssertEqual(detail.longestSeconds, 300)
        XCTAssertEqual(detail.averageSeconds, 7200)
        XCTAssertEqual(detail.totalFraction, Double(row.seconds) / 86400.0, accuracy: 1e-6)
        XCTAssertEqual(detail.averageFraction ?? 0, 7200.0 / 86400.0, accuracy: 1e-6)
    }

    func testTodayResultDayFractions() {
        let dayStart: Int64 = 1700000000
        let dayEnd: Int64 = dayStart + 86400

        let result = TodayResult(
            date: "2023-11-14",
            dayStartUtc: dayStart,
            dayEndUtc: dayEnd,
            nowUtc: dayStart + 43200,
            totalSeconds: 0,
            runningCount: 0,
            openSessionCount: 0,
            hours: [],
            hasTypical: false,
            sleepBands: [],
            firstApp: nil,
            longestSession: nil,
            totalVsYesterdaySeconds: 0,
            rows: [],
            uptimeSeconds: 0,
            uptimeVsYesterdaySeconds: 0,
            firstTrackedDay: nil,
            gamesOnly: false,
            deviceUptimes: nil,
            onBothSeconds: 0
        )

        // Exactly halfway
        XCTAssertEqual(result.dayFraction(dayStart + 43200), 0.5, accuracy: 1e-6)
        // Before start clamps to 0
        XCTAssertEqual(result.dayFraction(dayStart - 100), 0.0)
        // After end clamps to 1
        XCTAssertEqual(result.dayFraction(dayEnd + 100), 1.0)

        // Carried over session: e.g. started 1 hour before midnight (at 23:00)
        // 1.0 + (-3600 / 86400) = 23 / 24
        XCTAssertEqual(result.dayFractionBefore(dayStart - 3600), 23.0 / 24.0, accuracy: 1e-6)
        // Started 2 days ago clamps to 0
        XCTAssertEqual(result.dayFractionBefore(dayStart - 200000), 0.0)
    }

    func testTodayLiveCalculations() {
        let dayStart: Int64 = 1700000000
        let dayEnd: Int64 = dayStart + 86400
        let snapshotNow: Int64 = dayStart + 1000

        let runningApp = TodayAppRow(
            appId: 1,
            name: "Antigravity",
            seconds: 500,
            isRunning: true,
            runningSinceUtc: snapshotNow - 500,
            lastClosedUtc: nil,
            sessions: [],
            sevenDayAverageSeconds: nil,
            sevenDayDays: 0
        )
        let stoppedApp = TodayAppRow(
            appId: 2,
            name: "Notes",
            seconds: 300,
            isRunning: false,
            runningSinceUtc: nil,
            lastClosedUtc: snapshotNow - 100,
            sessions: [],
            sevenDayAverageSeconds: nil,
            sevenDayDays: 0
        )

        let result = TodayResult(
            date: "2023-11-14",
            dayStartUtc: dayStart,
            dayEndUtc: dayEnd,
            nowUtc: snapshotNow,
            totalSeconds: 800,
            runningCount: 1,
            openSessionCount: 1,
            hours: [],
            hasTypical: false,
            sleepBands: [],
            firstApp: nil,
            longestSession: nil,
            totalVsYesterdaySeconds: 0,
            rows: [runningApp, stoppedApp],
            uptimeSeconds: 1000,
            uptimeVsYesterdaySeconds: 0,
            firstTrackedDay: nil,
            gamesOnly: false,
            deviceUptimes: nil,
            onBothSeconds: 0
        )

        let liveNow = snapshotNow + 52 // 52 seconds elapsed
        XCTAssertEqual(result.elapsedAt(liveNow), 52)
        XCTAssertEqual(result.uptimeAt(liveNow, live: true), 1052)
        XCTAssertEqual(result.uptimeAt(liveNow, live: false), 1000) // past day is static
        XCTAssertEqual(result.rowSecondsAt(runningApp, nowUtc: liveNow), 552) // running app ticks
        XCTAssertEqual(result.rowSecondsAt(stoppedApp, nowUtc: liveNow), 300) // stopped app stays static
        XCTAssertEqual(result.combinedSecondsAt(liveNow), 852)
    }
}
