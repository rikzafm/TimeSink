import Foundation

public struct TodaySession: Sendable, Equatable {
    public let startUtc: Int64
    public let endUtc: Int64
    public let isOpen: Bool
    public let beganUtc: Int64?
    public let deviceId: String

    public var seconds: Int64 { max(0, endUtc - startUtc) }
    public var carriesOver: Bool { (beganUtc ?? startUtc) < startUtc }
    public var fullStartUtc: Int64 { beganUtc ?? startUtc }
    public var fullSeconds: Int64 { max(0, endUtc - fullStartUtc) }

    public init(startUtc: Int64, endUtc: Int64, isOpen: Bool, beganUtc: Int64? = nil, deviceId: String = "") {
        self.startUtc = startUtc
        self.endUtc = endUtc
        self.isOpen = isOpen
        self.beganUtc = beganUtc
        self.deviceId = deviceId
    }
}

public struct DeviceHour: Sendable, Equatable {
    public let appSeconds: Int64
    public let distinctApps: Int

    public init(appSeconds: Int64, distinctApps: Int) {
        self.appSeconds = appSeconds
        self.distinctApps = distinctApps
    }
}

public struct HourBucket: Sendable, Equatable {
    public let hour: Int
    public let appSeconds: Int64
    public let typicalAppSeconds: Double
    public let byDevice: [String: DeviceHour]?

    public init(hour: Int, appSeconds: Int64, typicalAppSeconds: Double, byDevice: [String: DeviceHour]? = nil) {
        self.hour = hour
        self.appSeconds = appSeconds
        self.typicalAppSeconds = typicalAppSeconds
        self.byDevice = byDevice
    }
}

public struct FirstAppTile: Sendable, Equatable {
    public let appId: Int64
    public let appName: String
    public let startUtc: Int64

    public init(appId: Int64, appName: String, startUtc: Int64) {
        self.appId = appId
        self.appName = appName
        self.startUtc = startUtc
    }
}

public struct LongestSessionTile: Sendable, Equatable {
    public let appId: Int64
    public let appName: String
    public let seconds: Int64

    public init(appId: Int64, appName: String, seconds: Int64) {
        self.appId = appId
        self.appName = appName
        self.seconds = seconds
    }
}

public struct DeviceUptime: Sendable, Equatable {
    public let deviceId: String
    public let uptimeSeconds: Int64
    public let lastActiveUtc: Int64?
    public let isLive: Bool

    public init(deviceId: String, uptimeSeconds: Int64, lastActiveUtc: Int64? = nil, isLive: Bool = false) {
        self.deviceId = deviceId
        self.uptimeSeconds = uptimeSeconds
        self.lastActiveUtc = lastActiveUtc
        self.isLive = isLive
    }
}

public struct TodayAppRow: Sendable, Equatable {
    public let appId: Int64
    public let name: String
    public let seconds: Int64
    public let isRunning: Bool
    public let runningSinceUtc: Int64?
    public let lastClosedUtc: Int64?
    public let sessions: [TodaySession]
    public let sevenDayAverageSeconds: Double?
    public let sevenDayDays: Int
    public let deviceSeconds: [String: Int64]?
    public let onBothSeconds: Int64

    public init(
        appId: Int64,
        name: String,
        seconds: Int64,
        isRunning: Bool,
        runningSinceUtc: Int64?,
        lastClosedUtc: Int64?,
        sessions: [TodaySession],
        sevenDayAverageSeconds: Double?,
        sevenDayDays: Int,
        deviceSeconds: [String: Int64]? = nil,
        onBothSeconds: Int64 = 0
    ) {
        self.appId = appId
        self.name = name
        self.seconds = seconds
        self.isRunning = isRunning
        self.runningSinceUtc = runningSinceUtc
        self.lastClosedUtc = lastClosedUtc
        self.sessions = sessions
        self.sevenDayAverageSeconds = sevenDayAverageSeconds
        self.sevenDayDays = sevenDayDays
        self.deviceSeconds = deviceSeconds
        self.onBothSeconds = onBothSeconds
    }
}

public struct TodayResult: Sendable, Equatable {
    public let date: String
    public let dayStartUtc: Int64
    public let dayEndUtc: Int64
    public let nowUtc: Int64
    public let totalSeconds: Int64
    public let runningCount: Int
    public let openSessionCount: Int
    public let hours: [HourBucket]
    public let hasTypical: Bool
    public let sleepBands: [SleepBand]
    public let firstApp: FirstAppTile?
    public let longestSession: LongestSessionTile?
    public let totalVsYesterdaySeconds: Int64
    public let rows: [TodayAppRow]
    public let uptimeSeconds: Int64
    public let uptimeVsYesterdaySeconds: Int64
    public let firstTrackedDay: String?
    public let gamesOnly: Bool
    public let deviceUptimes: [String: DeviceUptime]?
    public let onBothSeconds: Int64

    public init(
        date: String,
        dayStartUtc: Int64,
        dayEndUtc: Int64,
        nowUtc: Int64,
        totalSeconds: Int64,
        runningCount: Int,
        openSessionCount: Int,
        hours: [HourBucket],
        hasTypical: Bool,
        sleepBands: [SleepBand],
        firstApp: FirstAppTile?,
        longestSession: LongestSessionTile?,
        totalVsYesterdaySeconds: Int64,
        rows: [TodayAppRow],
        uptimeSeconds: Int64,
        uptimeVsYesterdaySeconds: Int64,
        firstTrackedDay: String?,
        gamesOnly: Bool,
        deviceUptimes: [String: DeviceUptime]?,
        onBothSeconds: Int64
    ) {
        self.date = date
        self.dayStartUtc = dayStartUtc
        self.dayEndUtc = dayEndUtc
        self.nowUtc = nowUtc
        self.totalSeconds = totalSeconds
        self.runningCount = runningCount
        self.openSessionCount = openSessionCount
        self.hours = hours
        self.hasTypical = hasTypical
        self.sleepBands = sleepBands
        self.firstApp = firstApp
        self.longestSession = longestSession
        self.totalVsYesterdaySeconds = totalVsYesterdaySeconds
        self.rows = rows
        self.uptimeSeconds = uptimeSeconds
        self.uptimeVsYesterdaySeconds = uptimeVsYesterdaySeconds
        self.firstTrackedDay = firstTrackedDay
        self.gamesOnly = gamesOnly
        self.deviceUptimes = deviceUptimes
        self.onBothSeconds = onBothSeconds
    }
}

public enum TodayQuery {
    private static let typicalWeeks = 8
    private static let averageDays = 7
    private static let hoursInDay = 24

    public static func run(_ context: UsageContext, forDay: String? = nil) throws -> TodayResult {
        try UsageWindow.over(context: context) { window in
            run(window: window, forDay: forDay)
        }
    }

    public static func run(window: UsageWindow, forDay: String? = nil, useSignIn: Bool = true) -> TodayResult {
        let context = window.Context
        let now = context.now
        let zone = context.zone
        let gamesOnly = context.gamesOnly
        let onSinceUtc = useSignIn ? context.signInUtc : nil
        let nowUtc = window.nowUtc

        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = zone

        let todayStr = window.todayStr
        let targetStr = (forDay != nil && forDay! < todayStr) ? forDay! : todayStr
        let isToday = (targetStr == todayStr)

        let targetComps = targetStr.split(separator: "-").compactMap { Int($0) }
        let (tYear, tMonth, tDay) = (targetComps[0], targetComps[1], targetComps[2])
        let d0 = LocalDays.startOf(year: tYear, month: tMonth, day: tDay, timeZone: zone)
        let d1 = LocalDays.startOfNextDay(for: d0, timeZone: zone)

        let apps = window.apps
        let targetDate = calendar.date(from: DateComponents(year: tYear, month: tMonth, day: tDay))!
        let lookbackDate = calendar.date(byAdding: .day, value: -typicalWeeks * 7, to: targetDate)!
        let lookbackUtc = Int64(calendar.startOfDay(for: lookbackDate).timeIntervalSince1970)

        let segs = window.segments(from: lookbackUtc)

        // Hourly chart & typical day
        let todayHours = UsageDays.hourSums(segs: segs, year: tYear, month: tMonth, day: tDay, zone: zone)
        let start = TrackingStart.of(apps: apps, zone: zone)
        var typical = [Double](repeating: 0.0, count: hoursInDay)
        var typicalDays = 0

        for k in 1...typicalWeeks {
            guard let pastDate = calendar.date(byAdding: .day, value: -7 * k, to: targetDate) else { continue }
            let pastComps = calendar.dateComponents([.year, .month, .day], from: pastDate)
            guard let py = pastComps.year, let pm = pastComps.month, let pd = pastComps.day else { continue }
            let pastStr = String(format: "%04d-%02d-%02d", py, pm, pd)
            if !start.counts(day: pastStr) { continue }
            let sums = UsageDays.hourSums(segs: segs, year: py, month: pm, day: pd, zone: zone)
            for h in 0..<hoursInDay {
                typical[h] += Double(sums[h])
            }
            typicalDays += 1
        }

        var hourlyDeviceSeconds = [[String: Int64]](repeating: [:], count: hoursInDay)
        var hourlyDeviceApps = [[String: Set<Int64>]](repeating: [:], count: hoursInDay)

        for s in segs {
            let a = max(s.start, d0)
            let b = min(s.end, d1)
            if b <= a { continue }
            let dev = s.deviceId.isEmpty ? (apps[s.appId]?.deviceId ?? "") : s.deviceId
            for p in UsageDays.hourPieces(from: a, to: b, zone: zone) {
                let h = p.hour
                if h < 0 || h >= hoursInDay { continue }
                hourlyDeviceSeconds[h][dev, default: 0] += (p.end - p.start)
                hourlyDeviceApps[h][dev, default: []].insert(s.appId)
            }
        }

        var hourBuckets: [HourBucket] = []
        for h in 0..<hoursInDay {
            var byDev: [String: DeviceHour] = [:]
            let allDevs = Set(hourlyDeviceSeconds[h].keys).union(hourlyDeviceApps[h].keys)
            for dev in allDevs {
                let devSecs = hourlyDeviceSeconds[h][dev] ?? 0
                let count = hourlyDeviceApps[h][dev]?.count ?? 0
                byDev[dev] = DeviceHour(appSeconds: devSecs, distinctApps: count)
            }
            hourBuckets.append(HourBucket(
                hour: h,
                appSeconds: todayHours[h],
                typicalAppSeconds: typicalDays == 0 ? 0.0 : typical[h] / Double(typicalDays),
                byDevice: byDev.isEmpty ? nil : byDev
            ))
        }

        // App rows
        var byGroup: [Int64: [UsageSeg]] = [:]
        for s in segs {
            byGroup[s.group, default: []].append(s)
        }

        var rows: [TodayAppRow] = []
        for (grpId, groupSegs) in byGroup {
            let sessions = groupSegs
                .filter { UsageDays.overlap($0, d0, d1) > 0 || ($0.open && $0.end >= d0) }
                .sorted { $0.start < $1.start }
                .map { s in
                    let dev = s.deviceId.isEmpty ? (apps[s.appId]?.deviceId ?? "") : s.deviceId
                    return TodaySession(
                        startUtc: max(s.start, d0),
                        endUtc: min(s.end, d1),
                        isOpen: s.open && isToday,
                        beganUtc: s.start < d0 ? s.start : nil,
                        deviceId: dev
                    )
                }

            let open = isToday ? groupSegs.filter { $0.open } : []
            let seconds = groupSegs.reduce(0) { $0 + UsageDays.overlap($1, d0, d1) }
            if seconds == 0 && open.isEmpty { continue }

            var devSecs: [String: Int64] = [:]
            for s in groupSegs {
                let dev = s.deviceId.isEmpty ? (apps[s.appId]?.deviceId ?? "") : s.deviceId
                let sec = UsageDays.overlap(s, d0, d1)
                if sec > 0 {
                    devSecs[dev, default: 0] += sec
                }
            }
            var rowOnBoth: Int64 = 0
            if devSecs.count > 1 {
                let unionSecs = UsageUptime.openSeconds(segs: groupSegs, from: d0, to: d1)
                rowOnBoth = max(0, seconds - unionSecs)
            }

            let closedToday = groupSegs.filter { !$0.open && $0.end >= d0 && $0.end < d1 }
            let groupApps = apps.values.filter { $0.group == grpId }
            let addedUtc = groupApps.map(\.addedUtc).min() ?? 0
            let addedStr = LocalDays.dayString(for: addedUtc, timeZone: zone)

            var avgDays = 0
            var avgSum: Int64 = 0
            for k in 1...averageDays {
                guard let pastDate = calendar.date(byAdding: .day, value: -k, to: targetDate) else { continue }
                let pastComps = calendar.dateComponents([.year, .month, .day], from: pastDate)
                guard let py = pastComps.year, let pm = pastComps.month, let pd = pastComps.day else { continue }
                let pastStr = String(format: "%04d-%02d-%02d", py, pm, pd)
                if pastStr < addedStr { continue }
                avgDays += 1
                let a = LocalDays.startOf(year: py, month: pm, day: pd, timeZone: zone)
                let b = LocalDays.startOfNextDay(for: a, timeZone: zone)
                avgSum += groupSegs.reduce(0) { $0 + UsageDays.overlap($1, a, b) }
            }

            let avg: Double? = avgDays > 0 ? Double(avgSum) / Double(avgDays) : nil

            rows.append(TodayAppRow(
                appId: grpId,
                name: UsageRank.nameOf(apps: apps, group: grpId),
                seconds: seconds,
                isRunning: !open.isEmpty,
                runningSinceUtc: open.map(\.start).min(),
                lastClosedUtc: closedToday.map(\.end).max(),
                sessions: sessions,
                sevenDayAverageSeconds: avg,
                sevenDayDays: avgDays,
                deviceSeconds: devSecs.isEmpty ? nil : devSecs,
                onBothSeconds: rowOnBoth
            ))
        }

        if isToday {
            rows.sort { a, b in
                if a.isRunning != b.isRunning { return a.isRunning }
                let bgA = UsageRank.isBackground(apps: apps, group: a.appId)
                let bgB = UsageRank.isBackground(apps: apps, group: b.appId)
                if bgA != bgB { return !bgA }
                if a.isRunning {
                    if a.seconds != b.seconds { return a.seconds > b.seconds }
                } else {
                    let lcA = a.lastClosedUtc ?? 0
                    let lcB = b.lastClosedUtc ?? 0
                    if lcA != lcB { return lcA > lcB }
                }
                if a.seconds != b.seconds { return a.seconds > b.seconds }
                return a.name.localizedCaseInsensitiveCompare(b.name) == .orderedAscending
            }
        } else {
            rows = UsageRank.rank(items: rows, apps: apps, groupOf: { $0.appId }, amountOf: { $0.seconds })
        }

        // Tiles
        var firstTile: FirstAppTile? = nil
        let started = segs.filter { $0.start >= d0 && $0.start < d1 }.sorted {
            if $0.start != $1.start { return $0.start < $1.start }
            return $0.appId < $1.appId
        }.first
        if let st = started {
            firstTile = FirstAppTile(appId: st.group, appName: UsageRank.nameOf(apps: apps, group: st.group), startUtc: st.start)
        }

        var longestTile: LongestSessionTile? = nil
        let best = segs.max(by: { UsageDays.overlap($0, d0, d1) < UsageDays.overlap($1, d0, d1) })
        if let b = best, UsageDays.overlap(b, d0, d1) > 0 {
            longestTile = LongestSessionTile(appId: b.group, appName: UsageRank.nameOf(apps: apps, group: b.group), seconds: UsageDays.overlap(b, d0, d1))
        }

        let total = rows.reduce(0) { $0 + $1.seconds }

        let yesterdayDate = calendar.date(byAdding: .day, value: -1, to: targetDate)!
        let yComps = calendar.dateComponents([.year, .month, .day], from: yesterdayDate)
        let y0 = LocalDays.startOf(year: yComps.year!, month: yComps.month!, day: yComps.day!, timeZone: zone)
        let cut: Int64
        if isToday {
            let timeOfDaySecs = Int64(calendar.component(.hour, from: now) * 3600 + calendar.component(.minute, from: now) * 60 + calendar.component(.second, from: now))
            let wallClockYesterday = y0 + timeOfDaySecs
            cut = min(wallClockYesterday, d0)
        } else {
            cut = d0
        }
        let yesterdayByNow = segs.reduce(0) { $0 + UsageDays.overlap($1, y0, cut) }

        // Uptime
        let dayEnd = isToday ? nowUtc : d1
        let breakdown = UsageUptime.breakdown(
            segs: segs,
            from: d0,
            to: dayEnd,
            live: isToday,
            onSince: onSinceUtc,
            gamesOnly: gamesOnly,
            localDeviceId: window.localDeviceId
        )
        let uptime = breakdown.unionSeconds
        let onBoth = breakdown.onBothSeconds

        var devUptimes: [String: DeviceUptime] = [:]
        for (devId, devSecs) in breakdown.deviceSeconds {
            let devSegs = segs.filter {
                let d = $0.deviceId.isEmpty ? (apps[$0.appId]?.deviceId ?? "") : $0.deviceId
                return d == devId
            }
            let lastActive = devSegs.map(\.end).max()
            let isLive = isToday && (devSegs.contains { $0.open } || (devId.caseInsensitiveCompare(window.localDeviceId ?? "") == .orderedSame && onSinceUtc != nil))
            devUptimes[devId] = DeviceUptime(deviceId: devId, uptimeSeconds: devSecs, lastActiveUtc: lastActive, isLive: isLive)
        }

        let vsYesterdayBreakdown = UsageUptime.breakdown(
            segs: segs,
            from: d0,
            to: dayEnd,
            live: isToday,
            onSince: nil,
            gamesOnly: gamesOnly,
            localDeviceId: window.localDeviceId
        )
        let yesterdayBreakdown = UsageUptime.breakdown(
            segs: segs,
            from: y0,
            to: d0,
            live: false,
            onSince: nil,
            gamesOnly: gamesOnly,
            localDeviceId: window.localDeviceId
        )
        let uptimeVs = vsYesterdayBreakdown.unionSeconds - yesterdayBreakdown.unionSeconds

        let openSegs = isToday ? segs.filter { $0.open } : []
        let runningGroupCount = Set(openSegs.map(\.group)).count

        return TodayResult(
            date: targetStr,
            dayStartUtc: d0,
            dayEndUtc: d1,
            nowUtc: nowUtc,
            totalSeconds: total,
            runningCount: runningGroupCount,
            openSessionCount: openSegs.count,
            hours: hourBuckets,
            hasTypical: typicalDays > 0,
            sleepBands: UsageUptime.sleepBands(segs: segs, d0: d0, nowUtc: min(nowUtc, d1)),
            firstApp: firstTile,
            longestSession: longestTile,
            totalVsYesterdaySeconds: total - yesterdayByNow,
            rows: rows,
            uptimeSeconds: uptime,
            uptimeVsYesterdaySeconds: uptimeVs,
            firstTrackedDay: start.firstDay,
            gamesOnly: gamesOnly,
            deviceUptimes: devUptimes.isEmpty ? nil : devUptimes,
            onBothSeconds: onBoth
        )
    }
}

