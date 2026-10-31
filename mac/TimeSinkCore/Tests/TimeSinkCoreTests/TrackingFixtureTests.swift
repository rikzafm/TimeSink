import XCTest
import Foundation
@testable import TimeSinkCore

// MARK: - Fixture Models

struct FixtureSettings: Codable {
    let minSessionSeconds: Int?
    let pauseOnSleep: Bool?
    let countWhenLocked: Bool?
}

struct FixtureApp: Codable {
    let id: Int64
    let name: String
    let path: String
    let bundleId: String?
    let addedUtc: Int64
}

struct FixtureEvent: Codable {
    let type: String
    let offset: Int?
    let from: Int?
    let to: Int?
    let step: Int?
    let pid: Int32?
    let appId: Int64?
    let procStartOffset: Int?
}

struct FixtureExpectedSession: Codable {
    let appId: Int64?
    let startUtc: Int64?
    let endUtc: Int64?
    let endReason: String?
    let pid: Int32?
    let procStartUtc: Int64?
}

struct FixtureCase: Codable {
    let name: String
    let description: String?
    let timeZoneId: String?
    let baseTimeUtc: String?
    let settings: FixtureSettings?
    let events: [FixtureEvent]?
    let expectedSessions: [FixtureExpectedSession]?
    let expectedDailyUsage: [String: Int64]?
}

struct TrackingFixtureDocument: Codable {
    let name: String
    let description: String?
    let timeZoneId: String?
    let baseTimeUtc: String?
    let settings: FixtureSettings?
    let apps: [FixtureApp]?
    let cases: [FixtureCase]
}

struct AccumulatorSession: Codable {
    let id: Int64
    let appId: Int64
    let startUtc: Int64
    let endUtc: Int64
    let endReason: String?
}

struct AccumulatorDailyUsage: Codable {
    let appId: Int64
    let day: String
    let seconds: Int64
}

struct AccumulatorRebuildDocument: Codable {
    let name: String
    let description: String?
    let timeZoneId: String?
    let baseTimeUtc: String?
    let apps: [FixtureApp]
    let sessions: [AccumulatorSession]
    let expectedDailyUsage: [AccumulatorDailyUsage]
}

// MARK: - Tests

final class TrackingFixtureTests: XCTestCase {
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

    private static func loadFixture<T: Decodable>(_ filename: String) throws -> T {
        let path = findFixturePath(filename: filename)
        let data = try Data(contentsOf: path)
        let decoder = JSONDecoder()
        return try decoder.decode(T.self, from: data)
    }

    private func parseIsoDate(_ str: String) -> Date {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let d = formatter.date(from: str) { return d }
        formatter.formatOptions = [.withInternetDateTime]
        return formatter.date(from: str) ?? Date(timeIntervalSince1970: 0)
    }

    private func runTrackingCase(doc: TrackingFixtureDocument, case c: FixtureCase) throws {
        let tzId = c.timeZoneId ?? doc.timeZoneId ?? "UTC"
        let tz = TimeZone(identifier: tzId) ?? TimeZone(secondsFromGMT: 0)!
        let baseTimeStr = c.baseTimeUtc ?? doc.baseTimeUtc ?? "2026-03-10T12:00:00Z"
        let baseTime = parseIsoDate(baseTimeStr)

        let rig = try TrackingTestRig(timeZone: tz, baseTime: baseTime)

        let minSec = c.settings?.minSessionSeconds ?? doc.settings?.minSessionSeconds ?? 15
        rig.settings.minSessionSeconds = minSec
        rig.settings.pauseOnSleep = c.settings?.pauseOnSleep ?? doc.settings?.pauseOnSleep ?? true
        rig.settings.countWhenLocked = c.settings?.countWhenLocked ?? doc.settings?.countWhenLocked ?? true

        // Map apps
        var appMap: [Int64: Int64] = [:]
        if let apps = doc.apps {
            for app in apps {
                if app.path.caseInsensitiveCompare(TrackingTestRig.notepadPath) == .orderedSame {
                    appMap[app.id] = rig.defaultAppId
                } else {
                    let id = try rig.insertApp(path: app.path, name: app.name, bundleId: app.bundleId, addedUtc: app.addedUtc)
                    appMap[app.id] = id
                }
            }
        }

        if let events = c.events {
            for evt in events {
                switch evt.type {
                case "proc_start":
                    let pid = evt.pid ?? 1
                    let app = doc.apps?.first(where: { $0.id == evt.appId })
                    let path = app?.path ?? TrackingTestRig.notepadPath
                    let name = app?.name ?? "notepad.exe"
                    let bundle = app?.bundleId ?? "com.microsoft.Notepad"
                    let startOff = evt.procStartOffset ?? evt.offset ?? 0

                    rig.procs.running.append(ProcInfo(
                        pid: pid,
                        name: name,
                        fullPath: path,
                        startUtc: rig.atTime(startOff),
                        bundleId: bundle
                    ))

                case "proc_exit":
                    let pid = evt.pid ?? 1
                    rig.procs.running.removeAll { $0.pid == pid }

                case "tick":
                    let off = evt.offset ?? 0
                    try rig.tick(off)

                case "tick_range":
                    let from = evt.from ?? 0
                    let to = evt.to ?? 0
                    let step = (evt.step ?? 0) > 0 ? evt.step! : 5
                    for s in stride(from: from, through: to, by: step) {
                        try rig.tick(s)
                    }

                case "clock_set":
                    let off = evt.offset ?? 0
                    rig.clock.utcNow = rig.atTime(off)

                case "suspend":
                    let off = evt.offset ?? 0
                    rig.power.raiseSuspend(rig.atTime(off))

                case "resume":
                    let off = evt.offset ?? 0
                    rig.power.raiseResume(rig.atTime(off))

                case "lock":
                    let off = evt.offset ?? 0
                    rig.power.raiseLock(rig.atTime(off))

                case "unlock":
                    let off = evt.offset ?? 0
                    rig.power.raiseUnlock(rig.atTime(off))

                case "shutdown":
                    let off = evt.offset ?? 0
                    rig.power.raiseShutdown(rig.atTime(off))

                case "crash_and_restart":
                    let off = evt.offset ?? 0
                    rig.clock.utcNow = rig.atTime(off)
                    _ = try rig.newEngine()

                case "recover_crash":
                    let off = evt.offset ?? 0
                    try rig.engine.recoverFromCrash(rig.atTime(off))

                default:
                    XCTFail("Unknown event type \(evt.type) in case \(c.name)")
                }
            }
        }

        // Assert sessions
        if let expSessions = c.expectedSessions {
            let actual = try rig.getAllSessions()
            XCTAssertEqual(actual.count, expSessions.count, "Session count mismatch in case '\(c.name)'")

            for (i, exp) in expSessions.enumerated() {
                guard i < actual.count else { break }
                let act = actual[i]
                if let expAppId = exp.appId {
                    let mapped = appMap[expAppId] ?? expAppId
                    XCTAssertEqual(act.appId, mapped, "appId mismatch at index \(i) in '\(c.name)'")
                }
                if let expStart = exp.startUtc {
                    XCTAssertEqual(act.startUtc, expStart, "startUtc mismatch at index \(i) in '\(c.name)'")
                }
                if let expEnd = exp.endUtc {
                    XCTAssertEqual(act.endUtc, expEnd, "endUtc mismatch at index \(i) in '\(c.name)'")
                }
                if let expReason = exp.endReason {
                    XCTAssertEqual(act.endReason, expReason, "endReason mismatch at index \(i) in '\(c.name)'")
                }
                if let expPid = exp.pid {
                    XCTAssertEqual(act.pid, expPid, "pid mismatch at index \(i) in '\(c.name)'")
                }
                if let expProcStart = exp.procStartUtc {
                    XCTAssertEqual(act.procStartUtc, expProcStart, "procStartUtc mismatch at index \(i) in '\(c.name)'")
                }
            }
        }

        // Assert daily usage
        if let expDaily = c.expectedDailyUsage {
            let actual = try rig.dailyUsageMap()
            XCTAssertEqual(actual, expDaily, "Daily usage mismatch in case '\(c.name)'")
        }
    }

    // MARK: - Single Session and Multi-process Fixtures

    func testSingleSessionFixtures() throws {
        let doc: TrackingFixtureDocument = try Self.loadFixture("single-session.json")
        for c in doc.cases {
            try runTrackingCase(doc: doc, case: c)
        }
    }

    // MARK: - Midnight and DST Fixtures

    func testMidnightAndDstFixtures() throws {
        let doc: TrackingFixtureDocument = try Self.loadFixture("midnight-and-dst.json")
        for c in doc.cases {
            try runTrackingCase(doc: doc, case: c)
        }
    }

    // MARK: - Suspend, Resume and Power Fixtures

    func testSuspendResumeAndPowerFixtures() throws {
        let doc: TrackingFixtureDocument = try Self.loadFixture("suspend-resume-and-power.json")
        for c in doc.cases {
            try runTrackingCase(doc: doc, case: c)
        }
    }

    // MARK: - Lock and Unlock Fixtures

    func testLockAndUnlockFixtures() throws {
        let doc: TrackingFixtureDocument = try Self.loadFixture("lock-and-unlock.json")
        for c in doc.cases {
            try runTrackingCase(doc: doc, case: c)
        }
    }

    // MARK: - Crash Recovery Fixtures

    func testCrashRecoveryFixtures() throws {
        let doc: TrackingFixtureDocument = try Self.loadFixture("crash-recovery.json")
        for c in doc.cases {
            try runTrackingCase(doc: doc, case: c)
        }
    }

    // MARK: - Min Session and Clock Moving Backwards Fixtures

    func testMinSessionAndClockFixtures() throws {
        let doc: TrackingFixtureDocument = try Self.loadFixture("min-session-and-clock.json")
        for c in doc.cases {
            try runTrackingCase(doc: doc, case: c)
        }
    }

    // MARK: - Accumulator Rebuild Fixture

    func testAccumulatorRebuildFixture() throws {
        let doc: AccumulatorRebuildDocument = try Self.loadFixture("accumulator-rebuild.json")
        let tz = TimeZone(identifier: doc.timeZoneId ?? "UTC") ?? TimeZone(secondsFromGMT: 0)!
        let baseTime = parseIsoDate(doc.baseTimeUtc ?? "2026-03-10T00:00:00Z")

        let rig = try TrackingTestRig(timeZone: tz, baseTime: baseTime)

        var appMap: [Int64: Int64] = [:]
        for app in doc.apps {
            if app.path.caseInsensitiveCompare(TrackingTestRig.notepadPath) == .orderedSame {
                appMap[app.id] = rig.defaultAppId
            } else {
                let id = try rig.insertApp(path: app.path, name: app.name, bundleId: app.bundleId, addedUtc: app.addedUtc)
                appMap[app.id] = id
            }
        }

        for s in doc.sessions {
            guard let mappedAppId = appMap[s.appId] else {
                XCTFail("Missing app mapping for \(s.appId)")
                return
            }
            let rec = try rig.store.openSession(appId: mappedAppId, startUtc: s.startUtc, pid: 1, procStartUtc: s.startUtc)
            try rig.usage.add(appId: mappedAppId, fromUtc: s.startUtc, toUtc: s.endUtc)
            try rig.store.closeSession(sessionId: rec.id, endUtc: s.endUtc, reason: s.endReason ?? "exit")
        }

        // Verify incremental equals expected
        let incrementalDaily = try rig.store.getDailyUsage()
        for exp in doc.expectedDailyUsage {
            guard let mappedAppId = appMap[exp.appId] else {
                XCTFail("Missing app mapping for \(exp.appId)")
                return
            }
            let match = incrementalDaily.contains { r in
                r.appId == mappedAppId && r.day == exp.day && r.seconds == exp.seconds
            }
            XCTAssertTrue(match, "Expected incremental daily row (appId: \(mappedAppId), day: \(exp.day), seconds: \(exp.seconds)) not found")
        }

        // Verify full RebuildAll() equals incremental
        try rig.usage.rebuildAll()
        let rebuiltDaily = try rig.store.getDailyUsage()

        XCTAssertEqual(rebuiltDaily.count, incrementalDaily.count, "Rebuilt count must match incremental")
        for (i, row) in rebuiltDaily.enumerated() {
            let inc = incrementalDaily[i]
            XCTAssertEqual(row.appId, inc.appId, "App id mismatch at \(i)")
            XCTAssertEqual(row.day, inc.day, "Day mismatch at \(i)")
            XCTAssertEqual(row.seconds, inc.seconds, "Seconds mismatch at \(i)")
        }
    }
}
