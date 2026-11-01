import Foundation

public struct MonthKey: Hashable, Comparable, Sendable {
    public let year: Int
    public let month: Int

    public init(year: Int, month: Int) {
        self.year = year
        self.month = month
    }

    public static func of(year: Int, month: Int) -> MonthKey {
        MonthKey(year: year, month: month)
    }

    public static func of(dayStr: String) -> MonthKey {
        let parts = dayStr.split(separator: "-").compactMap { Int($0) }
        return MonthKey(year: parts[0], month: parts[1])
    }

    public var prev: MonthKey {
        addMonths(-1)
    }

    public func addMonths(_ n: Int) -> MonthKey {
        let totalMonths = year * 12 + (month - 1) + n
        let newYear = totalMonths / 12
        let newMonth = (totalMonths % 12) + 1
        return MonthKey(year: newYear, month: newMonth)
    }

    public static func < (lhs: MonthKey, rhs: MonthKey) -> Bool {
        if lhs.year != rhs.year { return lhs.year < rhs.year }
        return lhs.month < rhs.month
    }
}

public final class MonthRollup: @unchecked Sendable {
    private var _groups: [MonthKey: [Int64: Int64]] = [:]
    private var _uptime: [MonthKey: Int64] = [:]
    private var _deviceUptime: [MonthKey: [String: Int64]] = [:]
    private var _onBoth: [MonthKey: Int64] = [:]

    public init(
        segs: [UsageSeg],
        zone: TimeZone,
        nowUtc: Int64,
        onSinceUtc: Int64?,
        gamesOnly: Bool,
        localDeviceId: String? = nil
    ) {
        for slice in UsageDays.slices(segs, zone: zone) {
            let key = MonthKey.of(year: slice.year, month: slice.month)
            _groups[key, default: [:]][slice.seg.group, default: 0] += (slice.end - slice.start)
        }

        let breakdown = UsageUptime.perDayBreakdown(
            segs: segs,
            zone: zone,
            nowUtc: nowUtc,
            onSinceUtc: onSinceUtc,
            gamesOnly: gamesOnly,
            localDeviceId: localDeviceId
        )

        for (dayStr, bd) in breakdown {
            let key = MonthKey.of(dayStr: dayStr)
            _uptime[key, default: 0] += bd.unionSeconds
            _onBoth[key, default: 0] += bd.onBothSeconds
            for (dev, s) in bd.deviceSeconds {
                _deviceUptime[key, default: [:]][dev, default: 0] += s
            }
        }
    }

    public func uptime(_ m: MonthKey) -> Int64 {
        _uptime[m] ?? 0
    }

    public func deviceUptime(_ m: MonthKey) -> [String: Int64] {
        _deviceUptime[m] ?? [:]
    }

    public func onBoth(_ m: MonthKey) -> Int64 {
        _onBoth[m] ?? 0
    }

    public func groups(_ m: MonthKey) -> [Int64: Int64] {
        _groups[m] ?? [:]
    }

    public func appSeconds(_ m: MonthKey) -> Int64 {
        _groups[m]?.values.reduce(0, +) ?? 0
    }
}
