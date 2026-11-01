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
        let devId = try db.scalarText("SELECT device_id FROM devices LIMIT 1;") ?? "mac-1"

        let tempFolder = (NSTemporaryDirectory() as NSString).appendingPathComponent("sync-self-\(UUID().uuidString)")
        try FileManager.default.createDirectory(atPath: tempFolder, withIntermediateDirectories: true)
        defer {
            try? FileManager.default.removeItem(atPath: tempFolder)
        }

        let snapPath = try SyncWriter.write(db: db, syncFolder: tempFolder)
        let res = try SyncReader.import(syncFile: snapPath, db: db)
        XCTAssertEqual(res.status, .selfFile)
    }
}
