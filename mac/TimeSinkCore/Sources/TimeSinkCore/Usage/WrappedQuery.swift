import Foundation

public struct WrappedApp: Sendable, Equatable {
    public let appId: Int64
    public let name: String
    public let seconds: Int64

    public init(appId: Int64, name: String, seconds: Int64) {
        self.appId = appId
        self.name = name
        self.seconds = seconds
    }
}

public struct WrappedKindTop: Sendable, Equatable {
    public let podium: [WrappedApp]
    public let others: [WrappedApp]
    public let seconds: Int64

    public init(podium: [WrappedApp], others: [WrappedApp], seconds: Int64) {
        self.podium = podium
        self.others = others
        self.seconds = seconds
    }
}

public struct WrappedResult: Sendable, Equatable {
    public let year: Int
    public let nowUtc: Int64
    public let trackingStarted: String?
    public let totalSeconds: Int64
    public let appSeconds: Int64
    public let daysTracked: Int
    public let daysWithActivity: Int
    public let appsTracked: Int
    public let podium: [WrappedApp]
    public let others: [WrappedApp]
    public let longestStreakDays: Int
    public let streakStart: String?
    public let streakEnd: String?
    public let busiestMonth: Int?
    public let busiestMonthSeconds: Int64
    public let peakHour: Int?
    public let peakHourSeconds: Int64
    public let beforeNoonPercent: Int
    public let gamesTop: WrappedKindTop?
    public let gachaTop: WrappedKindTop?
    public let longestSessions: [WrappedApp]?
    public let deviceTotals: [String: Int64]?
    public let onBothSeconds: Int64

    public var equivalentDays: Int { Int(totalSeconds / 86400) }
    public var equivalentHours: Int { Int((totalSeconds % 86400) / 3600) }
    public var totalHours: Int64 { totalSeconds / 3600 }

    public init(
        year: Int,
        nowUtc: Int64,
        trackingStarted: String?,
        totalSeconds: Int64,
        appSeconds: Int64,
        daysTracked: Int,
        daysWithActivity: Int,
        appsTracked: Int,
        podium: [WrappedApp],
        others: [WrappedApp],
        longestStreakDays: Int,
        streakStart: String?,
        streakEnd: String?,
        busiestMonth: Int?,
        busiestMonthSeconds: Int64,
        peakHour: Int?,
        peakHourSeconds: Int64,
        beforeNoonPercent: Int,
        gamesTop: WrappedKindTop?,
        gachaTop: WrappedKindTop?,
        longestSessions: [WrappedApp]?,
        deviceTotals: [String: Int64]?,
        onBothSeconds: Int64
    ) {
        self.year = year
        self.nowUtc = nowUtc
        self.trackingStarted = trackingStarted
        self.totalSeconds = totalSeconds
        self.appSeconds = appSeconds
        self.daysTracked = daysTracked
        self.daysWithActivity = daysWithActivity
        self.appsTracked = appsTracked
        self.podium = podium
        self.others = others
        self.longestStreakDays = longestStreakDays
        self.streakStart = streakStart
        self.streakEnd = streakEnd
        self.busiestMonth = busiestMonth
        self.busiestMonthSeconds = busiestMonthSeconds
        self.peakHour = peakHour
        self.peakHourSeconds = peakHourSeconds
        self.beforeNoonPercent = beforeNoonPercent
        self.gamesTop = gamesTop
        self.gachaTop = gachaTop
        self.longestSessions = longestSessions
        self.deviceTotals = deviceTotals
        self.onBothSeconds = onBothSeconds
    }
}

public enum WrappedQuery {
    private static let podiumSize = 5
    private static let othersSize = 3

    public static func run(_ context: UsageContext) throws -> WrappedResult {
        try UsageWindow.over(context: context, run)
    }

    public static func run(window: UsageWindow) -> WrappedResult {
        let context = window.Context
        let zone = context.zone
        let gamesOnly = context.gamesOnly
        let onSinceUtc = context.signInUtc
        let nowUtc = window.nowUtc

        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = zone

        let todayStr = window.todayStr
        let todayComps = todayStr.split(separator: "-").compactMap { Int($0) }
        let (todayYear, todayMonth, todayDay) = (todayComps[0], todayComps[1], todayComps[2])
        let todayDate = calendar.date(from: DateComponents(year: todayYear, month: todayMonth, day: todayDay))!

        let yearStart = LocalDays.startOf(year: todayYear, month: 1, day: 1, timeZone: zone)
        let windowStart = LocalDays.startOf(year: todayYear - 1, month: 12, day: 1, timeZone: zone)

        let apps = window.apps
        let start = TrackingStart.of(apps: apps, zone: zone)
        let first = start.firstDay
        let groups = Set(apps.values.map(\.group))

        var perDay: [String: Int64] = [:]
        var perHour = [Int64](repeating: 0, count: 24)
        var perGroup: [Int64: Int64] = [:]
        var beforeNoon: Int64 = 0
        var totalAppSeconds: Int64 = 0

        let segs = window.segments(from: windowStart)
        var windowed: [UsageSeg] = []
        for s in segs {
            let st = max(s.start, windowStart)
            let en = min(s.end, nowUtc)
            if en > st {
                windowed.append(UsageSeg(
                    group: s.group,
                    appId: s.appId,
                    start: st,
                    end: en,
                    open: s.open,
                    sleep: s.sleep,
                    deviceId: s.deviceId,
                    off: s.off
                ))
            }
        }

        let months = MonthRollup(segs: windowed, zone: zone, nowUtc: nowUtc, onSinceUtc: onSinceUtc, gamesOnly: gamesOnly)
        for sl in UsageDays.slices(windowed, zone: zone) {
            let len = sl.end - sl.start
            if sl.year != todayYear { continue }
            perDay[sl.day, default: 0] += len
            perGroup[sl.seg.group, default: 0] += len
            totalAppSeconds += len
            for p in UsageDays.hourPieces(from: sl.start, to: sl.end, zone: zone) {
                if p.hour >= 0 && p.hour < 24 {
                    perHour[p.hour] += (p.end - p.start)
                    if p.hour < 12 {
                        beforeNoon += (p.end - p.start)
                    }
                }
            }
        }

        let breakdownPerDay = UsageUptime.perDayBreakdown(segs: segs, zone: zone, nowUtc: nowUtc, onSinceUtc: onSinceUtc, gamesOnly: gamesOnly, localDeviceId: window.localDeviceId)
            .filter { kv in
                let parts = kv.key.split(separator: "-").compactMap { Int($0) }
                return parts.first == todayYear
            }

        let uptime = breakdownPerDay.values.reduce(0) { $0 + $1.unionSeconds }
        var wrappedDeviceTotals: [String: Int64] = [:]
        var wrappedOnBoth: Int64 = 0
        for bd in breakdownPerDay.values {
            for (dev, s) in bd.deviceSeconds {
                wrappedDeviceTotals[dev, default: 0] += s
            }
            wrappedOnBoth += bd.onBothSeconds
        }

        var nonBgApps: [WrappedApp] = []
        for (grpId, secs) in perGroup where !UsageRank.isBackground(apps: apps, group: grpId) {
            nonBgApps.append(WrappedApp(appId: grpId, name: UsageRank.nameOf(apps: apps, group: grpId), seconds: secs))
        }
        let ranked = UsageRank.rank(items: nonBgApps, apps: apps, groupOf: { $0.appId }, amountOf: { $0.seconds })

        func topOfKind(_ kind: AppKind) -> WrappedKindTop {
            let list = ranked.filter { UsageRank.kindOf(apps: apps, group: $0.appId) == kind }
            let p = Array(list.prefix(podiumSize))
            let o = Array(list.dropFirst(podiumSize).prefix(othersSize))
            let sumSecs = list.reduce(0) { $0 + $1.seconds }
            return WrappedKindTop(podium: p, others: o, seconds: sumSecs)
        }

        var longestSessionApps: [WrappedApp] = []
        var groupLongest: [Int64: Int64] = [:]
        for s in segs where s.end > yearStart && !UsageRank.isBackground(apps: apps, group: s.group) {
            let dur = s.end - s.start
            if dur > (groupLongest[s.group] ?? 0) {
                groupLongest[s.group] = dur
            }
        }
        for (grpId, dur) in groupLongest where dur > 0 {
            longestSessionApps.append(WrappedApp(appId: grpId, name: UsageRank.nameOf(apps: apps, group: grpId), seconds: dur))
        }
        let longestRanked = UsageRank.rank(items: longestSessionApps, apps: apps, groupOf: { $0.appId }, amountOf: { $0.seconds })
        let longest = Array(longestRanked.prefix(podiumSize))

        // Longest run of consecutive local days
        let activeDays = perDay.filter { $0.value > 0 }.map(\.key).sorted()
        var bestLen = 0
        var bestStartDay = ""
        var bestEndDay = ""
        var runLen = 0
        var runStartDay = ""

        var prevDayDate: Date? = nil
        for dayStr in activeDays {
            let comps = dayStr.split(separator: "-").compactMap { Int($0) }
            let curDate = calendar.date(from: DateComponents(year: comps[0], month: comps[1], day: comps[2]))!
            if let prev = prevDayDate, calendar.date(byAdding: .day, value: 1, to: prev) == curDate {
                runLen += 1
            } else {
                runLen = 1
                runStartDay = dayStr
            }
            prevDayDate = curDate
            if runLen > bestLen {
                bestLen = runLen
                bestStartDay = runStartDay
                bestEndDay = dayStr
            }
        }

        var busiest: Int? = nil
        var busiestSecs: Int64 = 0
        for m in 1...12 {
            let secs = months.appSeconds(MonthKey(year: todayYear, month: m))
            if secs > busiestSecs {
                busiest = m
                busiestSecs = secs
            }
        }

        var peak: Int? = nil
        var peakSecs: Int64 = 0
        for h in 0..<24 {
            if perHour[h] > peakSecs {
                peak = h
                peakSecs = perHour[h]
            }
        }

        var daysTracked = 0
        if start.firstDay != nil {
            let fromStr = start.since(day: String(format: "%04d-01-01", todayYear))
            let fromComps = fromStr.split(separator: "-").compactMap { Int($0) }
            let fromDate = calendar.date(from: DateComponents(year: fromComps[0], month: fromComps[1], day: fromComps[2]))!
            daysTracked = max(0, calendar.dateComponents([.day], from: fromDate, to: todayDate).day! + 1)
        }

        let beforeNoonPct = (totalAppSeconds == 0) ? 0 : Int(round(Double(beforeNoon) * 100.0 / Double(totalAppSeconds)))

        return WrappedResult(
            year: todayYear,
            nowUtc: nowUtc,
            trackingStarted: first,
            totalSeconds: uptime,
            appSeconds: totalAppSeconds,
            daysTracked: daysTracked,
            daysWithActivity: activeDays.count,
            appsTracked: groups.count,
            podium: Array(ranked.prefix(podiumSize)),
            others: Array(ranked.dropFirst(podiumSize).prefix(othersSize)),
            longestStreakDays: bestLen,
            streakStart: bestLen == 0 ? nil : bestStartDay,
            streakEnd: bestLen == 0 ? nil : bestEndDay,
            busiestMonth: busiest,
            busiestMonthSeconds: busiestSecs,
            peakHour: peak,
            peakHourSeconds: peakSecs,
            beforeNoonPercent: beforeNoonPct,
            gamesTop: topOfKind(.game),
            gachaTop: topOfKind(.gacha),
            longestSessions: longest,
            deviceTotals: wrappedDeviceTotals.isEmpty ? nil : wrappedDeviceTotals,
            onBothSeconds: wrappedOnBoth
        )
    }
}

