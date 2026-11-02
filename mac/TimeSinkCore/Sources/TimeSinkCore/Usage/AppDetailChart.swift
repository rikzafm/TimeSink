import Foundation

public enum AppDetailRange: String, Sendable, Codable, CaseIterable {
    case matrix
    case all
    case year
    case month
    case week
}

public struct AppDetailBar: Sendable, Codable, Equatable {
    public let label: String
    public let seconds: Int64
    public let from: String
    public let to: String
    public let sessions: Int

    public init(label: String, seconds: Int64, from: String, to: String, sessions: Int = 0) {
        self.label = label
        self.seconds = seconds
        self.from = from
        self.to = to
        self.sessions = sessions
    }
}

public struct AppDetailChartData: Sendable, Codable, Equatable {
    public let from: String
    public let to: String
    public let bars: [AppDetailBar]
    public let totalSeconds: Int64
    public let opensByHour: [Int]
    public let peakHour: Int?
    public let canPrev: Bool
    public let canNext: Bool

    public init(
        from: String,
        to: String,
        bars: [AppDetailBar],
        totalSeconds: Int64,
        opensByHour: [Int],
        peakHour: Int?,
        canPrev: Bool,
        canNext: Bool
    ) {
        self.from = from
        self.to = to
        self.bars = bars
        self.totalSeconds = totalSeconds
        self.opensByHour = opensByHour
        self.peakHour = peakHour
        self.canPrev = canPrev
        self.canNext = canNext
    }
}

public enum AppDetailChart {
    public static func build(
        range: AppDetailRange,
        anchor: String,
        today: String,
        firstTracked: String?,
        weekStart: WeekStart,
        daySeconds: [String: Int64],
        opens: [AppDetailOpen]
    ) -> AppDetailChartData {
        let first = firstTracked ?? today
        let nav = PeriodNavigator(unit: unitOf(range), first: first, today: today, weekStart: weekStart)
        let clampedAnchor = nav.clamp(day: anchor)
        let (from, to) = period(range: range, anchor: clampedAnchor, today: today, first: first, weekStart: weekStart)

        var bars: [AppDetailBar] = []
        switch range {
        case .all:
            let fromYear = Int(from.prefix(4)) ?? 1970
            let toYear = Int(to.prefix(4)) ?? 1970
            for y in fromYear...toYear {
                let yStr = String(y)
                let start = "\(yStr)-01-01"
                let end = "\(yStr)-12-31"
                bars.append(bar(label: yStr, from: start, to: end, daySeconds: daySeconds, opens: opens))
            }
        case .year:
            let yStr = String(from.prefix(4))
            let monthNames = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"]
            let y = Int(yStr) ?? 1970
            let isLeap = (y % 4 == 0 && y % 100 != 0) || (y % 400 == 0)
            let daysInMonth = [31, isLeap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]
            for m in 1...12 {
                let start = String(format: "%04d-%02d-01", y, m)
                let end = String(format: "%04d-%02d-%02d", y, m, daysInMonth[m - 1])
                bars.append(bar(label: monthNames[m - 1], from: start, to: end, daySeconds: daySeconds, opens: opens))
            }
        case .matrix, .month, .week:
            var cur = from
            var cal = Calendar(identifier: .gregorian)
            cal.timeZone = TimeZone(secondsFromGMT: 0)!
            let shortWeekdays = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"]

            while cur <= to {
                let label: String
                if range == .week {
                    let comps = cur.split(separator: "-").compactMap { Int($0) }
                    if comps.count == 3, let d = cal.date(from: DateComponents(year: comps[0], month: comps[1], day: comps[2])) {
                        let weekdayIdx = cal.component(.weekday, from: d) - 1 // 1=Sun -> 0
                        label = shortWeekdays[weekdayIdx]
                    } else {
                        label = cur
                    }
                } else {
                    let dNum = Int(cur.suffix(2)) ?? 1
                    label = String(dNum)
                }
                bars.append(bar(label: label, from: cur, to: cur, daySeconds: daySeconds, opens: opens))
                cur = PeriodNavigator.stepBy(unit: .day, anchor: cur, dir: 1)
            }
        }

        var byHour = [Int](repeating: 0, count: 24)
        for o in opens {
            if o.date >= from && o.date <= to && o.hour >= 0 && o.hour < 24 {
                byHour[o.hour] += 1
            }
        }

        var peak: Int? = nil
        for h in 0..<24 {
            if byHour[h] > 0 && (peak == nil || byHour[h] > byHour[peak!]) {
                peak = h
            }
        }

        let canPrev = range != .all && nav.canPrev(anchor: clampedAnchor)
        let canNext = range != .all && nav.canNext(anchor: clampedAnchor)
        let total = bars.reduce(0) { $0 + $1.seconds }

        return AppDetailChartData(
            from: from,
            to: to,
            bars: bars,
            totalSeconds: total,
            opensByHour: byHour,
            peakHour: peak,
            canPrev: canPrev,
            canNext: canNext
        )
    }

    public static func step(range: AppDetailRange, anchor: String, dir: Int) -> String {
        range == .all ? anchor : PeriodNavigator.stepBy(unit: unitOf(range), anchor: anchor, dir: dir)
    }

    public static func nextRange(_ range: AppDetailRange) -> AppDetailRange {
        let allCases = AppDetailRange.allCases
        guard let idx = allCases.firstIndex(of: range) else { return .all }
        return allCases[(idx + 1) % allCases.count]
    }

    private static func unitOf(_ range: AppDetailRange) -> PeriodUnit {
        switch range {
        case .matrix, .year: return .year
        case .month: return .month
        case .week: return .week
        case .all: return .day
        }
    }

    private static func period(
        range: AppDetailRange,
        anchor: String,
        today: String,
        first: String,
        weekStart: WeekStart
    ) -> (from: String, to: String) {
        switch range {
        case .all:
            let fYear = String(first.prefix(4))
            let tYear = String(today.prefix(4))
            return ("\(fYear)-01-01", "\(tYear)-12-31")
        case .matrix, .year:
            let y = String(anchor.prefix(4))
            return ("\(y)-01-01", "\(y)-12-31")
        case .month:
            let ym = String(anchor.prefix(7))
            let y = Int(anchor.prefix(4)) ?? 1970
            let m = Int(anchor.dropFirst(5).prefix(2)) ?? 1
            let isLeap = (y % 4 == 0 && y % 100 != 0) || (y % 400 == 0)
            let daysInMonth = [31, isLeap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]
            return ("\(ym)-01", String(format: "%@-%02d", ym, daysInMonth[m - 1]))
        case .week:
            let w = WeekStarts.containing(day: anchor, weekStart: weekStart)
            let end = PeriodNavigator.stepBy(unit: .day, anchor: w, dir: 6)
            return (w, end)
        }
    }

    private static func bar(label: String, from: String, to: String, daySeconds: [String: Int64], opens: [AppDetailOpen]) -> AppDetailBar {
        var seconds: Int64 = 0
        var cur = from
        while cur <= to {
            seconds += daySeconds[cur] ?? 0
            cur = PeriodNavigator.stepBy(unit: .day, anchor: cur, dir: 1)
        }
        let sessions = opens.filter { $0.date >= from && $0.date <= to }.count
        return AppDetailBar(label: label, seconds: seconds, from: from, to: to, sessions: sessions)
    }
}

