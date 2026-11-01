import Foundation

public enum AppDetailDayKind: String, Sendable, Codable, Equatable {
    case tracked
    case beforeTracking
    case future
}

public struct AppDetailDay: Sendable, Codable, Equatable {
    public let date: String
    public let seconds: Int64
    public let sessions: Int
    public let level: Int
    public let kind: AppDetailDayKind

    public init(date: String, seconds: Int64, sessions: Int, level: Int, kind: AppDetailDayKind) {
        self.date = date
        self.seconds = seconds
        self.sessions = sessions
        self.level = level
        self.kind = kind
    }
}

public struct AppDetailOpen: Sendable, Codable, Equatable {
    public let date: String
    public let hour: Int

    public init(date: String, hour: Int) {
        self.date = date
        self.hour = hour
    }
}

public struct AppDetailLast: Sendable, Codable, Equatable {
    public let startUtc: Int64
    public let seconds: Int64
    public let running: Bool

    public init(startUtc: Int64, seconds: Int64, running: Bool) {
        self.startUtc = startUtc
        self.seconds = seconds
        self.running = running
    }
}

public struct AppDetailResult: Sendable, Codable, Equatable {
    public let appId: Int64
    public let name: String
    public let exeName: String
    public let firstTracked: String?
    public let last: AppDetailLast?
    public let totalSeconds: Int64
    public let sessionCount: Int
    public let averageSessionSeconds: Int64
    public let longestSessionSeconds: Int64
    public let year: Int
    public let firstYear: Int
    public let lastYear: Int
    public let yearSeconds: Int64
    public let peakDaySeconds: Int64
    public let days: [AppDetailDay]
    public let background: Bool
    public let daySeconds: [String: Int64]?
    public let opens: [AppDetailOpen]?
    public let deviceSeconds: [String: Int64]?

    public init(
        appId: Int64,
        name: String,
        exeName: String,
        firstTracked: String?,
        last: AppDetailLast?,
        totalSeconds: Int64,
        sessionCount: Int,
        averageSessionSeconds: Int64,
        longestSessionSeconds: Int64,
        year: Int,
        firstYear: Int,
        lastYear: Int,
        yearSeconds: Int64,
        peakDaySeconds: Int64,
        days: [AppDetailDay],
        background: Bool = false,
        daySeconds: [String: Int64]? = nil,
        opens: [AppDetailOpen]? = nil,
        deviceSeconds: [String: Int64]? = nil
    ) {
        self.appId = appId
        self.name = name
        self.exeName = exeName
        self.firstTracked = firstTracked
        self.last = last
        self.totalSeconds = totalSeconds
        self.sessionCount = sessionCount
        self.averageSessionSeconds = averageSessionSeconds
        self.longestSessionSeconds = longestSessionSeconds
        self.year = year
        self.firstYear = firstYear
        self.lastYear = lastYear
        self.yearSeconds = yearSeconds
        self.peakDaySeconds = peakDaySeconds
        self.days = days
        self.background = background
        self.daySeconds = daySeconds
        self.opens = opens
        self.deviceSeconds = deviceSeconds
    }
}

public enum AppDetailQuery {
    public static func level(seconds: Int64, peak: Int64) -> Int {
        if seconds <= 0 || peak <= 0 { return 0 }
        let pct = seconds * 100
        if pct <= 25 * peak { return 1 }
        if pct <= 50 * peak { return 2 }
        if pct <= 75 * peak { return 3 }
        return 4
    }

    public static func run(context: UsageContext, appId: Int64, year: Int) -> AppDetailResult? {
        try? UsageWindow.over(context: context) { run(window: $0, appId: appId, year: year) }
    }

    internal static func run(window: UsageWindow, appId: Int64, year: Int) -> AppDetailResult? {
        let zone = window.context.zone
        let nowUtc = window.nowUtc
        let today = window.todayStr

        let apps = window.allApps
        guard let app = apps[appId], !app.isRetired, app.group == appId else { return nil }
        let name = app.name
        let cleanPath = app.exePath.replacingOccurrences(of: "\\", with: "/")
        let exe = (cleanPath as NSString).lastPathComponent
        let background = app.background

        let segs = window.segments(from: 0, includeRetired: true)
            .filter { $0.group == appId && $0.end > $0.start }

        var daySeconds: [String: Int64] = [:]
        var daySessions: [String: Int] = [:]
        var total: Int64 = 0
        var longest: Int64 = 0

        for seg in segs {
            longest = max(longest, seg.end - seg.start)
        }

        for slice in UsageDays.slices(segs, zone: zone) {
            let secs = slice.end - slice.start
            daySeconds[slice.day, default: 0] += secs
            daySessions[slice.day, default: 0] += 1
            total += secs
        }

        let first: String? = daySeconds.keys.min()
        let todayYear: Int = Int(today.prefix(4)) ?? 1970
        let firstYear: Int
        if let first = first, let y = Int(first.prefix(4)) {
            firstYear = y
        } else {
            firstYear = todayYear
        }

        let shown = min(max(year, min(firstYear, todayYear)), todayYear)

        var lastOpened: AppDetailLast? = nil
        if let s = segs.max(by: { $0.start < $1.start }) {
            lastOpened = AppDetailLast(startUtc: s.start, seconds: s.end - s.start, running: s.open)
        }

        func isLeapYear(_ y: Int) -> Bool {
            (y % 4 == 0 && y % 100 != 0) || (y % 400 == 0)
        }
        let daysInMonth = [31, isLeapYear(shown) ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]

        var yearDates: [String] = []
        yearDates.reserveCapacity(366)
        for m in 1...12 {
            let dim = daysInMonth[m - 1]
            for d in 1...dim {
                yearDates.append(String(format: "%04d-%02d-%02d", shown, m, d))
            }
        }

        var yearSeconds: Int64 = 0
        var peak: Int64 = 0
        for dStr in yearDates {
            let secs = daySeconds[dStr] ?? 0
            yearSeconds += secs
            peak = max(peak, secs)
        }

        var days: [AppDetailDay] = []
        days.reserveCapacity(366)
        for dStr in yearDates {
            let kind: AppDetailDayKind
            if dStr > today {
                kind = .future
            } else if first == nil || dStr < first! {
                kind = .beforeTracking
            } else {
                kind = .tracked
            }
            let secs = daySeconds[dStr] ?? 0
            days.append(AppDetailDay(
                date: dStr,
                seconds: secs,
                sessions: daySessions[dStr] ?? 0,
                level: kind == .tracked ? level(seconds: secs, peak: peak) : 0,
                kind: kind
            ))
        }

        var deviceSeconds: [String: Int64] = [:]
        for s in segs {
            let dev = s.deviceId.isEmpty ? (apps[s.appId]?.deviceId ?? "") : s.deviceId
            if !dev.isEmpty {
                deviceSeconds[dev, default: 0] += (s.end - s.start)
            }
        }

        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = zone
        let opens: [AppDetailOpen] = segs.map { s in
            let dStr = LocalDays.dayString(for: s.start, timeZone: zone)
            let hour = cal.component(.hour, from: Date(timeIntervalSince1970: TimeInterval(s.start)))
            return AppDetailOpen(date: dStr, hour: hour)
        }

        return AppDetailResult(
            appId: appId,
            name: name,
            exeName: exe,
            firstTracked: first,
            last: lastOpened,
            totalSeconds: total,
            sessionCount: segs.count,
            averageSessionSeconds: segs.count > 0 ? total / Int64(segs.count) : 0,
            longestSessionSeconds: longest,
            year: shown,
            firstYear: firstYear,
            lastYear: todayYear,
            yearSeconds: yearSeconds,
            peakDaySeconds: peak,
            days: days,
            background: background,
            daySeconds: daySeconds.isEmpty ? nil : daySeconds,
            opens: opens.isEmpty ? nil : opens,
            deviceSeconds: deviceSeconds.isEmpty ? nil : deviceSeconds
        )
    }
}
