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

        // Unchanged file is skipped via the fingerprint
        let unchanged = try SyncReader.import(syncFile: snapPath, db: dbPC)
        XCTAssertEqual(unchanged.status, .unchanged)

        // Idempotent re-import (fingerprint cleared so the merge itself runs)
        SyncReader.clearFingerprints(db: dbPC)
        let res2 = try SyncReader.import(syncFile: snapPath, db: dbPC)
        XCTAssertEqual(res2.status, .success)
        XCTAssertEqual(res2.appsAdded, 0)
        XCTAssertEqual(res2.sessionsAdded, 0)
        XCTAssertEqual(res2.sessionsSkipped, 1)
    }

    func testUnchangedRemoteFileIsSkippedUntilHistoryDeleted() throws {
        let dbMac = try SQLiteDatabase(path: ":memory:")
        try Migrator.apply(to: dbMac)
        let macId = try dbMac.scalarText("SELECT device_id FROM devices LIMIT 1;") ?? "mac-1"
        let macAppLib = MacAppLibrary(db: dbMac)
        let safariId = try macAppLib.trackApp(
            exePath: "/Applications/Safari.app",
            displayName: "Safari",
            category: "Browser",
            deviceId: macId
        )
        try dbMac.exec("INSERT INTO sessions (id, app_id, start_utc, end_utc, last_seen_utc, end_reason) VALUES (1, \(safariId), 1000, 2000, 2000, 'exit');")

        let tempFolder = (NSTemporaryDirectory() as NSString).appendingPathComponent("sync-unchanged-\(UUID().uuidString)")
        try FileManager.default.createDirectory(atPath: tempFolder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(atPath: tempFolder) }
        let snapPath = try SyncWriter.write(db: dbMac, syncFolder: tempFolder)

        let dbPC = try SQLiteDatabase(path: ":memory:")
        try Migrator.apply(to: dbPC)
        try dbPC.exec("UPDATE settings SET value = 'pc-remote-2' WHERE key = 'device.id'; UPDATE devices SET device_id = 'pc-remote-2', name = 'Gaming PC', platform = 'windows';")

        XCTAssertEqual(try SyncReader.import(syncFile: snapPath, db: dbPC).status, .success)
        XCTAssertEqual(try SyncReader.import(syncFile: snapPath, db: dbPC).status, .unchanged)

        // Deleting the device history clears the fingerprint and forces a re-import
        try dbPC.exec("DELETE FROM sessions; DELETE FROM daily_usage; DELETE FROM apps WHERE device_id = '\(macId)'; DELETE FROM devices WHERE device_id = '\(macId)'; DELETE FROM settings WHERE key = 'sync.imported.\(macId)';")
        let again = try SyncReader.import(syncFile: snapPath, db: dbPC)
        XCTAssertEqual(again.status, .success)
        XCTAssertEqual(again.sessionsAdded, 1)

        // DataExport.deleteAllHistory clears fingerprints
        try DataExport.deleteAllHistory(db: dbPC, confirmation: "DELETE")
        let remaining = (try dbPC.scalarInt("SELECT COUNT(*) FROM settings WHERE key LIKE 'sync.imported.%';")) ?? -1
        XCTAssertEqual(remaining, 0)
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

    func testGachaGameSyncFromWindowsCarriesOverToMac() throws {
        let dbWin = try SQLiteDatabase(path: ":memory:")
        try Migrator.apply(to: dbWin)
        let winId = "win-dev-gacha"
        try dbWin.exec("""
            UPDATE settings SET value = '\(winId)' WHERE key = 'device.id';
            UPDATE devices SET device_id = '\(winId)', name = 'Gaming PC', platform = 'windows';
            """)

        let winAppLib = MacAppLibrary(db: dbWin)
        _ = try winAppLib.trackApp(
            exePath: "C:\\Games\\Wuthering Waves\\Wuthering Waves.exe",
            displayName: "Wuthering Waves",
            category: "Games",
            kind: nil, // Leave untyped / detected
            deviceId: winId
        )

        let tempFolder = (NSTemporaryDirectory() as NSString).appendingPathComponent("sync-wuwa-\(UUID().uuidString)")
        try FileManager.default.createDirectory(atPath: tempFolder, withIntermediateDirectories: true)
        defer {
            try? FileManager.default.removeItem(atPath: tempFolder)
        }

        let snap = try SyncWriter.write(db: dbWin, syncFolder: tempFolder)

        let dbMac = try SQLiteDatabase(path: ":memory:")
        try Migrator.apply(to: dbMac)

        let res = try SyncReader.import(syncFile: snap, db: dbMac)
        XCTAssertEqual(res.status, .success)

        let macKind = try dbMac.scalarInt("SELECT kind FROM apps WHERE device_id = '\(winId)';")
        XCTAssertEqual(macKind, 2) // Gacha is rawValue 2

        let tracked = try MacAppLibrary(db: dbMac).getTrackedApps()
        XCTAssertEqual(tracked.first?.kind, .gacha)

        let groups = try AppGroups.load(db: dbMac, deviceScope: .all)
        XCTAssertEqual(groups.values.first?.kind, .gacha)
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

    func testEncryptedDatabaseSyncRoundTrip() throws {
        let tempDir = (NSTemporaryDirectory() as NSString).appendingPathComponent("sync-enc-\(UUID().uuidString)")
        try FileManager.default.createDirectory(atPath: tempDir, withIntermediateDirectories: true)
        defer {
            try? FileManager.default.removeItem(atPath: tempDir)
        }

        let syncFolder = (tempDir as NSString).appendingPathComponent("sync")
        let macDbPath = (tempDir as NSString).appendingPathComponent("mac.db")
        let winDbPath = (tempDir as NSString).appendingPathComponent("win.db")

        let macKey = Data(repeating: 0x11, count: 32)
        let winKey = Data(repeating: 0x22, count: 32)

        let dbMac = try SQLiteDatabase(path: macDbPath, key: macKey)
        try Migrator.apply(to: dbMac)
        let macId = "mac-encrypted-1"
        try dbMac.exec("""
            UPDATE settings SET value = '\(macId)' WHERE key = 'device.id';
            UPDATE devices SET device_id = '\(macId)', name = 'Encrypted Mac', platform = 'macos';
            """)

        let macLib = MacAppLibrary(db: dbMac)
        let safariId = try macLib.trackApp(
            exePath: "/Applications/Safari.app",
            displayName: "Safari",
            category: "Browser",
            deviceId: macId
        )
        try dbMac.exec("""
            INSERT INTO sessions (id, app_id, start_utc, end_utc, last_seen_utc, end_reason)
            VALUES (1, \(safariId), 1000, 2000, 2000, 'exit');
            """)

        // Export from encrypted Mac database
        let snapPath = try SyncWriter.write(db: dbMac, syncFolder: syncFolder)
        XCTAssertTrue(FileManager.default.fileExists(atPath: snapPath))

        // Target encrypted Windows database with completely different encryption key
        let dbWin = try SQLiteDatabase(path: winDbPath, key: winKey)
        try Migrator.apply(to: dbWin)
        let winId = "win-encrypted-2"
        try dbWin.exec("""
            UPDATE settings SET value = '\(winId)' WHERE key = 'device.id';
            UPDATE devices SET device_id = '\(winId)', name = 'Encrypted Windows', platform = 'windows';
            """)

        // Import into encrypted Windows database
        let res = try SyncReader.import(syncFile: snapPath, db: dbWin)
        XCTAssertEqual(res.status, .success)
        XCTAssertEqual(res.appsAdded, 1)
        XCTAssertEqual(res.sessionsAdded, 1)

        let importedAppCount = (try dbWin.scalarInt("SELECT COUNT(*) FROM apps WHERE device_id = '\(macId)';")) ?? 0
        XCTAssertEqual(importedAppCount, 1)

        dbMac.close()
        dbWin.close()
    }

    func testBidirectionalRemoteAppClassificationSyncUpdatesLocalApp() throws {
        let dbMac = try SQLiteDatabase(path: ":memory:")
        try Migrator.apply(to: dbMac)
        let macId = try dbMac.scalarText("SELECT device_id FROM devices LIMIT 1;") ?? "mac-1"

        let dbWin = try SQLiteDatabase(path: ":memory:")
        try Migrator.apply(to: dbWin)
        let winId = "win-pc-1"
        try dbWin.exec("""
            UPDATE settings SET value = '\(winId)' WHERE key = 'device.id';
            UPDATE devices SET device_id = '\(winId)', name = 'Windows PC', platform = 'windows';
            """)

        let macLib = MacAppLibrary(db: dbMac)
        let safariId = try macLib.trackApp(
            exePath: "/Applications/Safari.app",
            displayName: "Safari",
            category: "Other",
            deviceId: macId
        )

        let tempFolder = (NSTemporaryDirectory() as NSString).appendingPathComponent("sync-bidi-\(UUID().uuidString)")
        try FileManager.default.createDirectory(atPath: tempFolder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(atPath: tempFolder) }

        // 1. Mac exports its snapshot and Windows imports it
        let macSnap = try SyncWriter.write(db: dbMac, syncFolder: tempFolder)
        _ = try SyncReader.import(syncFile: macSnap, db: dbWin)

        // Windows now has Safari
        let winSafariId = try dbWin.scalarInt("SELECT id FROM apps WHERE device_id = '\(macId)' AND exe_path = '/Applications/Safari.app';")!
        let initialWinCat = try dbWin.scalarText("SELECT category FROM apps WHERE id = \(winSafariId);")
        XCTAssertEqual(initialWinCat, "Other")

        // 2. In Windows app, user changes category of Mac's Safari to "Coding" (kind = 5)
        let updateTime = Int64(Date().timeIntervalSince1970) + 10
        try dbWin.exec("""
            UPDATE apps SET category = 'Coding', kind = 5, updated_utc = \(updateTime) WHERE id = \(winSafariId);
            """)

        // 3. Windows exports snapshot
        let winSnap = try SyncWriter.write(db: dbWin, syncFolder: tempFolder)

        // 4. Mac imports Windows snapshot
        let importRes = try SyncReader.import(syncFile: winSnap, db: dbMac)
        XCTAssertEqual(importRes.status, .success)

        // 5. Verify Mac version of the app updated to Coding!
        let macCategory = try dbMac.scalarText("SELECT category FROM apps WHERE id = \(safariId);")
        let macKind = try dbMac.scalarInt("SELECT kind FROM apps WHERE id = \(safariId);")
        let macUpdatedUtc = try dbMac.scalarInt("SELECT updated_utc FROM apps WHERE id = \(safariId);")

        XCTAssertEqual(macCategory, "Coding")
        XCTAssertEqual(macKind, 5)
        XCTAssertEqual(macUpdatedUtc, updateTime)
    }

    // MARK: - Own sync connection (sync-reliability ticket 02)

    private static let testKey = Data(repeating: 0x11, count: 32)

    private func makeLocalFileDb(in dir: String, deviceId: String) throws -> SQLiteDatabase {
        let db = try SQLiteDatabase(path: (dir as NSString).appendingPathComponent("local.db"), key: Self.testKey)
        try Migrator.apply(to: db)
        try db.exec("""
            UPDATE settings SET value = '\(deviceId)' WHERE key = 'device.id';
            UPDATE devices SET device_id = '\(deviceId)', name = 'Local Mac', platform = 'macos';
            """)
        return db
    }

    private func enableSync(db: SQLiteDatabase, folder: String) throws {
        try db.exec("INSERT INTO settings (key, value) VALUES ('sync.enabled', 'true'), ('sync.folder', '\(folder)') ON CONFLICT(key) DO UPDATE SET value = excluded.value;")
    }

    /// Writes a remote device's .tssync into `folder` with `sessionCount` sessions.
    @discardableResult
    private func writeRemoteFile(folder: String, deviceId: String = "remote-pc-1", sessionCount: Int = 1) throws -> String {
        let remote = try SQLiteDatabase(path: ":memory:")
        try Migrator.apply(to: remote)
        try remote.exec("""
            UPDATE settings SET value = '\(deviceId)' WHERE key = 'device.id';
            UPDATE devices SET device_id = '\(deviceId)', name = 'Remote PC', platform = 'windows';
            """)
        let appId = try MacAppLibrary(db: remote).trackApp(
            exePath: "C:\\Games\\game.exe", displayName: "Game", category: "Game", deviceId: deviceId)
        try remote.exec("""
            WITH RECURSIVE c(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM c WHERE n < \(sessionCount))
            INSERT INTO sessions (app_id, start_utc, end_utc, last_seen_utc, end_reason)
            SELECT \(appId), 1000 + n * 10, 1005 + n * 10, 1005 + n * 10, 'exit' FROM c;
            """)
        return try SyncWriter.write(db: remote, syncFolder: folder)
    }

    private func makeTempDir(_ prefix: String) throws -> String {
        let dir = (NSTemporaryDirectory() as NSString).appendingPathComponent("\(prefix)-\(UUID().uuidString)")
        try FileManager.default.createDirectory(atPath: dir, withIntermediateDirectories: true)
        return dir
    }

    func testWriteRecoversFromLeakedSnapAlias() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try Migrator.apply(to: db)
        let folder = try makeTempDir("sync-leak-out")
        defer { try? FileManager.default.removeItem(atPath: folder) }

        // Simulate what a failed DETACH used to leave behind.
        try db.exec("ATTACH DATABASE ':memory:' AS snap;")

        let first = try SyncWriter.write(db: db, syncFolder: folder)
        XCTAssertTrue(FileManager.default.fileExists(atPath: first))
        try FileManager.default.removeItem(atPath: first)
        let second = try SyncWriter.write(db: db, syncFolder: folder)
        XCTAssertTrue(FileManager.default.fileExists(atPath: second))
    }

    func testImportRecoversFromLeakedSrcAlias() throws {
        let folder = try makeTempDir("sync-leak-in")
        defer { try? FileManager.default.removeItem(atPath: folder) }
        let file = try writeRemoteFile(folder: folder)

        let local = try SQLiteDatabase(path: ":memory:")
        try Migrator.apply(to: local)
        try local.exec("ATTACH DATABASE ':memory:' AS src;")

        let res = try SyncReader.import(syncFile: file, db: local)
        XCTAssertEqual(res.status, .success)
        XCTAssertEqual(res.sessionsAdded, 1)
    }

    /// Phase 0.3: a transaction open on the UI/tracker connection must not wedge export.
    func testExportSurvivesOpenTransactionOnMainConnection() throws {
        let dir = try makeTempDir("sync-wedge")
        defer { try? FileManager.default.removeItem(atPath: dir) }
        let folder = (dir as NSString).appendingPathComponent("sync")
        try FileManager.default.createDirectory(atPath: folder, withIntermediateDirectories: true)

        let main = try makeLocalFileDb(in: dir, deviceId: "local-mac-1")
        try enableSync(db: main, folder: folder)
        let host = SyncHost(dbPath: main.path, key: main.key)
        let target = (folder as NSString).appendingPathComponent("local-mac-1.tssync")

        try main.exec("BEGIN;")
        _ = try main.scalarInt("SELECT COUNT(*) FROM apps;")
        let g1 = DispatchGroup()
        DispatchQueue.global().async(group: g1) { host.performSync() }
        g1.wait()
        try main.exec("COMMIT;")
        XCTAssertTrue(FileManager.default.fileExists(atPath: target))

        try FileManager.default.removeItem(atPath: target)
        let g2 = DispatchGroup()
        DispatchQueue.global().async(group: g2) { host.performSync() }
        g2.wait()
        XCTAssertTrue(FileManager.default.fileExists(atPath: target), "second export must succeed")
    }

    func testImportOnSyncConnectionChangesDataVersionOnMainConnection() throws {
        let dir = try makeTempDir("sync-dataversion")
        defer { try? FileManager.default.removeItem(atPath: dir) }
        let folder = (dir as NSString).appendingPathComponent("sync")
        let main = try makeLocalFileDb(in: dir, deviceId: "local-mac-1")
        try enableSync(db: main, folder: folder)
        try writeRemoteFile(folder: folder)

        let store = SQLiteSessionStore(db: main)
        let before = store.dataVersion
        XCTAssertNotNil(before)

        SyncHost(dbPath: main.path, key: main.key).performSync()

        XCTAssertNotEqual(store.dataVersion, before)
        XCTAssertEqual(try main.scalarInt("SELECT COUNT(*) FROM apps WHERE device_id = 'remote-pc-1';"), 1)
    }

    /// 100 main-thread transactions racing performSync (export + import): no failures, no lost writes.
    func testMainTransactionsAndPerformSyncLoopLoseNothing() throws {
        let dir = try makeTempDir("sync-loop")
        defer { try? FileManager.default.removeItem(atPath: dir) }
        let folder = (dir as NSString).appendingPathComponent("sync")
        let main = try makeLocalFileDb(in: dir, deviceId: "local-mac-1")
        try enableSync(db: main, folder: folder)
        try writeRemoteFile(folder: folder)
        let appId = try MacAppLibrary(db: main).trackApp(
            exePath: "/Applications/Safari.app", displayName: "Safari", category: "Browser", deviceId: "local-mac-1")
        let host = SyncHost(dbPath: main.path, key: main.key)

        for i in 0..<100 {
            let group = DispatchGroup()
            DispatchQueue.global().async(group: group) { host.performSync() }
            try main.transaction {
                try main.exec("INSERT INTO sessions (app_id, start_utc, end_utc, last_seen_utc, end_reason) VALUES (\(appId), \(1000 + i * 10), \(1005 + i * 10), \(1005 + i * 10), 'exit');")
            }
            group.wait()
        }
        host.performSync()

        XCTAssertEqual(try main.scalarInt("SELECT COUNT(*) FROM sessions WHERE app_id = \(appId);"), 100)

        // The last export must carry every tracker write (an export wedged at any point would not).
        let target = (folder as NSString).appendingPathComponent("local-mac-1.tssync")
        let verify = try SQLiteDatabase(path: ":memory:")
        try Migrator.apply(to: verify)
        try verify.exec("""
            UPDATE settings SET value = 'verify-pc' WHERE key = 'device.id';
            UPDATE devices SET device_id = 'verify-pc', name = 'Verify', platform = 'windows';
            """)
        let res = try SyncReader.import(syncFile: target, db: verify)
        XCTAssertEqual(res.status, .success)
        XCTAssertEqual(try verify.scalarInt("SELECT COUNT(*) FROM sessions;"), 100)
    }

    /// A large import must not surface SQLITE_BUSY to tracker-style writes on the main connection.
    func testLargeImportDoesNotFailMainConnectionWrites() throws {
        let dir = try makeTempDir("sync-busy")
        defer { try? FileManager.default.removeItem(atPath: dir) }
        let folder = (dir as NSString).appendingPathComponent("sync")
        let main = try makeLocalFileDb(in: dir, deviceId: "local-mac-1")
        try enableSync(db: main, folder: folder)
        try writeRemoteFile(folder: folder, sessionCount: 20000)
        let appId = try MacAppLibrary(db: main).trackApp(
            exePath: "/Applications/Safari.app", displayName: "Safari", category: "Browser", deviceId: "local-mac-1")
        let host = SyncHost(dbPath: main.path, key: main.key)

        let group = DispatchGroup()
        DispatchQueue.global().async(group: group) { host.performSync() }

        var written = 0
        var failures: [String] = []
        while group.wait(timeout: .now()) == .timedOut {
            do {
                try main.exec("INSERT INTO sessions (app_id, start_utc, end_utc, last_seen_utc, end_reason) VALUES (\(appId), \(1000 + written * 10), \(1005 + written * 10), \(1005 + written * 10), 'exit');")
                written += 1
            } catch {
                failures.append("\(error)")
            }
            Thread.sleep(forTimeInterval: 0.002)
        }

        XCTAssertEqual(failures, [])
        XCTAssertEqual(try main.scalarInt("SELECT COUNT(*) FROM sessions WHERE app_id = \(appId);"), Int64(written))
        XCTAssertEqual(try main.scalarInt("SELECT COUNT(*) FROM apps WHERE device_id = 'remote-pc-1';"), 1)
    }

    // MARK: - Export failure visibility (sync-reliability ticket 03)

    private func setFolderWritable(_ folder: String, _ writable: Bool) throws {
        try FileManager.default.setAttributes([.posixPermissions: writable ? 0o755 : 0o555], ofItemAtPath: folder)
    }

    func testReadOnlyFolderMarksExportFailedAndRecovers() throws {
        try XCTSkipIf(geteuid() == 0, "root ignores directory permissions")
        let dir = try makeTempDir("sync-exportfail")
        let folder = (dir as NSString).appendingPathComponent("sync")
        try FileManager.default.createDirectory(atPath: folder, withIntermediateDirectories: true)
        defer {
            try? setFolderWritable(folder, true)
            try? FileManager.default.removeItem(atPath: dir)
        }
        let main = try makeLocalFileDb(in: dir, deviceId: "local-mac-1")
        try enableSync(db: main, folder: folder)
        let host = SyncHost(dbPath: main.path, key: main.key)
        host.exportRetryDelay = 3600   // keep the automatic retry out of this test

        try setFolderWritable(folder, false)
        host.performSync()

        XCTAssertEqual(try main.scalarText("SELECT value FROM settings WHERE key = 'sync.state';"), "export_failed")
        let lastError = try main.scalarText("SELECT value FROM settings WHERE key = 'sync.last_error';") ?? ""
        XCTAssertFalse(lastError.isEmpty)
        XCTAssertFalse(lastError.contains(dir), "last_error must not leak paths")
        XCTAssertEqual(host.getDeviceStatus(), .exportFailed)

        try setFolderWritable(folder, true)
        host.performSync()

        XCTAssertNotEqual(try main.scalarText("SELECT value FROM settings WHERE key = 'sync.state';"), "export_failed")
        XCTAssertNotEqual(host.getDeviceStatus(), .exportFailed)
        let cleared = try main.scalarText("SELECT value FROM settings WHERE key = 'sync.last_error';") ?? ""
        XCTAssertEqual(cleared, "")
        XCTAssertTrue(FileManager.default.fileExists(atPath: (folder as NSString).appendingPathComponent("local-mac-1.tssync")))
    }

    func testExportFailureRetriesWithoutUserAction() throws {
        try XCTSkipIf(geteuid() == 0, "root ignores directory permissions")
        let dir = try makeTempDir("sync-exportretry")
        let folder = (dir as NSString).appendingPathComponent("sync")
        try FileManager.default.createDirectory(atPath: folder, withIntermediateDirectories: true)
        defer {
            try? setFolderWritable(folder, true)
            try? FileManager.default.removeItem(atPath: dir)
        }
        let main = try makeLocalFileDb(in: dir, deviceId: "local-mac-1")
        try enableSync(db: main, folder: folder)
        let host = SyncHost(dbPath: main.path, key: main.key)
        host.exportRetryDelay = 0.3
        let target = (folder as NSString).appendingPathComponent("local-mac-1.tssync")

        try setFolderWritable(folder, false)
        host.performSync()
        XCTAssertEqual(try main.scalarText("SELECT value FROM settings WHERE key = 'sync.state';"), "export_failed")

        try setFolderWritable(folder, true)
        let deadline = Date().addingTimeInterval(5)
        while !FileManager.default.fileExists(atPath: target) && Date() < deadline {
            Thread.sleep(forTimeInterval: 0.1)
        }
        XCTAssertTrue(FileManager.default.fileExists(atPath: target), "retry must export without another performSync call")
        XCTAssertNotEqual(try main.scalarText("SELECT value FROM settings WHERE key = 'sync.state';"), "export_failed")
    }

    func testExportStalenessThreshold() {
        let now: Int64 = 1_000_000
        XCTAssertFalse(SyncExportHealth.isStale(lastExportUtc: 0, now: now), "never exported is not 'stale'")
        XCTAssertFalse(SyncExportHealth.isStale(lastExportUtc: now - 14 * 60, now: now))
        XCTAssertFalse(SyncExportHealth.isStale(lastExportUtc: now - 15 * 60, now: now))
        XCTAssertTrue(SyncExportHealth.isStale(lastExportUtc: now - 15 * 60 - 1, now: now))
    }

    func testShortErrorTextHasNoPathsOrQuotes() {
        let err = NSError(domain: NSCocoaErrorDomain, code: 513, userInfo: [NSFilePathErrorKey: "/Users/me/OneDrive/sync", NSLocalizedDescriptionKey: "can't write /Users/me/OneDrive/sync"])
        let text = SyncExportHealth.shortErrorText(err)
        XCTAssertFalse(text.isEmpty)
        XCTAssertFalse(text.contains("/"))
        XCTAssertFalse(text.contains("'"))
    }
}
