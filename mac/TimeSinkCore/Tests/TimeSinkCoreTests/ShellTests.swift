import XCTest
import Foundation
@testable import TimeSinkCore

private final class CounterBox: @unchecked Sendable {
    var count = 0
}

final class ShellTests: XCTestCase {
    func testProcessProviderManualTrackingAndCallbacks() {
        let provider = NSWorkspaceProcessProvider(autoObserve: false)
        let box = CounterBox()
        provider.onProcessChanged = {
            box.count += 1
        }

        XCTAssertTrue(provider.snapshot().isEmpty)

        let proc = ProcInfo(
            pid: 1001,
            name: "TestApp",
            fullPath: "/Applications/TestApp.app/Contents/MacOS/TestApp",
            startUtc: Date(),
            bundleId: "com.example.TestApp"
        )
        provider.addOrUpdate(pid: 1001, info: proc)

        XCTAssertEqual(box.count, 1)
        XCTAssertEqual(provider.snapshot().count, 1)
        XCTAssertEqual(provider.snapshot().first?.name, "TestApp")
        XCTAssertEqual(provider.snapshot().first?.bundleId, "com.example.TestApp")

        provider.remove(pid: 1001)
        XCTAssertEqual(box.count, 2)
        XCTAssertTrue(provider.snapshot().isEmpty)
    }

    func testPowerEventHubIntegration() {
        let hub = PowerEventHub()
        let macPower = MacPowerEventHub(hub: hub, autoObserve: false)
        let suspendBox = CounterBox()
        let resumeBox = CounterBox()
        let lockBox = CounterBox()
        let unlockBox = CounterBox()
        let shutdownBox = CounterBox()

        macPower.hub.onSuspend.append { _ in suspendBox.count += 1 }
        macPower.hub.onResume.append { _ in resumeBox.count += 1 }
        macPower.hub.onLock.append { _ in lockBox.count += 1 }
        macPower.hub.onUnlock.append { _ in unlockBox.count += 1 }
        macPower.hub.onShutdown.append { _ in shutdownBox.count += 1 }

        let now = Date()
        macPower.hub.raiseSuspend(now)
        macPower.hub.raiseResume(now)
        macPower.hub.raiseLock(now)
        macPower.hub.raiseUnlock(now)
        macPower.hub.raiseShutdown(now)

        XCTAssertEqual(suspendBox.count, 1)
        XCTAssertEqual(resumeBox.count, 1)
        XCTAssertEqual(lockBox.count, 1)
        XCTAssertEqual(unlockBox.count, 1)
        XCTAssertEqual(shutdownBox.count, 1)
    }

    func testMacIconCacheDatabasePersistence() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try Migrator.apply(to: db)

        let devId = try db.scalarText("SELECT device_id FROM devices LIMIT 1;") ?? "dev-1"
        // Insert app for foreign key constraint
        let insertAppStmt = try db.prepare("""
            INSERT INTO apps (id, exe_path, display_name, category, added_utc, device_id)
            VALUES (1, '/Applications/Safari.app', 'Safari', 'Browser', 1000, ?1);
            """)
        try insertAppStmt.bind(text: devId, at: 1)
        _ = try insertAppStmt.step()

        let cache = MacIconCache(db: db)
        let sampleData = "fake-png-bytes".data(using: .utf8)!

        XCTAssertNil(cache.getCachedIcon(forAppId: 1))

        cache.saveIcon(forAppId: 1, hash: "chosen:safari", png: sampleData)
        let retrieved = cache.getCachedIcon(forAppId: 1)

        XCTAssertNotNil(retrieved)
        XCTAssertEqual(retrieved, sampleData)
    }

    func testLoginItemManager() {
        let manager = LoginItemManager.shared
        // Reading isEnabled shouldn't crash
        _ = manager.isEnabled
    }
}
