import Foundation

public final class DataExport: Sendable {
    public static func exportCsv(dbPath: String, folder: String) throws -> (sessionsCsv: String, dailyCsv: String) {
        try FileManager.default.createDirectory(atPath: folder, withIntermediateDirectories: true)
        let sessionsCsvPath = (folder as NSString).appendingPathComponent("timesink-sessions.csv")
        let dailyCsvPath = (folder as NSString).appendingPathComponent("timesink-daily.csv")

        let db = try SQLiteDatabase(path: dbPath)
        defer { db.close() }

        let iso = ISO8601DateFormatter()
        iso.formatOptions = [.withInternetDateTime]
        iso.timeZone = TimeZone(secondsFromGMT: 0)

        // 1. Sessions CSV
        var sessionsContent = "app,start_utc,end_utc,seconds\n"
        let sessStmt = try db.prepare("""
            SELECT a.display_name, s.start_utc, COALESCE(s.end_utc, s.last_seen_utc)
            FROM sessions s JOIN apps a ON a.id = s.app_id
            ORDER BY s.start_utc, a.exe_path;
        """)
        while try sessStmt.step() {
            let name = sessStmt.columnText(at: 0) ?? ""
            let start = sessStmt.columnInt64(at: 1)
            let end = sessStmt.columnInt64(at: 2)
            let seconds = end - start
            let startIso = iso.string(from: Date(timeIntervalSince1970: TimeInterval(start)))
            let endIso = iso.string(from: Date(timeIntervalSince1970: TimeInterval(end)))
            sessionsContent += "\(csvEscape(name)),\(startIso),\(endIso),\(seconds)\n"
        }
        try sessionsContent.write(to: URL(fileURLWithPath: sessionsCsvPath), atomically: true, encoding: .utf8)

        // 2. Daily CSV
        var dailyContent = "app,day,seconds\n"
        let dailyStmt = try db.prepare("""
            SELECT a.display_name, d.day, d.seconds
            FROM daily_usage d JOIN apps a ON a.id = d.app_id
            ORDER BY d.day, a.exe_path;
        """)
        while try dailyStmt.step() {
            let name = dailyStmt.columnText(at: 0) ?? ""
            let day = dailyStmt.columnText(at: 1) ?? ""
            let seconds = dailyStmt.columnInt64(at: 2)
            dailyContent += "\(csvEscape(name)),\(day),\(seconds)\n"
        }
        try dailyContent.write(to: URL(fileURLWithPath: dailyCsvPath), atomically: true, encoding: .utf8)

        return (sessionsCsvPath, dailyCsvPath)
    }

    public static func exportJson(dbPath: String, file: String) throws {
        let dir = (file as NSString).deletingLastPathComponent
        try FileManager.default.createDirectory(atPath: dir, withIntermediateDirectories: true)

        let db = try SQLiteDatabase(path: dbPath)
        defer { db.close() }

        let iso = ISO8601DateFormatter()
        iso.formatOptions = [.withInternetDateTime]
        iso.timeZone = TimeZone(secondsFromGMT: 0)

        // Read apps
        var appsArray: [[String: Any?]] = []
        let appStmt = try db.prepare("SELECT exe_path, display_name, category, state, added_utc, retired_utc FROM apps ORDER BY id;")
        while try appStmt.step() {
            let exe = appStmt.columnText(at: 0) ?? ""
            let name = appStmt.columnText(at: 1) ?? ""
            let cat = appStmt.columnText(at: 2) ?? ""
            let state = appStmt.columnText(at: 3) ?? ""
            let addedIso = iso.string(from: Date(timeIntervalSince1970: TimeInterval(appStmt.columnInt64(at: 4))))
            let retiredIso: String? = appStmt.columnIsNull(at: 5) ? nil : iso.string(from: Date(timeIntervalSince1970: TimeInterval(appStmt.columnInt64(at: 5))))
            appsArray.append([
                "exePath": exe,
                "name": name,
                "category": cat,
                "state": state,
                "addedUtc": addedIso,
                "retiredUtc": retiredIso
            ])
        }

        // Read sessions
        var sessionsArray: [[String: Any]] = []
        let sessStmt = try db.prepare("""
            SELECT a.exe_path, a.display_name, s.start_utc, COALESCE(s.end_utc, s.last_seen_utc)
            FROM sessions s JOIN apps a ON a.id = s.app_id
            ORDER BY s.start_utc, a.exe_path;
        """)
        while try sessStmt.step() {
            let exe = sessStmt.columnText(at: 0) ?? ""
            let name = sessStmt.columnText(at: 1) ?? ""
            let start = sessStmt.columnInt64(at: 2)
            let end = sessStmt.columnInt64(at: 3)
            let startIso = iso.string(from: Date(timeIntervalSince1970: TimeInterval(start)))
            let endIso = iso.string(from: Date(timeIntervalSince1970: TimeInterval(end)))
            sessionsArray.append([
                "exePath": exe,
                "app": name,
                "startUtc": startIso,
                "endUtc": endIso,
                "seconds": end - start
            ])
        }

        // Read daily usage
        var dailyArray: [[String: Any]] = []
        let dailyStmt = try db.prepare("""
            SELECT a.exe_path, a.display_name, d.day, d.seconds
            FROM daily_usage d JOIN apps a ON a.id = d.app_id
            ORDER BY d.day, a.exe_path;
        """)
        while try dailyStmt.step() {
            let exe = dailyStmt.columnText(at: 0) ?? ""
            let name = dailyStmt.columnText(at: 1) ?? ""
            let day = dailyStmt.columnText(at: 2) ?? ""
            dailyArray.append([
                "exePath": exe,
                "app": name,
                "day": day,
                "seconds": dailyStmt.columnInt64(at: 3)
            ])
        }

        let rootObject: [String: Any] = [
            "apps": appsArray,
            "sessions": sessionsArray,
            "dailyUsage": dailyArray
        ]

        let data = try JSONSerialization.data(withJSONObject: rootObject, options: [.prettyPrinted, .sortedKeys])
        try data.write(to: URL(fileURLWithPath: file))
    }

    public static func deleteAllHistory(db: SQLiteDatabase, confirmation: String) throws {
        guard confirmation == "DELETE" else {
            throw BackupError.generic("Type DELETE to confirm.")
        }

        try db.transaction {
            try db.exec("DELETE FROM sessions;")
            try db.exec("DELETE FROM daily_usage;")
            try db.exec("DELETE FROM icons;")
            try db.exec("DELETE FROM settings WHERE key LIKE 'sync.imported.%';")
        }
    }

    private static func csvEscape(_ text: String) -> String {
        if text.contains(",") || text.contains("\"") || text.contains("\n") || text.contains("\r") {
            let escaped = text.replacingOccurrences(of: "\"", with: "\"\"")
            return "\"\(escaped)\""
        }
        return text
    }
}
