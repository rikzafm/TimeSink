import XCTest
import Foundation
@testable import TimeSinkCore

final class SyncTests: XCTestCase {
    func testSyncExportAndImportRoundTrip() throws {
        let dbMac = try SQLiteDatabase(path: ":memory:")
        try Migrator.apply(to: dbMac)
        let macId = try dbMac.scalarText("SELECT device_id FROM devices LIMIT 1;") ?? "mac-1"

        // Seed Mac data
        let macAppLib = MacAppLibrary(db: dbMac)
        let safariId = try macAppLib.trackApp(
            exePath: "/Applications/Safari.app",
            displayName: "Safari",
            category: "Browser",
            deviceId: macId
        )

        try dbMac.exec("""
            INSERT INTO sessions (id, app_id, start_utc, end_utc, last_seen_utc, end_reason)
            VALUES (1, \(safariId), 1000, 2000, 2000, 'exit');
            INSERT INTO daily_usage (app_id, day, seconds)
            VALUES (\(safariId), '2026-11-01', 1000);
            """)

        // Write Mac snapshot to temp folder
        let tempFolder = (NSTemporaryDirectory() as NSString).appendingPathComponent("sync-test-\(UUID().uuidString)")
        try FileManager.default.createDirectory(atPath: tempFolder, withIntermediateDirectories: true)
        defer {
            try? FileManager.default.removeItem(atPath: tempFolder)
        }

        let snapPath = try SyncWriter.write(db: dbMac, syncFolder: tempFolder)
        XCTAssertTrue(FileManager.default.fileExists(atPath: snapPath))
        XCTAssertTrue(snapPath.hasSuffix(".tssync"))

        // Create PC database and import Mac snapshot into PC
        let dbPC = try SQLiteDatabase(path: ":memory:")
        try Migrator.apply(to: dbPC)
        let pcId = "pc-remote-2"
        try dbPC.exec("""
            UPDATE settings SET value = '\(pcId)' WHERE key = 'device.id';
            UPDATE devices SET device_id = '\(pcId)', name = 'Gaming PC', platform = 'windows';
            """)

        let res = try SyncReader.import(syncFile: snapPath, db: dbPC)
        XCTAssertEqual(res.status, .success)
        XCTAssertEqual(res.appsAdded, 1)
        XCTAssertEqual(res.sessionsAdded, 1)

        // Verify PC has Mac device and Mac Safari app
        let remoteDevCount = (try dbPC.scalarInt("SELECT COUNT(*) FROM devices WHERE device_id = '\(macId)';")) ?? 0
        XCTAssertEqual(remoteDevCount, 1)

        let appsCount = (try dbPC.scalarInt("SELECT COUNT(*) FROM apps WHERE device_id = '\(macId)';")) ?? 0
        XCTAssertEqual(appsCount, 1)

        let sessionsCount = (try dbPC.scalarInt("SELECT COUNT(*) FROM sessions;")) ?? 0
        XCTAssertEqual(sessionsCount, 1)

        // Idempotent re-import
        let res2 = try SyncReader.import(syncFile: snapPath, db: dbPC)
        XCTAssertEqual(res2.status, .success)
        XCTAssertEqual(res2.appsAdded, 0)
        XCTAssertEqual(res2.sessionsAdded, 0)
        XCTAssertEqual(res2.sessionsSkipped, 1)
    }

    func testConflictCopyAndPlaceholderIgnored() throws {
        XCTAssertTrue(SyncReader.isConflictCopy(filePath: "/path/to/device (1).tssync"))
        XCTAssertTrue(SyncReader.isConflictCopy(filePath: "/path/to/device - Copy.tssync"))
        XCTAssertTrue(SyncReader.isConflictCopy(filePath: "/path/to/device-conflict-123.tssync"))
        XCTAssertFalse(SyncReader.isConflictCopy(filePath: "/path/to/device.tssync"))

        let tempPath = (NSTemporaryDirectory() as NSString).appendingPathComponent("placeholder-test.tssync")
        FileManager.default.createFile(atPath: tempPath, contents: Data())
        defer {
            try? FileManager.default.removeItem(atPath: tempPath)
        }
        XCTAssertTrue(SyncReader.isPlaceholder(filePath: tempPath))
    }

    func testSelfSnapshotIgnored() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try Migrator.apply(to: db)

        let tempFolder = (NSTemporaryDirectory() as NSString).appendingPathComponent("sync-self-\(UUID().uuidString)")
        try FileManager.default.createDirectory(atPath: tempFolder, withIntermediateDirectories: true)
        defer {
            try? FileManager.default.removeItem(atPath: tempFolder)
        }

        let snapPath = try SyncWriter.write(db: db, syncFolder: tempFolder)
        let res = try SyncReader.import(syncFile: snapPath, db: db)
        XCTAssertEqual(res.status, .selfFile)
    }

    func testSyncHostLifecycleAndStateUpdates() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try Migrator.apply(to: db)

        let tempFolder = (NSTemporaryDirectory() as NSString).appendingPathComponent("sync-host-\(UUID().uuidString)")
        try FileManager.default.createDirectory(atPath: tempFolder, withIntermediateDirectories: true)
        defer {
            try? FileManager.default.removeItem(atPath: tempFolder)
        }

        try db.exec("INSERT INTO settings (key, value) VALUES ('sync.enabled', 'true'), ('sync.folder', '\(tempFolder)') ON CONFLICT(key) DO UPDATE SET value = excluded.value;")

        let host = SyncHost(db: db)
        XCTAssertTrue(host.isEnabled)
        XCTAssertEqual(host.syncFolder, tempFolder)
        XCTAssertEqual(host.getDeviceStatus(), .noOtherFound)

        let expectation = XCTestExpectation(description: "Sync completion callback")
        host.onSyncCompleted = {
            expectation.fulfill()
        }

        host.performSync()
        wait(for: [expectation], timeout: 2.0)

        // Verify settings table was populated with sync stats
        let lastExport = (try? db.scalarInt("SELECT CAST(value AS INTEGER) FROM settings WHERE key = 'sync.last_export_utc';")) ?? 0
        XCTAssertGreaterThan(lastExport, 0)

        let syncState = try db.scalarText("SELECT value FROM settings WHERE key = 'sync.state';")
        XCTAssertEqual(syncState, "no_other_device")

        // Test restart and stop
        host.restart()
        host.stop()
    }

    func testCategoryAndKindSyncUpdatesExistingApps() throws {
        let dbWin = try SQLiteDatabase(path: ":memory:")
        try Migrator.apply(to: dbWin)
        let winId = "win-dev-1"
        try dbWin.exec("""
            UPDATE settings SET value = '\(winId)' WHERE key = 'device.id';
            UPDATE devices SET device_id = '\(winId)', name = 'Windows PC', platform = 'windows';
            """)

        let winAppLib = MacAppLibrary(db: dbWin)
        let vsCodeId = try winAppLib.trackApp(
            exePath: "C:\\tools\\Code.exe",
            displayName: "VS Code",
            category: "Other",
            kind: .other,
            deviceId: winId
        )

        let tempFolder = (NSTemporaryDirectory() as NSString).appendingPathComponent("sync-cat-\(UUID().uuidString)")
        try FileManager.default.createDirectory(atPath: tempFolder, withIntermediateDirectories: true)
        defer {
            try? FileManager.default.removeItem(atPath: tempFolder)
        }

        let snap1 = try SyncWriter.write(db: dbWin, syncFolder: tempFolder)

        // Mac imports snapshot
        let dbMac = try SQLiteDatabase(path: ":memory:")
        try Migrator.apply(to: dbMac)

        let res1 = try SyncReader.import(syncFile: snap1, db: dbMac)
        XCTAssertEqual(res1.status, .success)

        let macCategory1 = try dbMac.scalarText("SELECT category FROM apps WHERE device_id = '\(winId)';")
        let macKind1 = try dbMac.scalarInt("SELECT kind FROM apps WHERE device_id = '\(winId)';")
        XCTAssertEqual(macCategory1, "Other")
        XCTAssertEqual(macKind1, 0)

        // Now Windows updates category and kind
        try dbWin.exec("""
            UPDATE apps SET category = 'Development', kind = 1 WHERE id = \(vsCodeId);
            """)

        let snap2 = try SyncWriter.write(db: dbWin, syncFolder: tempFolder)

        // Mac imports updated snapshot
        let res2 = try SyncReader.import(syncFile: snap2, db: dbMac)
        XCTAssertEqual(res2.status, .success)

        let macCategory2 = try dbMac.scalarText("SELECT category FROM apps WHERE device_id = '\(winId)';")
        let macKind2 = try dbMac.scalarInt("SELECT kind FROM apps WHERE device_id = '\(winId)';")
        XCTAssertEqual(macCategory2, "Development")
        XCTAssertEqual(macKind2, 1)
    }

    func testMergeDataSyncsAcrossDevices() throws {
        let dbWin = try SQLiteDatabase(path: ":memory:")
        try Migrator.apply(to: dbWin)
        let winId = "win-dev-merge"
        try dbWin.exec("""
            UPDATE settings SET value = '\(winId)' WHERE key = 'device.id';
            UPDATE devices SET device_id = '\(winId)', name = 'Windows PC', platform = 'windows';
            """)

        let winAppLib = MacAppLibrary(db: dbWin)
        let chromeMain = try winAppLib.trackApp(
            exePath: "C:\\Chrome\\chrome.exe",
            displayName: "Chrome",
            category: "Browser",
            deviceId: winId
        )
        let chromeBeta = try winAppLib.trackApp(
            exePath: "C:\\Chrome\\chrome-beta.exe",
            displayName: "Chrome Beta",
            category: "Browser",
            deviceId: winId
        )

        let tempFolder = (NSTemporaryDirectory() as NSString).appendingPathComponent("sync-merge-\(UUID().uuidString)")
        try FileManager.default.createDirectory(atPath: tempFolder, withIntermediateDirectories: true)
        defer {
            try? FileManager.default.removeItem(atPath: tempFolder)
        }

        // Mac imports initial state (both unmerged)
        let dbMac = try SQLiteDatabase(path: ":memory:")
        try Migrator.apply(to: dbMac)
        let snap1 = try SyncWriter.write(db: dbWin, syncFolder: tempFolder)
        _ = try SyncReader.import(syncFile: snap1, db: dbMac)

        let macBetaCanonical1 = try dbMac.scalarInt("SELECT canonical_id FROM apps WHERE exe_path = 'C:\\Chrome\\chrome-beta.exe';")
        XCTAssertNil(macBetaCanonical1)

        // On Windows, merge Chrome Beta into Chrome
        try winAppLib.merge(mainId: chromeMain, candidateIds: [chromeBeta])

        // Export and re-import
        let snap2 = try SyncWriter.write(db: dbWin, syncFolder: tempFolder)
        _ = try SyncReader.import(syncFile: snap2, db: dbMac)

        let macMainId = try dbMac.scalarInt("SELECT id FROM apps WHERE exe_path = 'C:\\Chrome\\chrome.exe';")
        let macBetaCanonical2 = try dbMac.scalarInt("SELECT canonical_id FROM apps WHERE exe_path = 'C:\\Chrome\\chrome-beta.exe';")
        XCTAssertNotNil(macMainId)
        XCTAssertEqual(macBetaCanonical2, macMainId)

        // On Windows, unmerge Chrome Beta
        try winAppLib.unmerge(appId: chromeBeta)

        let snap3 = try SyncWriter.write(db: dbWin, syncFolder: tempFolder)
        _ = try SyncReader.import(syncFile: snap3, db: dbMac)

        let macBetaCanonical3 = try dbMac.scalarInt("SELECT canonical_id FROM apps WHERE exe_path = 'C:\\Chrome\\chrome-beta.exe';")
        XCTAssertNil(macBetaCanonical3)
    }

    func testCrossDeviceMergeSync() throws {
        let tempFolder = (NSTemporaryDirectory() as NSString).appendingPathComponent("sync-cross-\(UUID().uuidString)")
        try FileManager.default.createDirectory(atPath: tempFolder, withIntermediateDirectories: true)
        defer {
            try? FileManager.default.removeItem(atPath: tempFolder)
        }

        let dbMac = try SQLiteDatabase(path: ":memory:")
        try Migrator.apply(to: dbMac)
        let macId = try dbMac.scalarText("SELECT device_id FROM devices LIMIT 1;") ?? "mac-1"

        let dbWin = try SQLiteDatabase(path: ":memory:")
        try Migrator.apply(to: dbWin)
        let winId = "win-cross-1"
        try dbWin.exec("""
            UPDATE settings SET value = '\(winId)' WHERE key = 'device.id';
            UPDATE devices SET device_id = '\(winId)', name = 'Windows PC', platform = 'windows';
            """)

        let macLib = MacAppLibrary(db: dbMac)
        let winLib = MacAppLibrary(db: dbWin)

        let macCode = try macLib.trackApp(
            exePath: "/Applications/Visual Studio Code.app",
            displayName: "VS Code",
            category: "Development",
            deviceId: macId
        )
        try dbMac.exec("UPDATE apps SET bundle_id = 'com.microsoft.VSCode' WHERE id = \(macCode);")
        let winCode = try winLib.trackApp(
            exePath: "C:\\tools\\Code.exe",
            displayName: "VS Code",
            category: "Development",
            deviceId: winId
        )

        // Windows exports its app and Mac imports it
        let winSnap1 = try SyncWriter.write(db: dbWin, syncFolder: tempFolder)
        _ = try SyncReader.import(syncFile: winSnap1, db: dbMac)

        // On Mac, find the imported Windows app and merge it into Mac app
        let macImportedWinCode = (try dbMac.scalarInt("SELECT id FROM apps WHERE device_id = '\(winId)' AND exe_path = 'C:\\tools\\Code.exe';"))!
        try macLib.merge(mainId: macCode, candidateIds: [macImportedWinCode])

        // Verify Mac has it merged
        let checkCanonical = try dbMac.scalarInt("SELECT canonical_id FROM apps WHERE id = \(macImportedWinCode);")
        XCTAssertEqual(checkCanonical, macCode)

        // Mac exports snapshot (should include macCode AND macImportedWinCode due to cross-device merge!)
        let macSnap = try SyncWriter.write(db: dbMac, syncFolder: tempFolder)

        // Windows imports Mac snapshot
        let resWin = try SyncReader.import(syncFile: macSnap, db: dbWin)
        XCTAssertEqual(resWin.status, .success)

        // On Windows, the Windows app should now have canonical_id pointing to the Mac app
        let winMacCode = try dbWin.scalarInt("SELECT id FROM apps WHERE device_id = '\(macId)' AND bundle_id = 'com.microsoft.VSCode';")
        let winCodeCanonical = try dbWin.scalarInt("SELECT canonical_id FROM apps WHERE id = \(winCode);")
        XCTAssertNotNil(winMacCode)
        XCTAssertEqual(winCodeCanonical, winMacCode)
    }
}

