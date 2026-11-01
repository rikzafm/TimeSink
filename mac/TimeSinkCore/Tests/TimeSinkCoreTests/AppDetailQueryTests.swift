import XCTest
import Foundation
@testable import TimeSinkCore

final class AppDetailQueryTests: XCTestCase {
    func testLevelIsQuartersOfBusiestDay() {
        XCTAssertEqual(AppDetailQuery.level(seconds: 0, peak: 100), 0)
        XCTAssertEqual(AppDetailQuery.level(seconds: 1, peak: 100), 1)
        XCTAssertEqual(AppDetailQuery.level(seconds: 25, peak: 100), 1)
        XCTAssertEqual(AppDetailQuery.level(seconds: 26, peak: 100), 2)
        XCTAssertEqual(AppDetailQuery.level(seconds: 50, peak: 100), 2)
        XCTAssertEqual(AppDetailQuery.level(seconds: 51, peak: 100), 3)
        XCTAssertEqual(AppDetailQuery.level(seconds: 75, peak: 100), 3)
        XCTAssertEqual(AppDetailQuery.level(seconds: 76, peak: 100), 4)
        XCTAssertEqual(AppDetailQuery.level(seconds: 100, peak: 100), 4)
    }

    func testLevelIsZeroWithoutPeak() {
        XCTAssertEqual(AppDetailQuery.level(seconds: 50, peak: 0), 0)
    }

    func testPeriodNavigatorClampingAndStepping() {
        let nav = PeriodNavigator(unit: .month, first: "2026-01-15", today: "2026-06-20", weekStart: .mon)
        XCTAssertEqual(nav.clamp(day: "2025-12-01"), "2026-01-15")
        XCTAssertEqual(nav.clamp(day: "2026-03-10"), "2026-03-10")
        XCTAssertEqual(nav.clamp(day: "2026-07-01"), "2026-06-20")

        XCTAssertTrue(nav.contains(day: "2026-02-01"))
        XCTAssertFalse(nav.contains(day: "2025-12-31"))

        XCTAssertEqual(nav.startOf(day: "2026-03-15"), "2026-03-01")
        XCTAssertEqual(nav.step(anchor: "2026-03-15", dir: 1), "2026-04-15")
        XCTAssertEqual(nav.step(anchor: "2026-03-15", dir: -1), "2026-02-15")

        XCTAssertTrue(nav.canPrev(anchor: "2026-03-15"))
        XCTAssertFalse(nav.canPrev(anchor: "2026-01-10"))
        XCTAssertTrue(nav.canNext(anchor: "2026-03-15"))
        XCTAssertFalse(nav.canNext(anchor: "2026-06-25"))
    }

    func testAppDetailChartRangeCycling() {
        XCTAssertEqual(AppDetailChart.nextRange(.all), .year)
        XCTAssertEqual(AppDetailChart.nextRange(.year), .month)
        XCTAssertEqual(AppDetailChart.nextRange(.month), .week)
        XCTAssertEqual(AppDetailChart.nextRange(.week), .all)
    }
}

