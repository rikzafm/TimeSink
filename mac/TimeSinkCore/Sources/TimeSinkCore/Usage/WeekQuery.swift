import Foundation

public struct WeekDay: Sendable, Equatable {
    public let date: String
    public let seconds: Int64
    public let isToday: Bool
    public let isFuture: Bool
    public let deviceSeconds: [String: Int64]?
    public let onBothSeconds: Int64

    public init(
        date: String,
        seconds: Int64,
        isToday: Bool,
        isFuture: Bool,
        deviceSeconds: [String: Int64]? = nil,
        onBothSeconds: Int64 = 0
    ) {
        self.date = date
        self.seconds = seconds
        self.isToday = isToday
        self.isFuture = isFuture
        self.deviceSeconds = deviceSeconds
        self.onBothSeconds = onBothSeconds
    }
}

public struct HeatCell: Sendable, Equatable {
    public let date: String
    public let seconds: Int64
    public let level: Int
    public let isFuture: Bool

    public init(date: String, seconds: Int64, level: Int, isFuture: Bool) {
        self.date = date
        self.seconds = seconds
        self.level = level
        self.isFuture = isFuture
    }
}

public struct WeekAppRow: Sendable, Equatable {
    public let appId: Int64
    public let name: String
    public let seconds: Int64
    public let sparkline: [Int64]

    public init(appId: Int64, name: String, seconds: Int64, sparkline: [Int64]) {
        self.appId = appId
        self.name = name
        self.seconds = seconds
        self.sparkline = sparkline
    }
}

public struct WeekResult: Sendable, Equatable {
    public let today: String
    public let shownWeekStart: String
    public let nowUtc: Int64
    public let totalSeconds: Int64
    public let days: [WeekDay]
    public let heatGrid: [HeatCell]
    public let streakDays: Int
    public let rows: [WeekAppRow]
    public let weekTotals: [Int64]
    public let previousWeekTotalSeconds: Int64
    public let firstTrackedDay: String?
    public let deviceTotals: [String: Int64]?
    public let onBothSeconds: Int64

    public init(
        today: String,
        shownWeekStart: String,
        nowUtc: Int64,
        totalSeconds: Int64,
        days: [WeekDay],
        heatGrid: [HeatCell],
        streakDays: Int,
        rows: [WeekAppRow],
        weekTotals: [Int64],
        previousWeekTotalSeconds: Int64,
        firstTrackedDay: String?,
        deviceTotals: [String: Int64]?,
        onBothSeconds: Int64
    ) {
        self.today = today
        self.shownWeekStart = shownWeekStart
        self.nowUtc = nowUtc
        self.totalSeconds = totalSeconds
        self.days = days
        self.heatGrid = heatGrid
        self.streakDays = streakDays
        self.rows = rows
        self.weekTotals = weekTotals
        self.previousWeekTotalSeconds = previousWeekTotalSeconds
        self.firstTrackedDay = firstTrackedDay
        self.deviceTotals = deviceTotals
        self.onBothSeconds = onBothSeconds
    }
}

public enum WeekQuery {
    public static let gridWeeks = 20
    public static let sparkDays = 10
    public static let streakLookbackDays = 365

    public static func run(_ context: UsageContext, weeksBack: Int = 0) throws -> WeekResult {
        try UsageWindow.over(context: context) { window in
            run(window: window, weeksBack: weeksBack)
        }
    }

    public static func run(window: UsageWindow, weeksBack: Int = 0) -> WeekResult {
        let context = window.Context
        let zone = context.zone
        let gamesOnly = context.gamesOnly
        let weekStart = context.weekStart
        let onSinceUtc = context.signInUtc
        let nowUtc = window.nowUtc

        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = zone

        let todayStr = window.todayStr
        let todayComps = todayStr.split(separator: "-").compactMap { Int($0) }
        let todayDate = calendar.date(from: DateComponents(year: todayComps[0], month: todayComps[1], day: todayComps[2]))!

        // Find start of week for todayDate based on weekStart
        // weekday: 1 = Sun, 2 = Mon, 7 = Sat
        let targetWeekday: Int
        switch weekStart {
        case .mon: targetWeekday = 2
        case .sun: targetWeekday = 1
        case .sat: targetWeekday = 7
        }

        let currentWeekday = calendar.component(.weekday, from: todayDate)
        let daysBack = (currentWeekday - targetWeekday + 7) % 7
        let thisWeekStartDate = calendar.date(byAdding: .day, value: -daysBack, to: todayDate)!
        let gridStartDate = calendar.date(byAdding: .day, value: -7 * (gridWeeks - 1), to: thisWeekStartDate)!
        let shownWeekStartDate = calendar.date(byAdding: .day, value: -7 * weeksBack, to: thisWeekStartDate)!

        let streakMinDate = calendar.date(byAdding: .day, value: -streakLookbackDays, to: todayDate)!
        let prevWeekMinDate = calendar.date(byAdding: .day, value: -7, to: shownWeekStartDate)!

        let lookStartDate = min(gridStartDate, min(streakMinDate, prevWeekMinDate))
        let lookStartUtc = Int64(calendar.startOfDay(for: lookStartDate).timeIntervalSince1970)

        let apps = window.apps
        let segs = window.segments(from: lookStartUtc)
        let uptimeBreakdown = UsageUptime.perDayBreakdown(
            segs: segs,
            zone: zone,
            nowUtc: nowUtc,
            onSinceUtc: onSinceUtc,
            gamesOnly: gamesOnly,
            localDeviceId: window.localDeviceId
        )
        let uptime = uptimeBreakdown.mapValues { $0.unionSeconds }

        var perDay: [String: Int64] = [:]
        var perGroupDay: [String: [Int64: Int64]] = [:]

        for slice in UsageDays.slices(segs, zone: zone) {
            let overlap = slice.end - slice.start
            perDay[slice.day, default: 0] += overlap
            perGroupDay[slice.day, default: [:]][slice.seg.group, default: 0] += overlap
        }

        var weekDays: [WeekDay] = []
        for i in 0..<7 {
            let dDate = calendar.date(byAdding: .day, value: i, to: shownWeekStartDate)!
            let comps = calendar.dateComponents([.year, .month, .day], from: dDate)
            let dStr = String(format: "%04d-%02d-%02d", comps.year!, comps.month!, comps.day!)
            let isTod = (dStr == todayStr)
            let isFut = (dStr > todayStr)
            let bd = uptimeBreakdown[dStr]
            let secs = isFut ? 0 : (bd?.unionSeconds ?? 0)
            let devSecs = isFut ? nil : bd?.deviceSeconds
            let onBoth = isFut ? 0 : (bd?.onBothSeconds ?? 0)
            weekDays.append(WeekDay(
                date: dStr,
                seconds: secs,
                isToday: isTod,
                isFuture: isFut,
                deviceSeconds: devSecs,
                onBothSeconds: onBoth
            ))
        }

        var heat: [HeatCell] = []
        for i in 0..<(gridWeeks * 7) {
            let dDate = calendar.date(byAdding: .day, value: i, to: gridStartDate)!
            let comps = calendar.dateComponents([.year, .month, .day], from: dDate)
            let dStr = String(format: "%04d-%02d-%02d", comps.year!, comps.month!, comps.day!)
            let isFut = (dStr > todayStr)
            let secs = isFut ? 0 : (uptime[dStr] ?? 0)
            let level = isFut ? 0 : heatLevel(secs)
            heat.append(HeatCell(date: dStr, seconds: secs, level: level, isFuture: isFut))
        }

        var weekTotals: [Int64] = []
        for w in 0..<gridWeeks {
            let total = heat[(w * 7)..<((w + 1) * 7)].reduce(0) { $0 + $1.seconds }
            weekTotals.append(total)
        }

        var prevWeekTotal: Int64 = 0
        for (i, d) in weekDays.enumerated() {
            if !d.isFuture {
                let prevDDate = calendar.date(byAdding: .day, value: i - 7, to: shownWeekStartDate)!
                let comps = calendar.dateComponents([.year, .month, .day], from: prevDDate)
                let prevDStr = String(format: "%04d-%02d-%02d", comps.year!, comps.month!, comps.day!)
                prevWeekTotal += uptime[prevDStr] ?? 0
            }
        }

        var streak = 0
        var cursorDate = (perDay[todayStr] ?? 0) > 0 ? todayDate : calendar.date(byAdding: .day, value: -1, to: todayDate)!
        while streak < streakLookbackDays {
            let comps = calendar.dateComponents([.year, .month, .day], from: cursorDate)
            let cStr = String(format: "%04d-%02d-%02d", comps.year!, comps.month!, comps.day!)
            if (perDay[cStr] ?? 0) > 0 {
                streak += 1
                cursorDate = calendar.date(byAdding: .day, value: -1, to: cursorDate)!
            } else {
                break
            }
        }

        var allGroups: Set<Int64> = []
        for (_, grps) in perGroupDay {
            for (g, _) in grps { allGroups.insert(g) }
        }

        var rows: [WeekAppRow] = []
        for g in allGroups {
            var weekSum: Int64 = 0
            for d in weekDays {
                weekSum += perGroupDay[d.date]?[g] ?? 0
            }
            if weekSum == 0 { continue }

            var spark: [Int64] = []
            for i in 0..<sparkDays {
                let sDate = calendar.date(byAdding: .day, value: i - (sparkDays - 1), to: todayDate)!
                let comps = calendar.dateComponents([.year, .month, .day], from: sDate)
                let sStr = String(format: "%04d-%02d-%02d", comps.year!, comps.month!, comps.day!)
                spark.append(perGroupDay[sStr]?[g] ?? 0)
            }
            rows.append(WeekAppRow(appId: g, name: UsageRank.nameOf(apps: apps, group: g), seconds: weekSum, sparkline: spark))
        }
        rows = UsageRank.rank(items: rows, apps: apps, groupOf: { $0.appId }, amountOf: { $0.seconds })

        var weekDeviceTotals: [String: Int64] = [:]
        var weekOnBoth: Int64 = 0
        for d in weekDays {
            if let devMap = d.deviceSeconds {
                for (dev, s) in devMap {
                    weekDeviceTotals[dev, default: 0] += s
                }
            }
            weekOnBoth += d.onBothSeconds
        }

        let firstTracked = TrackingStart.of(apps: apps, zone: zone).firstDay
        let shownWeekStartComps = calendar.dateComponents([.year, .month, .day], from: shownWeekStartDate)
        let shownWeekStartStr = String(format: "%04d-%02d-%02d", shownWeekStartComps.year!, shownWeekStartComps.month!, shownWeekStartComps.day!)

        return WeekResult(
            today: todayStr,
            shownWeekStart: shownWeekStartStr,
            nowUtc: nowUtc,
            totalSeconds: weekDays.reduce(0) { $0 + $1.seconds },
            days: weekDays,
            heatGrid: heat,
            streakDays: streak,
            rows: rows,
            weekTotals: weekTotals,
            previousWeekTotalSeconds: prevWeekTotal,
            firstTrackedDay: firstTracked,
            deviceTotals: weekDeviceTotals.isEmpty ? nil : weekDeviceTotals,
            onBothSeconds: weekOnBoth
        )
    }

    private static func heatLevel(_ seconds: Int64) -> Int {
        if seconds <= 0 { return 0 }
        let hours = Double(seconds) / 3600.0
        if hours < 1.0 { return 1 }
        if hours < 3.0 { return 2 }
        if hours < 6.0 { return 3 }
        return 4
    }
}

