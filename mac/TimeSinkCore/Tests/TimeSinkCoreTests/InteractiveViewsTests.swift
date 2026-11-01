import XCTest
import Foundation
@testable import TimeSinkCore

final class InteractiveViewsTests: XCTestCase {
    func testDayDetailQueryComputesAppsAndComparisons() throws {
        let harness = try E2ETestHarness(name: "interactive_day_detail")
        let appXcode = try harness.insertApp(path: "/Applications/Xcode.app", name: "Xcode")
        let appSafari = try harness.insertApp(path: "/Applications/Safari.app", name: "Safari")

        // 2026-10-28 (Wednesday) usage
        let sOct28: Int64 = 1793145600
        try harness.insertDailyUsage(appId: appXcode, day: "2026-10-28", seconds: 7200)
        try harness.insertDailyUsage(appId: appSafari, day: "2026-10-28", seconds: 3600)
        try harness.insertSession(appId: appXcode, startUtc: sOct28 + 3600, endUtc: sOct28 + 10800, lastSeenUtc: sOct28 + 10800)
        try harness.insertSession(appId: appSafari, startUtc: sOct28 + 10800, endUtc: sOct28 + 14400, lastSeenUtc: sOct28 + 14400)

        // Same day last week: 2026-10-21 (Wednesday) usage
        let sOct21: Int64 = 1792540800
        try harness.insertDailyUsage(appId: appXcode, day: "2026-10-21", seconds: 5400)
        try harness.insertSession(appId: appXcode, startUtc: sOct21 + 3600, endUtc: sOct21 + 9000, lastSeenUtc: sOct21 + 9000)

        let ctx = harness.makeContext()
        let detail = try DayDetailQuery.run(ctx, day: "2026-10-28")

        XCTAssertEqual(detail.date, "2026-10-28")
        XCTAssertEqual(detail.totalSeconds, 10800)
        XCTAssertEqual(detail.apps.count, 2)
        XCTAssertEqual(detail.apps.first(where: { $0.appId == appXcode })?.seconds, 7200)
        XCTAssertEqual(detail.apps.first(where: { $0.appId == appSafari })?.seconds, 3600)
        XCTAssertEqual(detail.sameDayLastWeekSeconds, 5400)
        XCTAssertEqual(detail.firstStartUtc, sOct28 + 3600)
        XCTAssertEqual(detail.lastEndUtc, sOct28 + 14400)
    }

    func testDayDetailComparisonWithWeekAverage() throws {
        let harness = try E2ETestHarness(name: "interactive_day_detail_week_avg")
        let appXcode = try harness.insertApp(path: "/Applications/Xcode.app", name: "Xcode")

        // 2026-10-28: 10 hours
        let sOct28: Int64 = 1793145600
        try harness.insertDailyUsage(appId: appXcode, day: "2026-10-28", seconds: 36000)
        try harness.insertSession(appId: appXcode, startUtc: sOct28 + 3600, endUtc: sOct28 + 39600, lastSeenUtc: sOct28 + 39600)

        // 2026-10-29: 2 hours
        let sOct29: Int64 = 1793232000
        try harness.insertDailyUsage(appId: appXcode, day: "2026-10-29", seconds: 7200)
        try harness.insertSession(appId: appXcode, startUtc: sOct29 + 3600, endUtc: sOct29 + 10800, lastSeenUtc: sOct29 + 10800)

        let ctx = harness.makeContext()
        let detail = try DayDetailQuery.run(ctx, day: "2026-10-28")

        XCTAssertEqual(detail.totalSeconds, 36000)
        XCTAssertTrue(detail.weekAverageSeconds > 0)
    }
}
