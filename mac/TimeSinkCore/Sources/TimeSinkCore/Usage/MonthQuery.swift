import Foundation

public struct MonthAppAmount: Sendable, Equatable {
    public let appId: Int64
    public let name: String
    public let seconds: Int64

    public init(appId: Int64, name: String, seconds: Int64) {
        self.appId = appId
        self.name = name
        self.seconds = seconds
    }
}

public struct MonthDay: Sendable, Equatable {
    public let date: String
    public let seconds: Int64
    public let heat: Int
    public let isFuture: Bool
    public let isToday: Bool
    public let apps: [MonthAppAmount]
    public let deviceSeconds: [String: Int64]?
    public let onBothSeconds: Int64

    public init(
        date: String,
        seconds: Int64,
        heat: Int,
        isFuture: Bool,
        isToday: Bool,
        apps: [MonthAppAmount],
        deviceSeconds: [String: Int64]? = nil,
        onBothSeconds: Int64 = 0
    ) {
        self.date = date
        self.seconds = seconds
        self.heat = heat
        self.isFuture = isFuture
        self.isToday = isToday
        self.apps = apps
        self.deviceSeconds = deviceSeconds
        self.onBothSeconds = onBothSeconds
    }
}

public struct MonthWeek: Sendable, Equatable {
    public let days: [MonthDay?]
    public let totalSeconds: Int64

    public init(days: [MonthDay?], totalSeconds: Int64) {
        self.days = days
        self.totalSeconds = totalSeconds
    }
}

public struct BestDay: Sendable, Equatable {
    public let date: String
    public let seconds: Int64

    public init(date: String, seconds: Int64) {
        self.date = date
        self.seconds = seconds
    }
}

public struct MonthTopApp: Sendable, Equatable {
    public let appId: Int64
    public let name: String
    public let seconds: Int64
    public let previousSeconds: Int64
    public let deltaSeconds: Int64

    public init(appId: Int64, name: String, seconds: Int64, previousSeconds: Int64, deltaSeconds: Int64) {
        self.appId = appId
        self.name = name
        self.seconds = seconds
        self.previousSeconds = previousSeconds
        self.deltaSeconds = deltaSeconds
    }
}

public struct MonthResult: Sendable, Equatable {
    public let year: Int
    public let month: Int
    public let firstDay: String
    public let daysInMonth: Int
    public let daysCounted: Int
    public let isCurrent: Bool
    public let nowUtc: Int64
    public let days: [MonthDay]
    public let weeks: [MonthWeek]
    public let totalSeconds: Int64
    public let previousSeconds: Int64
    public let deltaSeconds: Int64
    public let dailyAverageSeconds: Double
    public let best: BestDay?
    public var bestDay: BestDay? { best }
    public let topApps: [MonthTopApp]
    public let defaultSelected: String
    public let firstTracked: String?
    public let deviceTotals: [String: Int64]?
    public let onBothSeconds: Int64

    public init(
        year: Int,
        month: Int,
        firstDay: String,
        daysInMonth: Int,
        daysCounted: Int,
        isCurrent: Bool,
        nowUtc: Int64,
        days: [MonthDay],
        weeks: [MonthWeek],
        totalSeconds: Int64,
        previousSeconds: Int64,
        deltaSeconds: Int64,
        dailyAverageSeconds: Double,
        best: BestDay?,
        topApps: [MonthTopApp],
        defaultSelected: String,
        firstTracked: String?,
        deviceTotals: [String: Int64]?,
        onBothSeconds: Int64
    ) {
        self.year = year
        self.month = month
        self.firstDay = firstDay
        self.daysInMonth = daysInMonth
        self.daysCounted = daysCounted
        self.isCurrent = isCurrent
        self.nowUtc = nowUtc
        self.days = days
        self.weeks = weeks
        self.totalSeconds = totalSeconds
        self.previousSeconds = previousSeconds
        self.deltaSeconds = deltaSeconds
        self.dailyAverageSeconds = dailyAverageSeconds
        self.best = best
        self.topApps = topApps
        self.defaultSelected = defaultSelected
        self.firstTracked = firstTracked
        self.deviceTotals = deviceTotals
        self.onBothSeconds = onBothSeconds
    }
}

public enum MonthQuery {
    public static func run(_ context: UsageContext, year: Int, month: Int) throws -> MonthResult {
        try UsageWindow.over(context: context) { window in
            run(window: window, year: year, month: month)
        }
    }

    public static func run(window: UsageWindow, year: Int, month: Int) -> MonthResult {
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
        let (todayYear, todayMonth, todayDay) = (todayComps[0], todayComps[1], todayComps[2])

        let firstDayStr = String(format: "%04d-%02d-01", year, month)
        let firstDate = calendar.date(from: DateComponents(year: year, month: month, day: 1))!
        let dim = calendar.range(of: .day, in: .month, for: firstDate)!.count

        let isCurrent = (todayYear == year && todayMonth == month)
        let counted: Int
        if isCurrent {
            counted = todayDay
        } else if year < todayYear || (year == todayYear && month < todayMonth) {
            counted = dim
        } else {
            counted = 0
        }

        let prevMonthDate = calendar.date(byAdding: .month, value: -1, to: firstDate)!
        let prevComps = calendar.dateComponents([.year, .month], from: prevMonthDate)
        let prevDim = calendar.range(of: .day, in: .month, for: prevMonthDate)!.count
        let prevCounted = min(counted, prevDim)

        let apps = window.apps
        let start = TrackingStart.of(apps: apps, zone: zone)

        let prevStartUtc = LocalDays.startOf(year: prevComps.year!, month: prevComps.month!, day: 1, timeZone: zone)
        let nextMonthDate = calendar.date(byAdding: .month, value: 1, to: firstDate)!
        let nextComps = calendar.dateComponents([.year, .month], from: nextMonthDate)
        let nextStartUtc = LocalDays.startOf(year: nextComps.year!, month: nextComps.month!, day: 1, timeZone: zone)

        let segs = window.segments(from: prevStartUtc, to: nextStartUtc)
        let uptimeBreakdown = UsageUptime.perDayBreakdown(
            segs: segs,
            zone: zone,
            nowUtc: nowUtc,
            onSinceUtc: onSinceUtc,
            gamesOnly: gamesOnly,
            localDeviceId: window.localDeviceId
        )
        let uptime = uptimeBreakdown.mapValues { $0.unionSeconds }

        let perDay = UsageDays.perDayGroup(segs, zone: zone)

        var monthDays: [MonthDay] = []
        var monthTotals: [Int64: Int64] = [:]
        var total: Int64 = 0
        var best: BestDay? = nil

        for d in 0..<dim {
            let dateStr = String(format: "%04d-%02d-%02d", year, month, d + 1)
            let grpMap = perDay[dateStr] ?? [:]
            var amounts: [MonthAppAmount] = grpMap.map { MonthAppAmount(appId: $0.key, name: UsageRank.nameOf(apps: apps, group: $0.key), seconds: $0.value) }
            amounts = UsageRank.rank(items: amounts, apps: apps, groupOf: { $0.appId }, amountOf: { $0.seconds })

            let bd = uptimeBreakdown[dateStr]
            let secs = bd?.unionSeconds ?? 0
            let devSecs = bd?.deviceSeconds
            let onBoth = bd?.onBothSeconds ?? 0

            for a in amounts {
                monthTotals[a.appId, default: 0] += a.seconds
            }
            total += secs

            if secs > 0 && (best == nil || secs > best!.seconds) {
                best = BestDay(date: dateStr, seconds: secs)
            }

            let isFut = (dateStr > todayStr)
            let isTod = (dateStr == todayStr)
            monthDays.append(MonthDay(
                date: dateStr,
                seconds: secs,
                heat: heatLevel(secs),
                isFuture: isFut,
                isToday: isTod,
                apps: amounts,
                deviceSeconds: devSecs,
                onBothSeconds: onBoth
            ))
        }

        var prevTotals: [Int64: Int64] = [:]
        var prevTotal: Int64 = 0
        for d in 0..<prevCounted {
            let pStr = String(format: "%04d-%02d-%02d", prevComps.year!, prevComps.month!, d + 1)
            let grpMap = perDay[pStr] ?? [:]
            for (g, s) in grpMap {
                prevTotals[g, default: 0] += s
            }
            prevTotal += uptime[pStr] ?? 0
        }

        var topAppIds = Array(monthTotals.keys)
        topAppIds.sort { a, b in
            let amtA = monthTotals[a] ?? 0
            let amtB = monthTotals[b] ?? 0
            if amtA != amtB { return amtA > amtB }
            return UsageRank.nameOf(apps: apps, group: a).localizedCaseInsensitiveCompare(UsageRank.nameOf(apps: apps, group: b)) == .orderedAscending
        }

        var topApps: [MonthTopApp] = []
        for g in topAppIds {
            let secs = monthTotals[g] ?? 0
            let prev = prevTotals[g] ?? 0
            topApps.append(MonthTopApp(appId: g, name: UsageRank.nameOf(apps: apps, group: g), seconds: secs, previousSeconds: prev, deltaSeconds: secs - prev))
        }

        // Layout weeks
        let targetWeekday: Int
        switch weekStart {
        case .mon: targetWeekday = 2
        case .sun: targetWeekday = 1
        case .sat: targetWeekday = 7
        }

        let firstWeekday = calendar.component(.weekday, from: firstDate)
        let padStart = (firstWeekday - targetWeekday + 7) % 7

        var weeks: [MonthWeek] = []
        var currentWeekCells: [MonthDay?] = [MonthDay?](repeating: nil, count: padStart)

        for d in 0..<dim {
            currentWeekCells.append(monthDays[d])
            if currentWeekCells.count == 7 {
                let wTotal = currentWeekCells.compactMap { $0?.seconds }.reduce(0, +)
                weeks.append(MonthWeek(days: currentWeekCells, totalSeconds: wTotal))
                currentWeekCells = []
            }
        }
        if !currentWeekCells.isEmpty {
            while currentWeekCells.count < 7 { currentWeekCells.append(nil) }
            let wTotal = currentWeekCells.compactMap { $0?.seconds }.reduce(0, +)
            weeks.append(MonthWeek(days: currentWeekCells, totalSeconds: wTotal))
        }

        var averageDays = 0
        for d in 0..<counted {
            let dStr = String(format: "%04d-%02d-%02d", year, month, d + 1)
            if start.counts(day: dStr) {
                averageDays += 1
            }
        }

        var monthDeviceTotals: [String: Int64] = [:]
        var monthOnBoth: Int64 = 0
        for md in monthDays {
            if let devMap = md.deviceSeconds {
                for (dev, s) in devMap {
                    monthDeviceTotals[dev, default: 0] += s
                }
            }
            monthOnBoth += md.onBothSeconds
        }

        let selected = isCurrent ? todayStr : (counted == 0 ? firstDayStr : String(format: "%04d-%02d-%02d", year, month, dim))

        return MonthResult(
            year: year,
            month: month,
            firstDay: firstDayStr,
            daysInMonth: dim,
            daysCounted: counted,
            isCurrent: isCurrent,
            nowUtc: nowUtc,
            days: monthDays,
            weeks: weeks,
            totalSeconds: total,
            previousSeconds: prevTotal,
            deltaSeconds: total - prevTotal,
            dailyAverageSeconds: averageDays > 0 ? Double(total) / Double(averageDays) : 0.0,
            best: best,
            topApps: topApps,
            defaultSelected: selected,
            firstTracked: start.firstDay,
            deviceTotals: monthDeviceTotals.isEmpty ? nil : monthDeviceTotals,
            onBothSeconds: monthOnBoth
        )
    }

    private static func heatLevel(_ seconds: Int64) -> Int {
        if seconds <= 0 { return 0 }
        let hours = Double(seconds) / 3600.0
        if hours < 1.0 { return 1 }
        if hours < 3.0 { return 2 }
        if hours < 5.0 { return 3 }
        return 4
    }
}

