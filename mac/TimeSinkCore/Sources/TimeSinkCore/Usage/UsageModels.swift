import Foundation

public struct UsageSeg: Sendable, Equatable {
    public let group: Int64
    public let appId: Int64
    public let start: Int64
    public let end: Int64
    public let open: Bool
    public let sleep: Bool
    public let deviceId: String
    public let off: Bool

    public init(
        group: Int64,
        appId: Int64,
        start: Int64,
        end: Int64,
        open: Bool,
        sleep: Bool,
        deviceId: String = "",
        off: Bool = false
    ) {
        self.group = group
        self.appId = appId
        self.start = start
        self.end = end
        self.open = open
        self.sleep = sleep
        self.deviceId = deviceId
        self.off = off
    }
}

public struct SleepBand: Sendable, Equatable {
    public let startUtc: Int64
    public let endUtc: Int64
    public let deviceId: String?

    public init(startUtc: Int64, endUtc: Int64, deviceId: String? = nil) {
        self.startUtc = startUtc
        self.endUtc = endUtc
        self.deviceId = deviceId
    }
}

public struct UptimeInterval: Sendable, Equatable {
    public let startUtc: Int64
    public let endUtc: Int64

    public init(startUtc: Int64, endUtc: Int64) {
        self.startUtc = startUtc
        self.endUtc = endUtc
    }
}

public struct DeviceUptimeBreakdown: Sendable, Equatable {
    public let unionSeconds: Int64
    public let deviceSeconds: [String: Int64]
    public let onBothSeconds: Int64
    public let deviceIntervals: [String: [UptimeInterval]]
    public let unionIntervals: [UptimeInterval]

    public init(
        unionSeconds: Int64,
        deviceSeconds: [String: Int64],
        onBothSeconds: Int64,
        deviceIntervals: [String: [UptimeInterval]],
        unionIntervals: [UptimeInterval]
    ) {
        self.unionSeconds = unionSeconds
        self.deviceSeconds = deviceSeconds
        self.onBothSeconds = onBothSeconds
        self.deviceIntervals = deviceIntervals
        self.unionIntervals = unionIntervals
    }
}

public struct UsageSlice: Sendable, Equatable {
    public let day: String
    public let year: Int
    public let month: Int
    public let dayOfMonth: Int
    public let start: Int64
    public let end: Int64
    public let seg: UsageSeg

    public init(day: String, year: Int, month: Int, dayOfMonth: Int, start: Int64, end: Int64, seg: UsageSeg) {
        self.day = day
        self.year = year
        self.month = month
        self.dayOfMonth = dayOfMonth
        self.start = start
        self.end = end
        self.seg = seg
    }
}

public struct HourPiece: Sendable, Equatable {
    public let hour: Int
    public let start: Int64
    public let end: Int64

    public init(hour: Int, start: Int64, end: Int64) {
        self.hour = hour
        self.start = start
        self.end = end
    }
}

public enum UsageRank {
    public static func kindOf(apps: AppGroups, group: Int64) -> AppKind {
        apps[group]?.kind ?? apps.members(group: group).first?.kind ?? .other
    }

    public static func isBackground(apps: AppGroups, group: Int64) -> Bool {
        apps[group]?.background ?? apps.members(group: group).first?.background ?? false
    }

    public static func nameOf(apps: AppGroups, group: Int64) -> String {
        if let app = apps[group] { return app.name }
        if let member = apps.members(group: group).first { return member.name }
        return String(group)
    }

    public static func rank<T>(
        items: [T],
        apps: AppGroups,
        groupOf: (T) -> Int64,
        amountOf: (T) -> Int64
    ) -> [T] {
        items.sorted { a, b in
            let bgA = isBackground(apps: apps, group: groupOf(a))
            let bgB = isBackground(apps: apps, group: groupOf(b))
            if bgA != bgB {
                return !bgA // non-background first
            }
            let amtA = amountOf(a)
            let amtB = amountOf(b)
            if amtA != amtB {
                return amtA > amtB // higher amount first
            }
            let nameA = nameOf(apps: apps, group: groupOf(a))
            let nameB = nameOf(apps: apps, group: groupOf(b))
            return nameA.localizedCaseInsensitiveCompare(nameB) == .orderedAscending
        }
    }
}

