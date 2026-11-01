import Foundation

public struct AllBestMonth: Sendable, Equatable {
    public let year: Int
    public let month: Int
    public let seconds: Int64

    public init(year: Int, month: Int, seconds: Int64) {
        self.year = year
        self.month = month
        self.seconds = seconds
    }
}

public struct AllAppRow: Sendable, Equatable {
    public let appId: Int64
    public let name: String
    public let seconds: Int64
    public let daysUsed: Int
    public let averageSecondsPerDay: Double
    public let share: Double
    public let firstTracked: String?
    public let bestMonth: AllBestMonth?
    public let longestSessionSeconds: Int64
    public let background: Bool
    public let sessionCount: Int
    public let lastUsedUtc: Int64
    public let devices: [String]?

    public var averageSessionSeconds: Double {
        sessionCount > 0 ? Double(seconds) / Double(sessionCount) : 0.0
    }

    public init(
        appId: Int64,
        name: String,
        seconds: Int64,
        daysUsed: Int,
        averageSecondsPerDay: Double,
        share: Double,
        firstTracked: String?,
        bestMonth: AllBestMonth?,
        longestSessionSeconds: Int64,
        background: Bool = false,
        sessionCount: Int = 0,
        lastUsedUtc: Int64 = 0,
        devices: [String]? = nil
    ) {
        self.appId = appId
        self.name = name
        self.seconds = seconds
        self.daysUsed = daysUsed
        self.averageSecondsPerDay = averageSecondsPerDay
        self.share = share
        self.firstTracked = firstTracked
        self.bestMonth = bestMonth
        self.longestSessionSeconds = longestSessionSeconds
        self.background = background
        self.sessionCount = sessionCount
        self.lastUsedUtc = lastUsedUtc
        self.devices = devices
    }
}

public struct AllRetiredRow: Sendable, Equatable {
    public let appId: Int64
    public let name: String
    public let seconds: Int64
    public let daysUsed: Int
    public let averageSecondsPerDay: Double
    public let stoppedDate: String

    public init(
        appId: Int64,
        name: String,
        seconds: Int64,
        daysUsed: Int,
        averageSecondsPerDay: Double,
        stoppedDate: String
    ) {
        self.appId = appId
        self.name = name
        self.seconds = seconds
        self.daysUsed = daysUsed
        self.averageSecondsPerDay = averageSecondsPerDay
        self.stoppedDate = stoppedDate
    }
}

public struct AllTotals: Sendable, Equatable {
    public let seconds: Int64
    public let daysUsed: Int
    public let averageSecondsPerDay: Double
    public let deviceTotals: [String: Int64]?
    public let onBothSeconds: Int64

    public init(
        seconds: Int64,
        daysUsed: Int,
        averageSecondsPerDay: Double,
        deviceTotals: [String: Int64]? = nil,
        onBothSeconds: Int64 = 0
    ) {
        self.seconds = seconds
        self.daysUsed = daysUsed
        self.averageSecondsPerDay = averageSecondsPerDay
        self.deviceTotals = deviceTotals
        self.onBothSeconds = onBothSeconds
    }
}

public struct AllResult: Sendable, Equatable {
    public let apps: [AllAppRow]
    public let retired: [AllRetiredRow]
    public let totals: AllTotals

    public init(apps: [AllAppRow], retired: [AllRetiredRow], totals: AllTotals) {
        self.apps = apps
        self.retired = retired
        self.totals = totals
    }
}

public enum AllQuery {
    public static let minRetiredSeconds: Int64 = 60

    private final class GroupState {
        let id: Int64
        let name: String
        let retired: Bool
        let background: Bool
        let retiredUtc: Int64
        var seconds: Int64 = 0
        var longest: Int64 = 0
        var sessions: Int = 0
        var lastUsed: Int64 = 0
        var days: Set<String> = []
        var months: [MonthKey: Int64] = [:]

        init(id: Int64, name: String, retired: Bool, background: Bool, retiredUtc: Int64) {
            self.id = id
            self.name = name
            self.retired = retired
            self.background = background
            self.retiredUtc = retiredUtc
        }
    }

    public static func run(_ context: UsageContext) throws -> AllResult {
        try UsageWindow.over(context: context, run)
    }

    public static func run(window: UsageWindow) -> AllResult {
        let context = window.Context
        let zone = context.zone
        let gamesOnly = context.gamesOnly
        let onSinceUtc = context.signInUtc
        let nowUtc = window.nowUtc

        let usageApps = window.allApps
        var groups: [Int64: GroupState] = [:]
        for a in usageApps.values where a.group == a.id {
            groups[a.id] = GroupState(
                id: a.id,
                name: a.name,
                retired: a.isRetired,
                background: a.background,
                retiredUtc: a.retiredUtc
            )
        }

        let loaded = window.segments(from: 0, includeRetired: true)
        var segs: [UsageSeg] = []

        for seg in loaded {
            if seg.end <= seg.start { continue }
            if let grp = groups[seg.group] {
                grp.sessions += 1
                grp.lastUsed = max(grp.lastUsed, seg.end)
                grp.longest = max(grp.longest, seg.end - seg.start)
            }
            segs.append(seg)
        }

        for slice in UsageDays.slices(segs, zone: zone) {
            if let grp = groups[slice.seg.group] {
                let s = slice.end - slice.start
                grp.seconds += s
                grp.days.insert(slice.day)
                let mKey = MonthKey(year: slice.year, month: slice.month)
                grp.months[mKey, default: 0] += s
            }
        }

        var trackedDays: Set<String> = []
        var total: Int64 = 0
        for grp in groups.values where !grp.retired {
            total += grp.seconds
            trackedDays.formUnion(grp.days)
        }

        var appRows: [AllAppRow] = []
        for g in groups.values.filter({ !$0.retired }).sorted(by: { $0.id < $1.id }) {
            let memberDevs = usageApps.members(group: g.id).map(\.deviceId).filter { !$0.isEmpty }
            let segDevs = segs.filter { $0.group == g.id && !$0.deviceId.isEmpty }.map(\.deviceId)
            let devList = Array(Set(memberDevs + segDevs)).sorted()

            let firstTrk = g.days.min()
            let bestM: AllBestMonth?
            if g.months.isEmpty {
                bestM = nil
            } else {
                let topM = g.months.max { a, b in
                    if a.value != b.value { return a.value < b.value }
                    return a.key > b.key
                }!
                bestM = AllBestMonth(year: topM.key.year, month: topM.key.month, seconds: topM.value)
            }

            let avgPerDay = g.days.isEmpty ? 0.0 : Double(g.seconds) / Double(g.days.count)
            let share = total > 0 ? Double(g.seconds) / Double(total) : 0.0

            appRows.append(AllAppRow(
                appId: g.id,
                name: g.name,
                seconds: g.seconds,
                daysUsed: g.days.count,
                averageSecondsPerDay: avgPerDay,
                share: share,
                firstTracked: firstTrk,
                bestMonth: bestM,
                longestSessionSeconds: g.longest,
                background: g.background,
                sessionCount: g.sessions,
                lastUsedUtc: g.lastUsed,
                devices: devList.isEmpty ? nil : devList
            ))
        }

        var retiredRows: [AllRetiredRow] = []
        let retiredGroups = groups.values.filter { $0.retired && $0.seconds >= minRetiredSeconds }
            .sorted { (a, b) in
                if a.retiredUtc != b.retiredUtc { return a.retiredUtc > b.retiredUtc }
                return a.id < b.id
            }

        for g in retiredGroups {
            let avgPerDay = g.days.isEmpty ? 0.0 : Double(g.seconds) / Double(g.days.count)
            let stopStr = LocalDays.dayString(for: g.retiredUtc, timeZone: zone)
            retiredRows.append(AllRetiredRow(
                appId: g.id,
                name: g.name,
                seconds: g.seconds,
                daysUsed: g.days.count,
                averageSecondsPerDay: avgPerDay,
                stoppedDate: stopStr
            ))
        }

        let nonRetiredLoaded = loaded.filter { !(usageApps[$0.appId]?.isRetired ?? false) }
        let uptimeBreakdown = UsageUptime.perDayBreakdown(
            segs: nonRetiredLoaded,
            zone: zone,
            nowUtc: nowUtc,
            onSinceUtc: onSinceUtc,
            gamesOnly: gamesOnly,
            localDeviceId: window.localDeviceId
        )

        let uptime = uptimeBreakdown.values.reduce(0) { $0 + $1.unionSeconds }
        var allDevTotals: [String: Int64] = [:]
        var allOnBoth: Int64 = 0
        for bd in uptimeBreakdown.values {
            for (dev, s) in bd.deviceSeconds {
                allDevTotals[dev, default: 0] += s
            }
            allOnBoth += bd.onBothSeconds
        }

        let avgTracked = trackedDays.isEmpty ? 0.0 : Double(uptime) / Double(trackedDays.count)
        let totals = AllTotals(
            seconds: uptime,
            daysUsed: trackedDays.count,
            averageSecondsPerDay: avgTracked,
            deviceTotals: allDevTotals.isEmpty ? nil : allDevTotals,
            onBothSeconds: allOnBoth
        )

        return AllResult(apps: appRows, retired: retiredRows, totals: totals)
    }
}

