import Foundation

/// Local calendar day helpers shared by usage accumulation and queries (DST-aware).
public enum LocalDays {
    public static func dateComponents(for utcSeconds: Int64, timeZone: TimeZone) -> DateComponents {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = timeZone
        let date = Date(timeIntervalSince1970: TimeInterval(utcSeconds))
        return calendar.dateComponents([.year, .month, .day], from: date)
    }

    public static func dayString(for utcSeconds: Int64, timeZone: TimeZone) -> String {
        let comps = dateComponents(for: utcSeconds, timeZone: timeZone)
        return String(format: "%04d-%02d-%02d", comps.year ?? 1970, comps.month ?? 1, comps.day ?? 1)
    }

    /// UTC unix seconds of local midnight for the given date components.
    public static func startOf(year: Int, month: Int, day: Int, timeZone: TimeZone) -> Int64 {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = timeZone
        var comps = DateComponents()
        comps.year = year
        comps.month = month
        comps.day = day
        comps.hour = 0
        comps.minute = 0
        comps.second = 0
        if let d = calendar.date(from: comps) {
            let start = calendar.startOfDay(for: d)
            return Int64(start.timeIntervalSince1970)
        }
        return 0
    }

    /// UTC unix seconds of local midnight for the start of the next calendar day.
    public static func startOfNextDay(for utcSeconds: Int64, timeZone: TimeZone) -> Int64 {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = timeZone
        let date = Date(timeIntervalSince1970: TimeInterval(utcSeconds))
        let startOfCurrent = calendar.startOfDay(for: date)
        if let nextDay = calendar.date(byAdding: .day, value: 1, to: startOfCurrent) {
            let startOfNext = calendar.startOfDay(for: nextDay)
            return Int64(startOfNext.timeIntervalSince1970)
        }
        return utcSeconds + 86400
    }
}

