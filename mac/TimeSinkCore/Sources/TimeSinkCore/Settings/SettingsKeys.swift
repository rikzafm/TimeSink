import Foundation

public enum DeviceScope: String, Sendable, Codable {
    case this = "This"
    case all = "All"
}

public enum WeekStart: String, Sendable, Codable {
    case mon = "Mon"
    case sun = "Sun"
    case sat = "Sat"

    public static func parse(_ value: String?) -> WeekStart {
        switch value?.lowercased() {
        case "sun", "sunday": return .sun
        case "sat", "saturday": return .sat
        default: return .mon
        }
    }
}

public enum WeekStarts {
    public static func parse(_ value: String?) -> WeekStart {
        WeekStart.parse(value)
    }

    public static func daysIntoWeek(day: String, weekStart: WeekStart) -> Int {
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = TimeZone(secondsFromGMT: 0)!
        let comps = day.split(separator: "-").compactMap { Int($0) }
        guard comps.count == 3, let d = cal.date(from: DateComponents(year: comps[0], month: comps[1], day: comps[2])) else {
            return 0
        }
        let dow = (cal.component(.weekday, from: d) + 6) % 7
        let startDow: Int
        switch weekStart {
        case .sun: startDow = 0
        case .mon: startDow = 1
        case .sat: startDow = 6
        }
        return (dow - startDow + 7) % 7
    }

    public static func containing(day: String, weekStart: WeekStart) -> String {
        let daysInto = daysIntoWeek(day: day, weekStart: weekStart)
        if daysInto == 0 { return day }
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = TimeZone(secondsFromGMT: 0)!
        let comps = day.split(separator: "-").compactMap { Int($0) }
        guard comps.count == 3,
              let d = cal.date(from: DateComponents(year: comps[0], month: comps[1], day: comps[2])),
              let shifted = cal.date(byAdding: .day, value: -daysInto, to: d) else {
            return day
        }
        let out = cal.dateComponents([.year, .month, .day], from: shifted)
        return String(format: "%04d-%02d-%02d", out.year!, out.month!, out.day!)
    }
}

public enum SettingKeys {
    public static let deviceScope = "ui.deviceScope"
    public static let gamesOnly = "ui.gamesOnly"
    public static let weekStart = "week.start"
    public static let pauseOnSleep = "tracking.pauseOnSleep"
    public static let countWhenLocked = "tracking.countWhenLocked"
    public static let minSessionSeconds = "tracking.minSessionSeconds"
    public static let deviceId = "device.id"
    public static let deviceName = "device.name"
    public static let syncFolder = "sync.folder"
    public static let syncEnabled = "sync.enabled"
    public static let theme = "ui.theme"
    public static let accent = "ui.accent"
    public static let appIcon = "ui.icon"
    public static let showMenuBarTime = "ui.showMenuBarTime"
    public static let backupMode = "backup.mode"
    public static let backupLastUtc = "backup.last_utc"
    public static let shortDeviceNames = "devices.shortNames"
    public static let allLayout = "all.layout"
}
