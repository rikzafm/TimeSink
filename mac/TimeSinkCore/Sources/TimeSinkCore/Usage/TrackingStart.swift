import Foundation

public struct TrackingStart: Sendable, Equatable {
    public let firstDay: String?
    public let firstYear: Int?
    public let firstMonth: Int?
    public let firstDayOfMonth: Int?

    public init(firstDay: String?, year: Int? = nil, month: Int? = nil, day: Int? = nil) {
        self.firstDay = firstDay
        self.firstYear = year
        self.firstMonth = month
        self.firstDayOfMonth = day
    }

    public static func of(apps: AppGroups, zone: TimeZone) -> TrackingStart {
        if apps.count == 0 { return TrackingStart(firstDay: nil) }
        let minAdded = apps.values.map(\.addedUtc).min() ?? 0
        let comps = LocalDays.dateComponents(for: minAdded, timeZone: zone)
        guard let y = comps.year, let m = comps.month, let d = comps.day else {
            return TrackingStart(firstDay: nil)
        }
        let dayStr = String(format: "%04d-%02d-%02d", y, m, d)
        return TrackingStart(firstDay: dayStr, year: y, month: m, day: d)
    }

    public func counts(day: String) -> Bool {
        guard let f = firstDay else { return false }
        return day >= f
    }

    public func isPartialMonth(year: Int, month: Int) -> Bool {
        guard let d = firstDayOfMonth, let y = firstYear, let m = firstMonth else { return false }
        return d > 1 && y == year && m == month
    }

    public func since(day: String) -> String {
        guard let f = firstDay else { return day }
        return f > day ? f : day
    }
}

