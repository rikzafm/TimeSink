import Foundation

public enum BackupRetention {
    public static let keepNewest: Int = 7
    public static let keepMonths: Int = 12

    public static func filesToDelete(fileNames: [String], now: Date = Date()) -> [String] {
        var auto: [(name: String, date: Date)] = []
        for name in fileNames {
            if let date = parseDate(name) {
                auto.append((name, date))
            }
        }
        auto.sort { a, b in
            if a.date != b.date {
                return a.date > b.date
            }
            return a.name > b.name
        }

        var keep = Set<String>()
        for item in auto.prefix(keepNewest) {
            keep.insert(item.name)
        }

        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = TimeZone(secondsFromGMT: 0)!
        let nowComps = cal.dateComponents([.year, .month], from: now)
        guard let thisMonth = cal.date(from: DateComponents(year: nowComps.year, month: nowComps.month, day: 1)),
              let oldest = cal.date(byAdding: .month, value: -(keepMonths - 1), to: thisMonth) else {
            return auto.filter { !keep.contains($0.name) }.map { $0.name }
        }

        // Group by year and month
        var seenMonths = Set<String>()
        for item in auto where item.date >= oldest {
            let itemComps = cal.dateComponents([.year, .month], from: item.date)
            let key = "\(itemComps.year ?? 0)-\(itemComps.month ?? 0)"
            if !seenMonths.contains(key) {
                seenMonths.insert(key)
                keep.insert(item.name)
            }
        }

        return auto.filter { !keep.contains($0.name) }.map { $0.name }
    }

    public static func parseDate(_ name: String) -> Date? {
        let ext = BackupService.fileExtension
        guard name.hasSuffix(ext) else { return nil }
        for prefix in ["daily-", "weekly-"] {
            if name.hasPrefix(prefix) {
                let start = name.index(name.startIndex, offsetBy: prefix.count)
                let end = name.index(name.endIndex, offsetBy: -ext.count)
                let stamp = String(name[start..<end])
                let df = DateFormatter()
                df.dateFormat = "yyyyMMdd"
                df.timeZone = TimeZone(secondsFromGMT: 0)
                if let d = df.date(from: stamp) {
                    return d
                }
            }
        }
        return nil
    }
}

public enum AutoBackup {
    @discardableResult
    public static func runIfDue(
        db: SQLiteDatabase,
        folder: String,
        now: Date = Date(),
        appVersion: String = "1.0.0"
    ) -> String? {
        guard let mode = try? db.scalarText("SELECT value FROM settings WHERE key = 'backup.mode';") else {
            return nil
        }
        let intervalSeconds: Int64
        switch mode.lowercased() {
        case "daily":
            intervalSeconds = 23 * 3600
        case "weekly":
            intervalSeconds = 7 * 86400 - 3600
        default:
            return nil
        }

        let lastUtc = (try? db.scalarInt("SELECT CAST(value AS INTEGER) FROM settings WHERE key = 'backup.last_utc';")) ?? 0
        let nowUtc = Int64(now.timeIntervalSince1970)
        if nowUtc - lastUtc < intervalSeconds {
            return nil
        }

        let prefix = mode.lowercased() == "weekly" ? "weekly" : "daily"
        let df = DateFormatter()
        df.dateFormat = "yyyyMMdd"
        df.timeZone = TimeZone(secondsFromGMT: 0)
        let fileName = "\(prefix)-\(df.string(from: now))\(BackupService.fileExtension)"
        let filePath = (folder as NSString).appendingPathComponent(fileName)

        do {
            try BackupService.create(dbPath: db.path, destFile: filePath, now: now, appVersion: appVersion)
            try? db.exec("INSERT INTO settings (key, value) VALUES ('backup.last_utc', '\(nowUtc)') ON CONFLICT(key) DO UPDATE SET value = excluded.value;")

            // Prune old backups
            if let files = try? FileManager.default.contentsOfDirectory(atPath: folder) {
                let toDelete = BackupRetention.filesToDelete(fileNames: files, now: now)
                for f in toDelete {
                    let full = (folder as NSString).appendingPathComponent(f)
                    try? FileManager.default.removeItem(atPath: full)
                }
            }
            return filePath
        } catch {
            return nil
        }
    }
}

