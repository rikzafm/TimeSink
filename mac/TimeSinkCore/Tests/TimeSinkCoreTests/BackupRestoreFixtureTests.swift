import XCTest
import Foundation
@testable import TimeSinkCore

final class BackupRestoreFixtureTests: XCTestCase {
    var tempDir: URL!

    override func setUpWithError() throws {
        tempDir = URL(fileURLWithPath: NSTemporaryDirectory()).appendingPathComponent("timesink-test-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: tempDir, withIntermediateDirectories: true)
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(at: tempDir)
    }

    func testReplaceRestoreRoundTrip() throws {
        let liveDbPath = tempDir.appendingPathComponent("live.db").path
        let backupPath = tempDir.appendingPathComponent("backup.timesink").path
        let safetyFolder = tempDir.appendingPathComponent("safety").path

        let db = try SQLiteDatabase(path: liveDbPath)
        try Migrator.apply(to: db)
        try db.exec("""
            INSERT OR IGNORE INTO devices (device_id, name, platform, is_local, first_seen_utc, state)
            VALUES ('dev-1', 'Device 1', 'windows', 1, '2026-10-23T12:00:00Z', 'active');
            INSERT INTO apps (exe_path, display_name, category, state, added_utc, device_id)
            VALUES ('c:\\\\a\\\\one.exe', 'one.exe', 'Games', 'tracked', 1700000000, 'dev-1');
            INSERT INTO apps (exe_path, display_name, category, state, added_utc, device_id)
            VALUES ('c:\\\\a\\\\two.exe', 'two.exe', 'Other', 'tracked', 1700000000, 'dev-1');
            INSERT INTO sessions (app_id, start_utc, end_utc, last_seen_utc, end_reason)
            VALUES (1, 1700000000, 1700003600, 1700003600, 'exit');
            INSERT INTO sessions (app_id, start_utc, end_utc, last_seen_utc, end_reason)
            VALUES (2, 1700010000, 1700010600, 1700010600, 'exit');
        """)
        let acc = UsageAccumulator(store: SQLiteSessionStore(db: db), timeZone: TimeZone(secondsFromGMT: 0)!)
        try acc.rebuildAll()
        db.close()

        // Create backup
        try BackupService.create(dbPath: liveDbPath, destFile: backupPath, now: Date(timeIntervalSince1970: 1700020000))

        // Modify live database (simulate loss of data or corruption)
        let liveDb2 = try SQLiteDatabase(path: liveDbPath)
        try liveDb2.exec("DELETE FROM sessions; DELETE FROM daily_usage; DELETE FROM apps WHERE id = 2;")
        liveDb2.close()

        // Inspect backup
        let inspection = try BackupService.inspect(backupFile: backupPath)
        XCTAssertEqual(inspection.apps, 2)
        XCTAssertEqual(inspection.sessions, 2)

        // Replace restore
        let replaceResult = try BackupService.replace(
            backupFile: backupPath,
            dbPath: liveDbPath,
            safetyFolder: safetyFolder,
            now: Date(timeIntervalSince1970: 1700025000),
            zone: TimeZone(secondsFromGMT: 0)!
        )
        XCTAssertNotNil(replaceResult.safetyCopy)
        XCTAssertTrue(FileManager.default.fileExists(atPath: replaceResult.safetyCopy!))

        // Verify restored content
        let restoredDb = try SQLiteDatabase(path: liveDbPath)
        let appsCount = try restoredDb.scalarInt("SELECT COUNT(*) FROM apps;")
        let sessionsCount = try restoredDb.scalarInt("SELECT COUNT(*) FROM sessions;")
        let totalSeconds = try restoredDb.scalarInt("SELECT SUM(seconds) FROM daily_usage;")
        restoredDb.close()

        XCTAssertEqual(appsCount, 2)
        XCTAssertEqual(sessionsCount, 2)
        XCTAssertEqual(totalSeconds, 4200)
    }

    func testMergeTwiceIsIdempotent() throws {
        let liveDbPath = tempDir.appendingPathComponent("live.db").path
        let otherDbPath = tempDir.appendingPathComponent("other.db").path
        let otherBackupPath = tempDir.appendingPathComponent("other.timesink").path

        // Setup live
        let liveDb = try SQLiteDatabase(path: liveDbPath)
        try Migrator.apply(to: liveDb)
        try liveDb.exec("""
            INSERT OR IGNORE INTO devices (device_id, name, platform, is_local, first_seen_utc, state)
            VALUES ('dev-1', 'Device 1', 'windows', 1, '2026-10-23T12:00:00Z', 'active');
            INSERT INTO apps (exe_path, display_name, category, state, added_utc, device_id)
            VALUES ('c:\\\\a\\\\one.exe', 'one.exe', 'Other', 'tracked', 1700000000, 'dev-1');
            INSERT INTO apps (exe_path, display_name, category, state, added_utc, device_id)
            VALUES ('c:\\\\a\\\\two.exe', 'two.exe', 'Other', 'tracked', 1700000000, 'dev-1');
            INSERT INTO sessions (app_id, start_utc, end_utc, last_seen_utc, end_reason)
            VALUES (1, 1700000000, 1700003600, 1700003600, 'exit');
            INSERT INTO sessions (app_id, start_utc, end_utc, last_seen_utc, end_reason)
            VALUES (2, 1700010000, 1700010600, 1700010600, 'exit');
            INSERT INTO settings (key, value) VALUES ('ui.theme', 'Light');
        """)
        liveDb.close()

        // Setup incoming "other" backup
        let otherDb = try SQLiteDatabase(path: otherDbPath)
        try Migrator.apply(to: otherDb)
        try otherDb.exec("""
            INSERT OR IGNORE INTO devices (device_id, name, platform, is_local, first_seen_utc, state)
            VALUES ('dev-2', 'Device 2', 'windows', 0, '2026-10-23T12:00:00Z', 'active');
            INSERT INTO apps (exe_path, display_name, category, state, added_utc, device_id)
            VALUES ('c:\\\\a\\\\one.exe', 'one.exe', 'Other', 'tracked', 1700000000, 'dev-2');
            INSERT INTO apps (exe_path, display_name, category, state, added_utc, device_id)
            VALUES ('C:\\\\A\\\\Two.exe', 'Two.exe', 'Other', 'tracked', 1700000000, 'dev-2');
            INSERT INTO apps (exe_path, display_name, category, state, added_utc, device_id)
            VALUES ('c:\\\\b\\\\new.exe', 'new.exe', 'Other', 'tracked', 1700000000, 'dev-2');
            INSERT INTO sessions (app_id, start_utc, end_utc, last_seen_utc, end_reason)
            VALUES (1, 1700000000, 1700003600, 1700003600, 'exit');
            INSERT INTO sessions (app_id, start_utc, end_utc, last_seen_utc, end_reason)
            VALUES (2, 1700020000, 1700020300, 1700020300, 'exit');
            INSERT INTO sessions (app_id, start_utc, end_utc, last_seen_utc, end_reason)
            VALUES (3, 1700030000, 1700030100, 1700030100, 'exit');
        """)
        otherDb.close()

        try BackupService.create(dbPath: otherDbPath, destFile: otherBackupPath, now: Date(timeIntervalSince1970: 1700040000))

        // First merge
        let firstResult = try BackupService.merge(backupFile: otherBackupPath, dbPath: liveDbPath, zone: TimeZone(secondsFromGMT: 0)!)
        XCTAssertEqual(firstResult.appsAdded, 1) // new.exe
        XCTAssertEqual(firstResult.sessionsAdded, 2) // two.exe session (300s) + new.exe session (100s)
        XCTAssertEqual(firstResult.sessionsSkipped, 1) // one.exe duplicate start_utc

        // Second merge with same backup
        let secondResult = try BackupService.merge(backupFile: otherBackupPath, dbPath: liveDbPath, zone: TimeZone(secondsFromGMT: 0)!)
        XCTAssertEqual(secondResult.appsAdded, 0)
        XCTAssertEqual(secondResult.sessionsAdded, 0)
        XCTAssertEqual(secondResult.sessionsSkipped, 3)

        let verifyDb = try SQLiteDatabase(path: liveDbPath)
        let totalDailySeconds = try verifyDb.scalarInt("SELECT SUM(seconds) FROM daily_usage;")
        let themeSetting = try verifyDb.scalarText("SELECT value FROM settings WHERE key = 'ui.theme';")
        verifyDb.close()

        XCTAssertEqual(totalDailySeconds, 4600) // 3600 + 600 + 300 + 100
        XCTAssertEqual(themeSetting, "Light")
    }

    func testNewerSchemaRefused() throws {
        let fakeBackupPath = tempDir.appendingPathComponent("future.timesink").path
        let tempExtractDir = tempDir.appendingPathComponent("future-pkg")
        try FileManager.default.createDirectory(at: tempExtractDir, withIntermediateDirectories: true)

        let manifest = BackupManifest(format: 1, schemaVersion: 999, appVersion: "99.0", createdUtc: "2026-10-23T12:00:00Z")
        let manifestData = try manifest.toJsonData()
        try manifestData.write(to: tempExtractDir.appendingPathComponent(BackupManifest.entryName))

        let dummyDbPath = tempExtractDir.appendingPathComponent(BackupManifest.dbEntryName).path
        let db = try SQLiteDatabase(path: dummyDbPath)
        db.userVersion = 999
        db.close()

        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/zip")
        process.arguments = ["-q", "-j", fakeBackupPath, tempExtractDir.appendingPathComponent(BackupManifest.entryName).path, dummyDbPath]
        try process.run()
        process.waitUntilExit()

        XCTAssertThrowsError(try BackupService.inspect(backupFile: fakeBackupPath)) { error in
            guard let backupError = error as? BackupError else {
                XCTFail("Expected BackupError, got \(error)")
                return
            }
            if case .newerSchemaVersion(let current, _) = backupError {
                XCTAssertEqual(current, 999)
            } else {
                XCTFail("Expected newerSchemaVersion error, got \(backupError)")
            }
        }
    }

    func testExportAndHistoryDelete() throws {
        let liveDbPath = tempDir.appendingPathComponent("live.db").path
        let exportFolder = tempDir.appendingPathComponent("export_csv").path
        let jsonExportPath = tempDir.appendingPathComponent("export.json").path

        let db = try SQLiteDatabase(path: liveDbPath)
        try Migrator.apply(to: db)
        try db.exec("""
            INSERT OR IGNORE INTO devices (device_id, name, platform, is_local, first_seen_utc, state)
            VALUES ('dev-1', 'Device 1', 'macos', 1, '2026-10-23T12:00:00Z', 'active');
            INSERT INTO apps (exe_path, display_name, category, state, added_utc, device_id)
            VALUES ('/Applications/Test.app', 'Test App', 'Other', 'tracked', 1700000000, 'dev-1');
            INSERT INTO sessions (app_id, start_utc, end_utc, last_seen_utc, end_reason)
            VALUES (1, 1700000000, 1700001800, 1700001800, 'exit');
        """)
        let acc = UsageAccumulator(store: SQLiteSessionStore(db: db), timeZone: TimeZone(secondsFromGMT: 0)!)
        try acc.rebuildAll()

        // Test CSV export
        let csvFiles = try DataExport.exportCsv(dbPath: liveDbPath, folder: exportFolder)
        XCTAssertTrue(FileManager.default.fileExists(atPath: csvFiles.sessionsCsv))
        XCTAssertTrue(FileManager.default.fileExists(atPath: csvFiles.dailyCsv))

        let sessCsv = try String(contentsOfFile: csvFiles.sessionsCsv, encoding: .utf8)
        XCTAssertTrue(sessCsv.contains("Test App"))
        XCTAssertTrue(sessCsv.contains("1800"))

        // Test JSON export
        try DataExport.exportJson(dbPath: liveDbPath, file: jsonExportPath)
        XCTAssertTrue(FileManager.default.fileExists(atPath: jsonExportPath))
        let jsonData = try Data(contentsOf: URL(fileURLWithPath: jsonExportPath))
        let jsonObj = try JSONSerialization.jsonObject(with: jsonData) as? [String: Any]
        XCTAssertNotNil(jsonObj?["apps"])
        XCTAssertNotNil(jsonObj?["sessions"])
        XCTAssertNotNil(jsonObj?["dailyUsage"])

        // Test DeleteAllHistory
        XCTAssertThrowsError(try DataExport.deleteAllHistory(db: db, confirmation: "wrong"))
        try DataExport.deleteAllHistory(db: db, confirmation: "DELETE")

        let sessionsCount = try db.scalarInt("SELECT COUNT(*) FROM sessions;")
        let dailyCount = try db.scalarInt("SELECT COUNT(*) FROM daily_usage;")
        let appsCount = try db.scalarInt("SELECT COUNT(*) FROM apps;")
        XCTAssertEqual(sessionsCount, 0)
        XCTAssertEqual(dailyCount, 0)
        XCTAssertEqual(appsCount, 1) // apps remain preserved

        db.close()
    }
}
