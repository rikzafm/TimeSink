import XCTest
import Foundation
@testable import TimeSinkCore

final class AllSortingTests: XCTestCase {
    func testAllSortCycleAndNaturalDirections() {
        var sort = AllSort.default
        XCTAssertEqual(sort.column, .hours)
        XCTAssertTrue(sort.descending)
        XCTAssertEqual(sort.arrow, "↓")

        sort.next()
        XCTAssertEqual(sort.column, .app)
        XCTAssertFalse(sort.descending, "App sort naturally defaults to ascending A-Z")
        XCTAssertEqual(sort.arrow, "↑")

        sort.next()
        XCTAssertEqual(sort.column, .avgSession)
        XCTAssertTrue(sort.descending)
        XCTAssertEqual(sort.arrow, "↓")

        sort.next()
        XCTAssertEqual(sort.column, .lastOpened)
        XCTAssertTrue(sort.descending)
        XCTAssertEqual(sort.arrow, "↓")

        sort.next()
        XCTAssertEqual(sort.column, .hours)
        XCTAssertTrue(sort.descending)
    }

    func testAllSortTap() {
        var sort = AllSort.default
        // Tapping same column flips direction
        sort.tap(.hours)
        XCTAssertEqual(sort.column, .hours)
        XCTAssertFalse(sort.descending)
        XCTAssertEqual(sort.arrow, "↑")

        // Tapping different column switches to it with natural direction
        sort.tap(.app)
        XCTAssertEqual(sort.column, .app)
        XCTAssertFalse(sort.descending)

        // Tapping app again flips to descending
        sort.tap(.app)
        XCTAssertEqual(sort.column, .app)
        XCTAssertTrue(sort.descending)

        sort.tap(.lastOpened)
        XCTAssertEqual(sort.column, .lastOpened)
        XCTAssertTrue(sort.descending)
    }

    func testAllSortingApply() {
        let rows = [
            AllAppRow(appId: 1, name: "Brave", seconds: 36000, daysUsed: 5, averageSecondsPerDay: 7200, share: 0.5, firstTracked: nil, bestMonth: nil, longestSessionSeconds: 3600, background: false, sessionCount: 5, lastUsedUtc: 1000),
            AllAppRow(appId: 2, name: "Antigravity", seconds: 18000, daysUsed: 2, averageSecondsPerDay: 9000, share: 0.25, firstTracked: nil, bestMonth: nil, longestSessionSeconds: 3600, background: false, sessionCount: 1, lastUsedUtc: 5000),
            AllAppRow(appId: 3, name: "Zen", seconds: 7200, daysUsed: 1, averageSecondsPerDay: 7200, share: 0.1, firstTracked: nil, bestMonth: nil, longestSessionSeconds: 1800, background: true, sessionCount: 2, lastUsedUtc: 2000),
            AllAppRow(appId: 4, name: "Empty", seconds: 0, daysUsed: 0, averageSecondsPerDay: 0, share: 0.0, firstTracked: nil, bestMonth: nil, longestSessionSeconds: 0, background: false, sessionCount: 0, lastUsedUtc: 0)
        ]

        // Default sort: seconds > 0 non-background first, then background, then 0-seconds
        let defaultSorted = AllSorting.apply(rows: rows, sort: .default)
        XCTAssertEqual(defaultSorted.map(\.name), ["Brave", "Antigravity", "Zen", "Empty"])

        // App A-Z
        let appSorted = AllSorting.apply(rows: rows, sort: AllSort(column: .app, descending: false))
        XCTAssertEqual(appSorted.map(\.name), ["Antigravity", "Brave", "Empty", "Zen"])

        // App Z-A
        let appDesc = AllSorting.apply(rows: rows, sort: AllSort(column: .app, descending: true))
        XCTAssertEqual(appDesc.map(\.name), ["Zen", "Empty", "Brave", "Antigravity"])

        // Average Session desc: Antigravity has 18000s / 1 session = 18000s; Brave has 36000s / 5 sessions = 7200s; Zen = 3600s
        let avgDesc = AllSorting.apply(rows: rows, sort: AllSort(column: .avgSession, descending: true))
        XCTAssertEqual(avgDesc.map(\.name), ["Antigravity", "Brave", "Zen", "Empty"])

        // Last Opened desc: Antigravity (5000) > Zen (2000) > Brave (1000) > Empty (0)
        let lastDesc = AllSorting.apply(rows: rows, sort: AllSort(column: .lastOpened, descending: true))
        XCTAssertEqual(lastDesc.map(\.name), ["Antigravity", "Zen", "Brave", "Empty"])
    }

    func testAllLayoutSetting() throws {
        let db = try SQLiteDatabase.inMemory()
        try db.exec("CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);")
        try db.exec("INSERT INTO settings (key, value) VALUES ('\(SettingKeys.allLayout)', 'icons') ON CONFLICT(key) DO UPDATE SET value = excluded.value;")
        let val = try db.scalarText("SELECT value FROM settings WHERE key = '\(SettingKeys.allLayout)';")
        XCTAssertEqual(val, "icons")
    }
}
