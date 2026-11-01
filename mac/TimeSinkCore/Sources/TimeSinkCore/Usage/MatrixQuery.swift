import Foundation

public struct MatrixCell: Sendable, Equatable {
    public let seconds: Int64
    public let level: Int

    public init(seconds: Int64, level: Int) {
        self.seconds = seconds
        self.level = level
    }
}

public struct MatrixRow: Sendable, Equatable {
    public let appId: Int64
    public let name: String
    public let cells: [MatrixCell]
    public let totalSeconds: Int64
    public let sharePercent: Int
    public let peakMonth: Int
    public let peakSeconds: Int64

    public init(
        appId: Int64,
        name: String,
        cells: [MatrixCell],
        totalSeconds: Int64,
        sharePercent: Int,
        peakMonth: Int,
        peakSeconds: Int64
    ) {
        self.appId = appId
        self.name = name
        self.cells = cells
        self.totalSeconds = totalSeconds
        self.sharePercent = sharePercent
        self.peakMonth = peakMonth
        self.peakSeconds = peakSeconds
    }
}

public struct MatrixMonth: Sendable, Equatable {
    public let month: Int
    public let totalSeconds: Int64
    public let topApps: [YearAppShare]

    public init(month: Int, totalSeconds: Int64, topApps: [YearAppShare]) {
        self.month = month
        self.totalSeconds = totalSeconds
        self.topApps = topApps
    }
}

public struct MatrixResult: Sendable, Equatable {
    public let year: Int
    public let nowUtc: Int64
    public let trackingStarted: String?
    public let firstMonth: Int
    public let months: [MatrixMonth]
    public let rows: [MatrixRow]
    public let grandSeconds: Int64

    public init(
        year: Int,
        nowUtc: Int64,
        trackingStarted: String?,
        firstMonth: Int,
        months: [MatrixMonth],
        rows: [MatrixRow],
        grandSeconds: Int64
    ) {
        self.year = year
        self.nowUtc = nowUtc
        self.trackingStarted = trackingStarted
        self.firstMonth = firstMonth
        self.months = months
        self.rows = rows
        self.grandSeconds = grandSeconds
    }
}

public enum MatrixQuery {
    private static let topApps = 3

    public static func level(_ seconds: Int64, best: Int64) -> Int {
        if best <= 0 || seconds <= 0 { return 0 }
        let pct = seconds * 100
        if pct < 30 * best { return 0 }
        if pct < 55 * best { return 1 }
        if pct < 75 * best { return 2 }
        if pct < 90 * best { return 3 }
        return 4
    }

    public static func run(_ context: UsageContext) throws -> MatrixResult {
        try UsageWindow.over(context: context, run)
    }

    public static func run(window: UsageWindow) -> MatrixResult {
        let context = window.Context
        let zone = context.zone
        let gamesOnly = context.gamesOnly
        let onSinceUtc = context.signInUtc
        let nowUtc = window.nowUtc

        let todayStr = window.todayStr
        let todayComps = todayStr.split(separator: "-").compactMap { Int($0) }
        let (todayYear, todayMonth) = (todayComps[0], todayComps[1])

        let apps = window.apps
        let start = TrackingStart.of(apps: apps, zone: zone)
        guard let started = start.firstDay else {
            return MatrixResult(year: todayYear, nowUtc: nowUtc, trackingStarted: nil, firstMonth: 1, months: [], rows: [], grandSeconds: 0)
        }

        let first = (start.firstYear == todayYear) ? (start.firstMonth ?? 1) : 1
        let count = todayMonth - first + 1
        if count < 1 {
            return MatrixResult(year: todayYear, nowUtc: nowUtc, trackingStarted: started, firstMonth: first, months: [], rows: [], grandSeconds: 0)
        }

        let segsStartUtc = LocalDays.startOf(year: todayYear, month: first, day: 1, timeZone: zone)
        let segs = window.segments(from: segsStartUtc)
        let rollup = MonthRollup(segs: segs, zone: zone, nowUtc: nowUtc, onSinceUtc: onSinceUtc, gamesOnly: gamesOnly)

        var monthKeys: [MonthKey] = []
        for i in 0..<count {
            monthKeys.append(MonthKey(year: todayYear, month: first + i))
        }

        var perApp: [Int64: [Int64]] = [:]
        for i in 0..<count {
            for (grp, secs) in rollup.groups(monthKeys[i]) {
                if perApp[grp] == nil {
                    perApp[grp] = [Int64](repeating: 0, count: count)
                }
                perApp[grp]![i] += secs
            }
        }

        let uptimeByMonth = monthKeys.map { rollup.uptime($0) }
        let grand = uptimeByMonth.reduce(0, +)

        var unrankedRows: [MatrixRow] = []
        for (grpId, secsArr) in perApp where secsArr.reduce(0, +) > 0 {
            let best = secsArr.max() ?? 0
            let peakIdx = secsArr.firstIndex(of: best) ?? 0
            let cells = secsArr.map { MatrixCell(seconds: $0, level: level($0, best: best)) }
            let total = secsArr.reduce(0, +)
            let share = grand <= 0 ? 0 : Int(round(Double(total) * 100.0 / Double(grand)))
            unrankedRows.append(MatrixRow(
                appId: grpId,
                name: UsageRank.nameOf(apps: apps, group: grpId),
                cells: cells,
                totalSeconds: total,
                sharePercent: share,
                peakMonth: first + peakIdx,
                peakSeconds: best
            ))
        }

        let rankedRows = UsageRank.rank(items: unrankedRows, apps: apps, groupOf: { $0.appId }, amountOf: { $0.totalSeconds })

        var columns: [MatrixMonth] = []
        for i in 0..<count {
            let idx = i
            let top = rankedRows.filter { $0.cells[idx].seconds > 0 }
                .sorted { $0.cells[idx].seconds > $1.cells[idx].seconds }
                .prefix(topApps)
                .map { YearAppShare(appId: $0.appId, name: $0.name, seconds: $0.cells[idx].seconds) }
            columns.append(MatrixMonth(month: first + i, totalSeconds: uptimeByMonth[idx], topApps: Array(top)))
        }

        return MatrixResult(
            year: todayYear,
            nowUtc: nowUtc,
            trackingStarted: started,
            firstMonth: first,
            months: columns,
            rows: rankedRows,
            grandSeconds: grand
        )
    }
}

