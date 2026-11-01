import Foundation

public enum YearMonthKind: String, Sendable {
    case beforeTracking
    case tracked
    case current
    case future
}

public struct YearAppShare: Sendable, Equatable {
    public let appId: Int64
    public let name: String
    public let seconds: Int64

    public init(appId: Int64, name: String, seconds: Int64) {
        self.appId = appId
        self.name = name
        self.seconds = seconds
    }
}

public struct YearMonth: Sendable, Equatable {
    public let month: Int
    public let kind: YearMonthKind
    public let seconds: Int64
    public let projectedSeconds: Int64
    public let previousSeconds: Int64?
    public let topApps: [YearAppShare]
    public let deviceSeconds: [String: Int64]?
    public let onBothSeconds: Int64

    public init(
        month: Int,
        kind: YearMonthKind,
        seconds: Int64,
        projectedSeconds: Int64,
        previousSeconds: Int64?,
        topApps: [YearAppShare],
        deviceSeconds: [String: Int64]? = nil,
        onBothSeconds: Int64 = 0
    ) {
        self.month = month
        self.kind = kind
        self.seconds = seconds
        self.projectedSeconds = projectedSeconds
        self.previousSeconds = previousSeconds
        self.topApps = topApps
        self.deviceSeconds = deviceSeconds
        self.onBothSeconds = onBothSeconds
    }
}

public struct YearResult: Sendable, Equatable {
    public let year: Int
    public let nowUtc: Int64
    public let trackingStarted: String?
    public let months: [YearMonth]
    public let totalSeconds: Int64
    public let averageMonthSeconds: Int64?
    public let bestMonth: Int?
    public let bestMonthSeconds: Int64
    public let paceYearSeconds: Int64?
    public let recentMonthCount: Int
    public let deviceTotals: [String: Int64]?
    public let onBothSeconds: Int64

    public init(
        year: Int,
        nowUtc: Int64,
        trackingStarted: String?,
        months: [YearMonth],
        totalSeconds: Int64,
        averageMonthSeconds: Int64?,
        bestMonth: Int?,
        bestMonthSeconds: Int64,
        paceYearSeconds: Int64?,
        recentMonthCount: Int,
        deviceTotals: [String: Int64]?,
        onBothSeconds: Int64
    ) {
        self.year = year
        self.nowUtc = nowUtc
        self.trackingStarted = trackingStarted
        self.months = months
        self.totalSeconds = totalSeconds
        self.averageMonthSeconds = averageMonthSeconds
        self.bestMonth = bestMonth
        self.bestMonthSeconds = bestMonthSeconds
        self.paceYearSeconds = paceYearSeconds
        self.recentMonthCount = recentMonthCount
        self.deviceTotals = deviceTotals
        self.onBothSeconds = onBothSeconds
    }
}

public enum YearQuery {
    private static let recentMonths = 3

    public static func run(_ context: UsageContext) throws -> YearResult {
        try UsageWindow.over(context: context, run)
    }

    public static func run(window: UsageWindow) -> YearResult {
        let context = window.Context
        let now = context.now
        let zone = context.zone
        let gamesOnly = context.gamesOnly
        let onSinceUtc = context.signInUtc
        let nowUtc = window.nowUtc

        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = zone

        let todayStr = window.todayStr
        let todayComps = todayStr.split(separator: "-").compactMap { Int($0) }
        let (todayYear, todayMonth, todayDay) = (todayComps[0], todayComps[1], todayComps[2])

        let cur = MonthKey(year: todayYear, month: todayMonth)
        let firstMonth = MonthKey(year: todayYear, month: 1).addMonths(-(recentMonths - 1))

        let apps = window.apps
        let start = TrackingStart.of(apps: apps, zone: zone)
        let first = start.firstDay

        var startMonth: MonthKey? = nil
        if let f = first {
            startMonth = MonthKey.of(dayStr: f)
        }

        func beforeTracking(_ m: MonthKey) -> Bool {
            guard let s = startMonth else { return true }
            return m < s
        }

        var partialFirst: MonthKey? = nil
        if let y = start.firstYear, let m = start.firstMonth, start.isPartialMonth(year: y, month: m) {
            partialFirst = startMonth
        }

        let firstMonthUtc = LocalDays.startOf(year: firstMonth.year, month: firstMonth.month, day: 1, timeZone: zone)
        let segs = window.segments(from: firstMonthUtc)
        let rollup = MonthRollup(segs: segs, zone: zone, nowUtc: nowUtc, onSinceUtc: onSinceUtc, gamesOnly: gamesOnly, localDeviceId: window.localDeviceId)

        let curSecs = rollup.uptime(cur)
        var curProj = curSecs
        let hasPace = (first != nil)

        let todayDate = calendar.date(from: DateComponents(year: todayYear, month: todayMonth, day: todayDay))!
        let dim = calendar.range(of: .day, in: .month, for: todayDate)!.count

        if hasPace {
            let monthFirstStr = String(format: "%04d-%02d-01", todayYear, todayMonth)
            let fromStr = start.since(day: monthFirstStr)
            let fromParts = fromStr.split(separator: "-").compactMap { Int($0) }
            let fromDate = calendar.date(from: DateComponents(year: fromParts[0], month: fromParts[1], day: fromParts[2]))!
            let elapsedDays = max(1, calendar.dateComponents([.day], from: fromDate, to: todayDate).day! + 1)
            curProj = Int64(round(Double(curSecs) / Double(elapsedDays) * Double(dim)))
        }

        var recent: [Int64] = []
        var walker = cur.addMonths(-(recentMonths - 1))
        while walker <= cur {
            if !beforeTracking(walker) {
                if walker == cur {
                    recent.Add(curProj)
                } else if walker != partialFirst {
                    recent.Add(rollup.uptime(walker))
                }
            }
            walker = walker.addMonths(1)
        }

        let ghost: Int64 = recent.isEmpty ? 0 : Int64(round(Double(recent.reduce(0, +)) / Double(recent.count)))

        var months: [YearMonth] = []
        for m in 1...12 {
            let monthKey = MonthKey(year: todayYear, month: m)
            let kind: YearMonthKind
            if beforeTracking(monthKey) {
                kind = .beforeTracking
            } else if monthKey > cur {
                kind = .future
            } else if monthKey == cur {
                kind = .current
            } else {
                kind = .tracked
            }

            let secs = (kind == .tracked || kind == .current) ? rollup.uptime(monthKey) : 0
            let proj: Int64
            switch kind {
            case .current: proj = curProj
            case .future: proj = ghost
            default: proj = secs
            }

            var prev: Int64? = nil
            if (kind == .tracked || kind == .current) && !beforeTracking(monthKey.prev) {
                prev = rollup.uptime(monthKey.prev)
            }

            var top: [YearAppShare] = []
            if kind == .tracked || kind == .current {
                let shares = rollup.groups(monthKey)
                if !shares.isEmpty {
                    var items = shares.map { YearAppShare(appId: $0.key, name: UsageRank.nameOf(apps: apps, group: $0.key), seconds: $0.value) }
                    items = UsageRank.rank(items: items, apps: apps, groupOf: { $0.appId }, amountOf: { $0.seconds })
                    top = items
                }
            }

            let devSecs = (kind == .tracked || kind == .current) ? rollup.deviceUptime(monthKey) : nil
            let onBoth = (kind == .tracked || kind == .current) ? rollup.onBoth(monthKey) : 0
            months.append(YearMonth(
                month: m,
                kind: kind,
                seconds: secs,
                projectedSeconds: proj,
                previousSeconds: prev,
                topApps: top,
                deviceSeconds: (devSecs != nil && !devSecs!.isEmpty) ? devSecs : nil,
                onBothSeconds: onBoth
            ))
        }

        let total = months.reduce(0) { $0 + $1.seconds }
        var yearDeviceTotals: [String: Int64] = [:]
        var yearOnBoth: Int64 = 0
        for ym in months {
            if let devMap = ym.deviceSeconds {
                for (dev, s) in devMap {
                    yearDeviceTotals[dev, default: 0] += s
                }
            }
            yearOnBoth += ym.onBothSeconds
        }

        let fullMonths = months.filter { $0.kind == .tracked && MonthKey(year: todayYear, month: $0.month) != partialFirst }
        let average: Int64? = fullMonths.isEmpty ? nil : Int64(round(Double(fullMonths.reduce(0) { $0 + $1.seconds }) / Double(fullMonths.count)))

        let best = months.filter { ($0.kind == .tracked || $0.kind == .current) && $0.seconds > 0 }
            .max(by: { $0.seconds < $1.seconds })

        let pace: Int64? = hasPace
            ? (total + (curProj - curSecs) + months.filter { $0.kind == .future }.reduce(0) { $0 + $1.projectedSeconds })
            : nil

        return YearResult(
            year: todayYear,
            nowUtc: nowUtc,
            trackingStarted: first,
            months: months,
            totalSeconds: total,
            averageMonthSeconds: average,
            bestMonth: best?.month,
            bestMonthSeconds: best?.seconds ?? 0,
            paceYearSeconds: pace,
            recentMonthCount: hasPace ? recent.count : 0,
            deviceTotals: yearDeviceTotals.isEmpty ? nil : yearDeviceTotals,
            onBothSeconds: yearOnBoth
        )
    }
}

private extension Array {
    mutating func Add(_ element: Element) {
        append(element)
    }
}

