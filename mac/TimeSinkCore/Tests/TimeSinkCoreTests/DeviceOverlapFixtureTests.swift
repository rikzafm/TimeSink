import XCTest
import Foundation
@testable import TimeSinkCore

// MARK: - Fixture Models

struct DeviceOverlapDeviceItem: Codable {
    let id: String
    let name: String
    let platform: String
    let isLocal: Bool
    let state: String
}

struct DeviceOverlapAppItem: Codable {
    let id: Int64
    let deviceId: String
    let name: String
    let path: String
    let bundleId: String?
    let addedUtc: Int64
}

struct DeviceOverlapSessionItem: Codable {
    let appId: Int64
    let startUtc: Int64
    let endUtc: Int64?
    let lastSeenUtc: Int64
    let endReason: String?
}

struct DeviceOverlapExpected: Codable {
    let headlineUptime: Int64
    let deviceUptimes: [String: Int64]
    let onBothSeconds: Int64
}

struct DeviceOverlapCase: Codable {
    let name: String
    let description: String
    let timeZoneId: String
    let nowUtc: Int64
    let deviceScope: String
    let forgottenDeviceId: String?
    let sessions: [DeviceOverlapSessionItem]
    let expected: DeviceOverlapExpected
}

struct DeviceOverlapDoc: Codable {
    let name: String
    let description: String
    let devices: [DeviceOverlapDeviceItem]
    let apps: [DeviceOverlapAppItem]
    let cases: [DeviceOverlapCase]
}

// MARK: - Tests

final class DeviceOverlapFixtureTests: XCTestCase {
    private static func findFixturePath(filename: String) -> URL {
        var current = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
        for _ in 0..<10 {
            let candidate = current.appendingPathComponent("shared/fixtures/\(filename)")
            if FileManager.default.fileExists(atPath: candidate.path) {
                return candidate
            }
            current = current.deletingLastPathComponent()
        }
        fatalError("Fixture file '\(filename)' was not found.")
    }

    private static func loadDoc() throws -> DeviceOverlapDoc {
        let path = findFixturePath(filename: "device-overlap.json")
        let data = try Data(contentsOf: path)
        return try JSONDecoder().decode(DeviceOverlapDoc.self, from: data)
    }

    private func createTestDb(doc: DeviceOverlapDoc, case c: DeviceOverlapCase) throws -> (dbPath: String, cleanup: () -> Void) {
        let dir = URL(fileURLWithPath: NSTemporaryDirectory()).appendingPathComponent("ts-dev-overlap-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let dbPath = dir.appendingPathComponent("timesink.db").path

        let db = try SQLiteDatabase(path: dbPath)
        try Migrator.apply(to: db)

        for dev in doc.devices {
            let state = (dev.id == c.forgottenDeviceId) ? "forgotten" : dev.state
            let stmt = try db.prepare("""
                INSERT INTO devices (device_id, name, platform, is_local, first_seen_utc, state)
                VALUES (?1, ?2, ?3, ?4, ?5, ?6)
                ON CONFLICT(device_id) DO UPDATE SET is_local = ?4, state = ?6;
            """)
            try stmt.bind(text: dev.id, at: 1)
            try stmt.bind(text: dev.name, at: 2)
            try stmt.bind(text: dev.platform, at: 3)
            try stmt.bind(int32: dev.isLocal ? 1 : 0, at: 4)
            try stmt.bind(text: ISO8601DateFormatter().string(from: Date()), at: 5)
            try stmt.bind(text: state, at: 6)
            _ = try stmt.step()

            if dev.isLocal {
                let s1 = try db.prepare("INSERT INTO settings (key, value) VALUES ('device.id', ?1) ON CONFLICT(key) DO UPDATE SET value = ?1;")
                try s1.bind(text: dev.id, at: 1)
                _ = try s1.step()

                let s2 = try db.prepare("INSERT INTO settings (key, value) VALUES ('device.name', ?1) ON CONFLICT(key) DO UPDATE SET value = ?1;")
                try s2.bind(text: dev.name, at: 1)
                _ = try s2.step()
            }
        }

        for app in doc.apps {
            let stmt = try db.prepare("""
                INSERT INTO apps (id, device_id, exe_path, bundle_id, display_name, added_utc, state)
                VALUES (?1, ?2, ?3, ?4, ?5, ?6, 'tracked');
            """)
            try stmt.bind(int64: app.id, at: 1)
            try stmt.bind(text: app.deviceId, at: 2)
            try stmt.bind(text: app.path, at: 3)
            if let bundleId = app.bundleId {
                try stmt.bind(text: bundleId, at: 4)
            } else {
                try stmt.bindNull(at: 4)
            }
            try stmt.bind(text: app.name, at: 5)
            try stmt.bind(int64: app.addedUtc, at: 6)
            _ = try stmt.step()
        }

        for s in c.sessions {
            let stmt = try db.prepare("""
                INSERT INTO sessions (app_id, start_utc, end_utc, last_seen_utc, end_reason)
                VALUES (?1, ?2, ?3, ?4, ?5);
            """)
            try stmt.bind(int64: s.appId, at: 1)
            try stmt.bind(int64: s.startUtc, at: 2)
            if let end = s.endUtc {
                try stmt.bind(int64: end, at: 3)
            } else {
                try stmt.bindNull(at: 3)
            }
            try stmt.bind(int64: s.lastSeenUtc, at: 4)
            if let reason = s.endReason {
                try stmt.bind(text: reason, at: 5)
            } else {
                try stmt.bindNull(at: 5)
            }
            _ = try stmt.step()
        }

        db.close()

        let cleanup: () -> Void = {
            _ = try? FileManager.default.removeItem(at: dir)
        }
        return (dbPath, cleanup)
    }

    func testVerifyAllCasesFromDeviceOverlapFixture() throws {
        let doc = try Self.loadDoc()
        XCTAssertEqual(doc.cases.count, 5)

        for c in doc.cases {
            let (dbPath, cleanup) = try createTestDb(doc: doc, case: c)
            defer { cleanup() }

            let zone = TimeZone(identifier: c.timeZoneId) ?? TimeZone(secondsFromGMT: 0)!
            let now = Date(timeIntervalSince1970: TimeInterval(c.nowUtc))
            let scope: DeviceScope = (c.deviceScope == "This") ? .this : .all

            let ctx = UsageContext(
                now: now,
                zone: zone,
                running: nil,
                signInUtc: nil,
                weekStart: .mon,
                gamesOnly: false,
                dbPath: dbPath,
                deviceScope: scope,
                localDeviceId: "dev-win"
            )

            let todayResult = try TodayQuery.run(ctx)

            // 1. Headline Uptime matches expected
            XCTAssertEqual(todayResult.uptimeSeconds, c.expected.headlineUptime, "case '\(c.name)' headline uptime mismatch")

            // 2. On both seconds matches expected
            XCTAssertEqual(todayResult.onBothSeconds, c.expected.onBothSeconds, "case '\(c.name)' onBoth mismatch")

            // 3. Headline equation holds: headline = devA + devB - onBoth
            if let devUptimes = todayResult.deviceUptimes, !devUptimes.isEmpty {
                let sumDev = devUptimes.values.reduce(0) { $0 + $1.uptimeSeconds }
                XCTAssertEqual(todayResult.uptimeSeconds, sumDev - todayResult.onBothSeconds, "case '\(c.name)' headline must equal sum(devices) - onBoth")

                for (devId, expectedSecs) in c.expected.deviceUptimes {
                    XCTAssertNotNil(devUptimes[devId], "case '\(c.name)' missing device \(devId)")
                    XCTAssertEqual(devUptimes[devId]?.uptimeSeconds, expectedSecs, "case '\(c.name)' device \(devId) uptime mismatch")
                }
            }
        }
    }

    func testHeadlineReconcilesWithDeviceFiguresAcrossEveryQueryResult() throws {
        let doc = try Self.loadDoc()
        guard let overlapCase = doc.cases.first(where: { $0.name == "overlap-counted-once" }) else {
            XCTFail("Missing overlap-counted-once case")
            return
        }

        let (dbPath, cleanup) = try createTestDb(doc: doc, case: overlapCase)
        defer { cleanup() }

        let zone = TimeZone(secondsFromGMT: 0)!
        let now = Date(timeIntervalSince1970: TimeInterval(overlapCase.nowUtc))
        let ctx = UsageContext(
            now: now,
            zone: zone,
            running: nil,
            signInUtc: nil,
            weekStart: .mon,
            gamesOnly: false,
            dbPath: dbPath,
            deviceScope: .all,
            localDeviceId: "dev-win"
        )

        // Today
        let today = try TodayQuery.run(ctx)
        XCTAssertEqual(today.uptimeSeconds, 21600)
        XCTAssertEqual(today.onBothSeconds, 7200)
        XCTAssertNotNil(today.deviceUptimes)
        let todaySum = today.deviceUptimes!.values.reduce(0) { $0 + $1.uptimeSeconds }
        XCTAssertEqual(today.uptimeSeconds, todaySum - today.onBothSeconds)

        // Week
        let week = try WeekQuery.run(ctx)
        XCTAssertEqual(week.totalSeconds, 21600)
        XCTAssertEqual(week.onBothSeconds, 7200)
        XCTAssertNotNil(week.deviceTotals)
        let weekSum = week.deviceTotals!.values.reduce(0) { $0 + $1 }
        XCTAssertEqual(week.totalSeconds, weekSum - week.onBothSeconds)

        // Month
        let month = try MonthQuery.run(ctx, year: 2026, month: 3)
        XCTAssertEqual(month.totalSeconds, 21600)
        XCTAssertEqual(month.onBothSeconds, 7200)
        XCTAssertNotNil(month.deviceTotals)
        let monthSum = month.deviceTotals!.values.reduce(0) { $0 + $1 }
        XCTAssertEqual(month.totalSeconds, monthSum - month.onBothSeconds)

        // Year
        let year = try YearQuery.run(ctx)
        XCTAssertEqual(year.totalSeconds, 21600)
        XCTAssertEqual(year.onBothSeconds, 7200)
        XCTAssertNotNil(year.deviceTotals)
        let yearSum = year.deviceTotals!.values.reduce(0) { $0 + $1 }
        XCTAssertEqual(year.totalSeconds, yearSum - year.onBothSeconds)

        // All
        let all = try AllQuery.run(ctx)
        XCTAssertEqual(all.totals.seconds, 21600)
        XCTAssertEqual(all.totals.onBothSeconds, 7200)
        XCTAssertNotNil(all.totals.deviceTotals)
        let allSum = all.totals.deviceTotals!.values.reduce(0) { $0 + $1 }
        XCTAssertEqual(all.totals.seconds, allSum - all.totals.onBothSeconds)
        XCTAssertTrue(all.apps.contains(where: { $0.name == "VS Code Win" && ($0.devices?.contains("dev-win") ?? false) }))
        XCTAssertTrue(all.apps.contains(where: { $0.name == "VS Code Mac" && ($0.devices?.contains("dev-mac") ?? false) }))

        // AppDetail
        let appWin = AppDetailQuery.run(context: ctx, appId: 1, year: 2026)
        XCTAssertNotNil(appWin)
        XCTAssertEqual(appWin?.totalSeconds, 14400)
        XCTAssertEqual(appWin?.deviceSeconds?["dev-win"], 14400)

        let appMac = AppDetailQuery.run(context: ctx, appId: 2, year: 2026)
        XCTAssertNotNil(appMac)
        XCTAssertEqual(appMac?.totalSeconds, 14400)
        XCTAssertEqual(appMac?.deviceSeconds?["dev-mac"], 14400)

        // Wrapped
        let wrapped = try WrappedQuery.run(ctx)
        XCTAssertEqual(wrapped.totalSeconds, 21600)
        XCTAssertEqual(wrapped.onBothSeconds, 7200)
        XCTAssertNotNil(wrapped.deviceTotals)
        let wrappedSum = wrapped.deviceTotals!.values.reduce(0) { $0 + $1 }
        XCTAssertEqual(wrapped.totalSeconds, wrappedSum - wrapped.onBothSeconds)
        XCTAssertNotNil(wrapped.pcTop)
        XCTAssertFalse(wrapped.pcTop!.podium.isEmpty)
        XCTAssertNotNil(wrapped.macTop)
        XCTAssertFalse(wrapped.macTop!.podium.isEmpty)
        XCTAssertNotNil(wrapped.deviceTops)
        XCTAssertNotNil(wrapped.deviceTops?["dev-win"])
        XCTAssertNotNil(wrapped.deviceTops?["dev-mac"])
    }

    func testForgottenDeviceShowsInAllScopeAndHiddenInThisScope() throws {
        let doc = try Self.loadDoc()
        guard let forgottenCase = doc.cases.first(where: { $0.name == "forgotten-device" }) else {
            XCTFail("Missing forgotten-device case")
            return
        }

        let (dbPath, cleanup) = try createTestDb(doc: doc, case: forgottenCase)
        defer { cleanup() }

        let zone = TimeZone(secondsFromGMT: 0)!
        let now = Date(timeIntervalSince1970: TimeInterval(forgottenCase.nowUtc))

        // In All Scope: Both devices count
        let allCtx = UsageContext(
            now: now,
            zone: zone,
            running: nil,
            signInUtc: nil,
            weekStart: .mon,
            gamesOnly: false,
            dbPath: dbPath,
            deviceScope: .all,
            localDeviceId: "dev-win"
        )

        let allResult = try TodayQuery.run(allCtx)
        XCTAssertEqual(allResult.uptimeSeconds, 18000)
        XCTAssertEqual(allResult.onBothSeconds, 3600)
        XCTAssertNotNil(allResult.deviceUptimes?["dev-win"])
        XCTAssertNotNil(allResult.deviceUptimes?["dev-mac"])
        let winUptime = allResult.deviceUptimes!["dev-win"]!.uptimeSeconds
        let macUptime = allResult.deviceUptimes!["dev-mac"]!.uptimeSeconds
        XCTAssertEqual(allResult.uptimeSeconds, winUptime + macUptime - allResult.onBothSeconds)

        // In This Scope: Only local device (dev-win) counts
        let thisCtx = UsageContext(
            now: now,
            zone: zone,
            running: nil,
            signInUtc: nil,
            weekStart: .mon,
            gamesOnly: false,
            dbPath: dbPath,
            deviceScope: .this,
            localDeviceId: "dev-win"
        )

        let thisResult = try TodayQuery.run(thisCtx)
        XCTAssertEqual(thisResult.uptimeSeconds, 10800)
        XCTAssertEqual(thisResult.onBothSeconds, 0)
        XCTAssertNotNil(thisResult.deviceUptimes?["dev-win"])
        XCTAssertNil(thisResult.deviceUptimes?["dev-mac"])
    }
}
