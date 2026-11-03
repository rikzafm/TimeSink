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

    func testResolveBundlePath() {
        XCTAssertEqual(
            MacIconCache.resolveBundlePath(from: "/Applications/OneDrive.app/Contents/MacOS/OneDrive"),
            "/Applications/OneDrive.app"
        )
        XCTAssertEqual(
            MacIconCache.resolveBundlePath(from: "/Applications/Utilities/Terminal.app/Contents/MacOS/Terminal"),
            "/Applications/Utilities/Terminal.app"
        )
        XCTAssertEqual(
            MacIconCache.resolveBundlePath(from: "/Applications/Safari.app"),
            "/Applications/Safari.app"
        )
        XCTAssertNil(MacIconCache.resolveBundlePath(from: "/usr/local/bin/git"))
        XCTAssertNil(MacIconCache.resolveBundlePath(from: ""))
    }

    func testLoginItemManager() {
        let manager = LoginItemManager.shared
        // Reading isEnabled shouldn't crash
        _ = manager.isEnabled
    }

    func testMacIconCacheInheritsMergedAliasIcon() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try Migrator.apply(to: db)

        let devId = try db.scalarText("SELECT device_id FROM devices LIMIT 1;") ?? "dev-1"
        // Insert main app (id=1) and alias app (id=2) merged under main
        let ins1 = try db.prepare("INSERT INTO apps (id, exe_path, display_name, category, added_utc, device_id) VALUES (1, '', 'Keynote', 'Work', 1000, ?1);")
        try ins1.bind(text: devId, at: 1)
        _ = try ins1.step()

        let ins2 = try db.prepare("INSERT INTO apps (id, canonical_id, exe_path, display_name, category, added_utc, device_id) VALUES (2, 1, '/System/Applications/Preview.app', 'Keynote', 'Work', 1000, ?1);")
        try ins2.bind(text: devId, at: 1)
        _ = try ins2.step()

        let cache = MacIconCache(db: db)
        let sampleData = "alias-icon-data".data(using: .utf8)!
        // Save icon only under alias (id=2)
        cache.saveIcon(forAppId: 2, hash: "alias-hash", png: sampleData)

        // getCachedIcon for main app (id=1) should inherit from alias
        let retrieved = cache.getCachedIcon(forAppId: 1)
        XCTAssertEqual(retrieved, sampleData)
    }

    func testRepairMissingIcons() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try Migrator.apply(to: db)

        let devId = try db.scalarText("SELECT device_id FROM devices LIMIT 1;") ?? "dev-1"
        // Preview app exists on macOS at /System/Applications/Preview.app
        let ins = try db.prepare("INSERT INTO apps (id, exe_path, display_name, category, added_utc, device_id, state) VALUES (10, '/System/Applications/Preview.app', 'Preview', 'Utility', 1000, ?1, 'tracked');")
        try ins.bind(text: devId, at: 1)
        _ = try ins.step()

        let cache = MacIconCache(db: db)
        XCTAssertNil(cache.getCachedIcon(forAppId: 10))

        let library = MacAppLibrary(db: db)
        library.repairMissingIcons(iconCache: cache, localDeviceId: devId)

        let retrieved = cache.getCachedIcon(forAppId: 10)
        XCTAssertNotNil(retrieved)
    }
}
