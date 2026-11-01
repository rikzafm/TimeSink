import Foundation

public enum PeriodUnit: Sendable, Codable {
    case day
    case week
    case month
    case year
}

public final class PeriodNavigator: @unchecked Sendable {
    public let unit: PeriodUnit
    public let first: String
    public let today: String
    private let weekStart: WeekStart

    public init(unit: PeriodUnit, first: String, today: String, weekStart: WeekStart = .mon) {
        self.unit = unit
        self.today = today
        self.first = first > today ? today : first
        self.weekStart = weekStart
    }

    public func contains(day: String) -> Bool {
        day >= first && day <= today
    }

    public func clamp(day: String) -> String {
        if day < first { return first }
        if day > today { return today }
        return day
    }

    public func startOf(day: String) -> String {
        switch unit {
        case .week:
            return WeekStarts.containing(day: day, weekStart: weekStart)
        case .month:
            return String(day.prefix(7)) + "-01"
        case .year:
            return String(day.prefix(4)) + "-01-01"
        case .day:
            return day
        }
    }

    public func step(anchor: String, dir: Int) -> String {
        Self.stepBy(unit: unit, anchor: anchor, dir: dir)
    }

    public static func stepBy(unit: PeriodUnit, anchor: String, dir: Int) -> String {
        let y = Int(anchor.prefix(4)) ?? 1970
        let m = Int(anchor.dropFirst(5).prefix(2)) ?? 1
        let d = Int(anchor.suffix(2)) ?? 1

        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = TimeZone(secondsFromGMT: 0)!
        var comps = DateComponents(year: y, month: m, day: d)
        guard let baseDate = calendar.date(from: comps) else { return anchor }

        let targetDate: Date?
        switch unit {
        case .week:
            targetDate = calendar.date(byAdding: .day, value: 7 * dir, to: baseDate)
        case .month:
            targetDate = calendar.date(byAdding: .month, value: dir, to: baseDate)
        case .year:
            targetDate = calendar.date(byAdding: .year, value: dir, to: baseDate)
        case .day:
            targetDate = calendar.date(byAdding: .day, value: dir, to: baseDate)
        }

        guard let target = targetDate else { return anchor }
        let outComps = calendar.dateComponents([.year, .month, .day], from: target)
        return String(format: "%04d-%02d-%02d", outComps.year!, outComps.month!, outComps.day!)
    }

    public func canPrev(anchor: String) -> Bool {
        startOf(day: anchor) > startOf(day: first)
    }

    public func canNext(anchor: String) -> Bool {
        startOf(day: anchor) < startOf(day: today)
    }
}

