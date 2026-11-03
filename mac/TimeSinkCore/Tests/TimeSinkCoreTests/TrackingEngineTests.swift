import XCTest
import Foundation
@testable import TimeSinkCore

final class TrackingEngineTests: XCTestCase {
    private func unix(_ seconds: Int) -> Int64 {
        Int64(TrackingTestRig.baseDate.timeIntervalSince1970) + Int64(seconds)
    }

    func testTimeBeforeTheEngineStartedIsNotBackFilled() throws {
        let rig = try TrackingTestRig()
        rig.procs.running.append(rig.notepadProc(pid: 1)) // started long ago
        try rig.tickEvery5(from: 0, to: 40)

        let sessions = try rig.getAllSessions()
        XCTAssertEqual(sessions.count, 1)
        XCTAssertEqual(sessions[0].startUtc, unix(0))
    }

    func testTimeBeforeTheAppWasAddedIsNotCounted() throws {
        let rig = try TrackingTestRig(addedUtc: unix(30))
        rig.procs.running.append(rig.notepadProc(pid: 1))
        try rig.tickEvery5(from: 0, to: 60)

        let sessions = try rig.getAllSessions()
        XCTAssertEqual(sessions.count, 1)
        XCTAssertEqual(sessions[0].startUtc, unix(30))
    }

    func testOtherProgramsAndOtherPathsAreIgnoredButPathCaseAndPathlessUniqueNamesMatch() throws {
        let rig = try TrackingTestRig()
        rig.settings.minSessionSeconds = 5

        rig.procs.running.append(ProcInfo(pid: 1, name: "notepad.exe", fullPath: "c:\\other\\notepad.exe", startUtc: rig.atTime(0)))
        rig.procs.running.append(ProcInfo(pid: 2, name: "calc.exe", fullPath: "c:\\windows\\calc.exe", startUtc: rig.atTime(0)))
        try rig.tick(0)
        XCTAssertTrue(try rig.getAllSessions().isEmpty)

        rig.procs.running.append(ProcInfo(pid: 3, name: "Notepad.EXE", fullPath: "C:\\Windows\\Notepad.exe", startUtc: rig.atTime(0)))
        try rig.tick(5)
        XCTAssertEqual(try rig.getAllSessions().count, 1)

        rig.procs.running.removeAll()
        try rig.tick(10)

        rig.procs.running.append(ProcInfo(pid: 4, name: "notepad.exe", fullPath: nil, startUtc: rig.atTime(10)))
        try rig.tick(15)
        XCTAssertEqual(try rig.getAllSessions().count, 2)
    }

    func testHeartbeatEvery30SecondsUpdatesLastSeenAndAccumulatesUsage() throws {
        let rig = try TrackingTestRig()
        rig.procs.running.append(rig.notepadProc(pid: 1))
        try rig.tickEvery5(from: 0, to: 25)

        let sessions1 = try rig.getAllSessions()
        XCTAssertEqual(sessions1.count, 1)
        XCTAssertEqual(sessions1[0].lastSeenUtc, unix(0))
        XCTAssertTrue(try rig.dailyUsageMap().isEmpty)

        try rig.tickEvery5(from: 30, to: 55)
        let sessions2 = try rig.getAllSessions()
        XCTAssertEqual(sessions2[0].lastSeenUtc, unix(30))
        let daily = try rig.dailyUsageMap()
        XCTAssertEqual(daily["2026-03-10"], 30)
    }

    func testRetiringARunningAppClosesItsSessionAsRemoved() throws {
        let rig = try TrackingTestRig()
        rig.procs.running.append(rig.notepadProc(pid: 1))
        try rig.tickEvery5(from: 0, to: 40)

        // External update retiring app as UI does
        try rig.externalExec("UPDATE apps SET state = 'retired', retired_utc = \(unix(41)) WHERE id = \(rig.defaultAppId);")
        rig.clock.utcNow = rig.atTime(42)
        try rig.engine.reloadTrackedApps()

        let sessions = try rig.getAllSessions()
        XCTAssertEqual(sessions.count, 1)
        XCTAssertEqual(sessions[0].endUtc, unix(42))
        XCTAssertEqual(sessions[0].endReason, "removed")

        try rig.tickEvery5(from: 45, to: 80)
        XCTAssertEqual(try rig.getAllSessions().count, 1)
    }

    func testAnAppAddedWhileRunningIsPickedUpAfterReload() throws {
        let rig = try TrackingTestRig()
        try rig.externalExec("INSERT INTO apps (exe_path, display_name, added_utc) VALUES ('c:\\windows\\calc.exe', 'Calc', 0);")
        let calcId = try rig.db.scalarInt("SELECT id FROM apps WHERE display_name = 'Calc';")!
        rig.procs.running.append(ProcInfo(pid: 9, name: "calc.exe", fullPath: "c:\\windows\\calc.exe", startUtc: rig.atTime(-10)))

        try rig.tick(0)
        XCTAssertTrue(try rig.getAllSessions().isEmpty)

        try rig.engine.reloadTrackedApps()
        try rig.tick(5)

        let sessions = try rig.getAllSessions()
        XCTAssertEqual(sessions.count, 1)
        XCTAssertEqual(sessions[0].appId, calcId)
    }

    func testReloadReReadsAppListOnlyWhenAnotherConnectionCommitted() throws {
        let rig = try TrackingTestRig()
        let store = CountingStore(inner: rig.store)
        let engine = try TrackingEngine(
            store: store,
            processes: rig.procs,
            clock: rig.clock,
            settings: rig.settings,
            timeZone: rig.timeZone
        )

        let baseline = store.appReads
        try engine.reloadTrackedApps()
        try engine.reloadTrackedApps()
        try engine.reloadTrackedApps()
        XCTAssertLessThanOrEqual(store.appReads, baseline)

        // No-op update
        try rig.externalExec("UPDATE settings SET value = value WHERE 0;")
        try engine.reloadTrackedApps()
        XCTAssertLessThanOrEqual(store.appReads, baseline)

        // External commit
        try rig.externalExec("INSERT INTO settings (key, value) VALUES ('x', '1');")
        try engine.reloadTrackedApps()
        XCTAssertEqual(store.appReads, baseline + 1)
        try engine.reloadTrackedApps()
        XCTAssertEqual(store.appReads, baseline + 1)
    }

    func testAStoreThatCannotTellAlwaysReloads() throws {
        let rig = try TrackingTestRig()
        let store = CountingStore(inner: rig.store, versioned: false)
        let engine = try TrackingEngine(
            store: store,
            processes: rig.procs,
            clock: rig.clock,
            settings: rig.settings,
            timeZone: rig.timeZone
        )

        let baseline = store.appReads
        try engine.reloadTrackedApps()
        try engine.reloadTrackedApps()
        XCTAssertEqual(store.appReads, baseline + 2)
    }

    func testBackgroundAppOnlyTracksWhenExplicitlyFocused() throws {
        let rig = try TrackingTestRig()
        rig.settings.minSessionSeconds = 15

        // Mark default app as background app in DB
        try rig.externalExec("UPDATE apps SET is_background = 1 WHERE id = \(rig.defaultAppId);")
        try rig.engine.reloadTrackedApps()

        // Proc is running but NOT active (in background)
        rig.procs.running.append(ProcInfo(
            pid: 1,
            name: "notepad.exe",
            fullPath: "c:\\windows\\notepad.exe",
            startUtc: rig.atTime(0),
            isActive: false,
            activeSinceUtc: nil
        ))

        // Tick from 0 to 40 while in background
        try rig.tickEvery5(from: 0, to: 40)
        XCTAssertTrue(try rig.getAllSessions().isEmpty, "Background app must not track when unfocused")

        // User activates / focuses app at t = 50
        rig.procs.running.removeAll()
        rig.procs.running.append(ProcInfo(
            pid: 1,
            name: "notepad.exe",
            fullPath: "c:\\windows\\notepad.exe",
            startUtc: rig.atTime(0),
            isActive: true,
            activeSinceUtc: rig.atTime(50)
        ))

        try rig.tick(50)
        let openSessions = try rig.getAllSessions().filter { $0.endUtc == nil }
        XCTAssertEqual(openSessions.count, 1, "Session must open when background app becomes active")
        XCTAssertEqual(openSessions[0].startUtc, unix(50))

        // Keep active through 80 (30s duration)
        try rig.tickEvery5(from: 55, to: 80)
        let sessions = try rig.getAllSessions()
        XCTAssertEqual(sessions.count, 1)

        // Deactivate app at t = 85
        rig.procs.running.removeAll()
        rig.procs.running.append(ProcInfo(
            pid: 1,
            name: "notepad.exe",
            fullPath: "c:\\windows\\notepad.exe",
            startUtc: rig.atTime(0),
            isActive: false,
            activeSinceUtc: nil
        ))

        try rig.tick(85)
        let closedSessions = try rig.getAllSessions()
        XCTAssertEqual(closedSessions.count, 1)
        XCTAssertEqual(closedSessions[0].endUtc, unix(85))
        XCTAssertEqual(closedSessions[0].endReason, EndReasons.exit)

        // Proc continues running in background through 120 -> no new session
        try rig.tickEvery5(from: 90, to: 120)
        let finalSessions = try rig.getAllSessions()
        XCTAssertEqual(finalSessions.count, 1)
    }

    func testProcessProviderActivationAndDeactivation() {
        final class SafeCounter: @unchecked Sendable {
            var count = 0
        }
        let provider = NSWorkspaceProcessProvider(autoObserve: false)
        let counter = SafeCounter()
        provider.onProcessChanged = {
            counter.count += 1
        }

        let p1 = ProcInfo(pid: 101, name: "AppOne", fullPath: "/AppOne", startUtc: Date())
        let p2 = ProcInfo(pid: 102, name: "AppTwo", fullPath: "/AppTwo", startUtc: Date())
        provider.addOrUpdate(pid: 101, info: p1)
        provider.addOrUpdate(pid: 102, info: p2)

        // Activate PID 101
        provider.handleActivation(pid: 101)
        let snap1 = provider.snapshot()
        let active1 = snap1.first { $0.pid == 101 }
        let inactive1 = snap1.first { $0.pid == 102 }
        XCTAssertEqual(active1?.isActive, true)
        XCTAssertEqual(inactive1?.isActive, false)

        // Switch activation to PID 102
        provider.handleActivation(pid: 102)
        let snap2 = provider.snapshot()
        let inactive2 = snap2.first { $0.pid == 101 }
        let active2 = snap2.first { $0.pid == 102 }
        XCTAssertEqual(inactive2?.isActive, false)
        XCTAssertEqual(active2?.isActive, true)

        // Deactivate PID 102
        provider.handleDeactivation(pid: 102)
        let snap3 = provider.snapshot()
        XCTAssertFalse(snap3.contains { $0.isActive })
        XCTAssertGreaterThan(counter.count, 2)
    }

    func testBundleFolderAndBinaryPathMatch() throws {
        let rig = try TrackingTestRig()
        // Insert WhatsApp tracked as .app bundle via externalExec to update data_version
        try rig.externalExec("""
            INSERT INTO apps (exe_path, display_name, added_utc, device_id, state)
            VALUES ('/Applications/WhatsApp.app', 'WhatsApp', \(unix(0)), '\(rig.deviceId)', 'tracked');
        """)
        try rig.engine.reloadTrackedApps()
        let waId = try rig.db.scalarInt("SELECT id FROM apps WHERE exe_path = '/Applications/WhatsApp.app';")!

        // Process reports the Mach-O binary inside the bundle
        rig.procs.running.append(ProcInfo(
            pid: 42,
            name: "WhatsApp",
            fullPath: "/Applications/WhatsApp.app/Contents/MacOS/WhatsApp",
            startUtc: rig.atTime(0)
        ))
        try rig.tick(0)

        let sessions = try rig.getAllSessions()
        XCTAssertEqual(sessions.count, 1)
        XCTAssertEqual(sessions.first?.appId, Int64(waId))
    }

    func testBundleIdMatchesEvenWhenPathsDiffer() throws {
        let rig = try TrackingTestRig()
        try rig.externalExec("""
            INSERT INTO apps (exe_path, bundle_id, display_name, added_utc, device_id, state)
            VALUES ('/Applications/WhatsApp.app', 'net.whatsapp.WhatsApp', 'WhatsApp', \(unix(0)), '\(rig.deviceId)', 'tracked');
        """)
        try rig.engine.reloadTrackedApps()
        let waId = try rig.db.scalarInt("SELECT id FROM apps WHERE exe_path = '/Applications/WhatsApp.app';")!

        // Process path differs or is sandboxed/temporary, but bundleId matches
        rig.procs.running.append(ProcInfo(
            pid: 99,
            name: "WhatsApp",
            fullPath: "/private/var/folders/xyz/WhatsApp",
            startUtc: rig.atTime(0),
            bundleId: "net.whatsapp.WhatsApp"
        ))
        try rig.tick(0)

        let sessions = try rig.getAllSessions()
        XCTAssertEqual(sessions.count, 1)
        XCTAssertEqual(sessions.first?.appId, Int64(waId))
    }

    func testProcessRemovedClosesSessionImmediately() throws {
        let rig = try TrackingTestRig()
        rig.procs.running.append(rig.notepadProc(pid: 1))
        try rig.tick(0)
        XCTAssertEqual(try rig.getAllSessions().count, 1)
        XCTAssertNil(try rig.getAllSessions()[0].endUtc)

        // Run past minSessionSeconds (15s)
        try rig.tickEvery5(from: 5, to: 25)

        // Process terminates
        rig.procs.running.removeAll()
        try rig.tick(30)

        let sessions = try rig.getAllSessions()
        XCTAssertEqual(sessions.count, 1)
        XCTAssertNotNil(sessions[0].endUtc)
        XCTAssertEqual(sessions[0].endReason, EndReasons.exit)
    }
}

// MARK: - Helper Counting Store

private final class CountingStore: SessionStore, @unchecked Sendable {
    private let inner: SessionStore
    private let versioned: Bool
    var appReads: Int = 0

    init(inner: SessionStore, versioned: Bool = true) {
        self.inner = inner
        self.versioned = versioned
    }

    var dataVersion: Int64? {
        versioned ? inner.dataVersion : nil
    }

    func getTrackedApps() throws -> [TrackedApp] {
        appReads += 1
        return try inner.getTrackedApps()
    }

    func getOpenSessions() throws -> [SessionRecord] { try inner.getOpenSessions() }
    func getAllSessions() throws -> [SessionRecord] { try inner.getAllSessions() }
    func openSession(appId: Int64, startUtc: Int64, pid: Int32?, procStartUtc: Int64?) throws -> SessionRecord {
        try inner.openSession(appId: appId, startUtc: startUtc, pid: pid, procStartUtc: procStartUtc)
    }
    func heartbeat(sessionId: Int64, lastSeenUtc: Int64) throws { try inner.heartbeat(sessionId: sessionId, lastSeenUtc: lastSeenUtc) }
    func closeSession(sessionId: Int64, endUtc: Int64, reason: String) throws { try inner.closeSession(sessionId: sessionId, endUtc: endUtc, reason: reason) }
    func deleteSession(sessionId: Int64) throws { try inner.deleteSession(sessionId: sessionId) }
    func addDailyUsage(appId: Int64, day: String, deltaSeconds: Int64) throws { try inner.addDailyUsage(appId: appId, day: day, deltaSeconds: deltaSeconds) }
    func getDailyUsage() throws -> [DailyUsageRow] { try inner.getDailyUsage() }
    func replaceDailyUsage(_ rows: [DailyUsageRow]) throws { try inner.replaceDailyUsage(rows) }
}
