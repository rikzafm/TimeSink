import Foundation

/// One app's share of a clicked Today hour bar.
public struct BarApp: Sendable, Equatable {
    public let appId: Int64
    public let name: String
    public let seconds: Int64

    public init(appId: Int64, name: String, seconds: Int64) {
        self.appId = appId
        self.name = name
        self.seconds = seconds
    }
}

/// One session (clipped to the clicked hour) of a clicked Today bar.
public struct BarSession: Sendable, Equatable {
    public let appId: Int64
    public let appName: String
    public let startUtc: Int64
    public let endUtc: Int64

    public var seconds: Int64 { max(0, endUtc - startUtc) }

    public init(appId: Int64, appName: String, startUtc: Int64, endUtc: Int64) {
        self.appId = appId
        self.appName = appName
        self.startUtc = startUtc
        self.endUtc = endUtc
    }
}

/// What a Today hour bar shows when clicked: app-seconds per app, the sessions inside the hour and the typical-day baseline.
public struct HourDetail: Sendable, Equatable {
    public let hour: Int
    public let startUtc: Int64
    public let endUtc: Int64
    public let uptimeSeconds: Int64
    public let appSeconds: Int64
    public let typicalUptimeSeconds: Double
    public let typicalAppSeconds: Double
    public let apps: [BarApp]
    public let sessions: [BarSession]
    public let byDevice: [String: DeviceHour]?
    public let onBothSeconds: Int64

    public init(
        hour: Int,
        startUtc: Int64,
        endUtc: Int64,
        uptimeSeconds: Int64 = 0,
        appSeconds: Int64,
        typicalUptimeSeconds: Double = 0,
        typicalAppSeconds: Double = 0,
        apps: [BarApp],
        sessions: [BarSession],
        byDevice: [String: DeviceHour]? = nil,
        onBothSeconds: Int64 = 0
    ) {
        self.hour = hour
        self.startUtc = startUtc
        self.endUtc = endUtc
        self.uptimeSeconds = uptimeSeconds
        self.appSeconds = appSeconds
        self.typicalUptimeSeconds = typicalUptimeSeconds
        self.typicalAppSeconds = typicalAppSeconds
        self.apps = apps
        self.sessions = sessions
        self.byDevice = byDevice
        self.onBothSeconds = onBothSeconds
    }
}

public enum BarDetail {
    /// The detail of wall-clock hour column `hour` (0-23) of the day in `result`.
    public static func forHour(result: TodayResult, hour: Int, zone: TimeZone = .current) -> HourDetail {
        let pieces = UsageDays.hourPieces(from: result.dayStartUtc, to: result.dayEndUtc, zone: zone)
            .filter { $0.hour == hour }

        var apps: [BarApp] = []
        var sessions: [BarSession] = []

        for row in result.rows {
            var secs: Int64 = 0
            for s in row.sessions {
                for piece in pieces {
                    let a = max(s.startUtc, piece.start)
                    let b = min(s.endUtc, piece.end)
                    if b <= a { continue }
                    secs += b - a
                    sessions.append(BarSession(appId: row.appId, appName: row.name, startUtc: a, endUtc: b))
                }
            }
            if secs > 0 {
                apps.append(BarApp(appId: row.appId, name: row.name, seconds: secs))
            }
        }

        let bucket = hour >= 0 && hour < result.hours.count
            ? result.hours[hour]
            : HourBucket(hour: hour, appSeconds: 0, typicalAppSeconds: 0)

        let sortedApps = apps.sorted {
            if $0.seconds != $1.seconds { return $0.seconds > $1.seconds }
            return $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending
        }
        let sortedSessions = sessions.sorted { $0.startUtc < $1.startUtc }

        var onBoth: Int64 = 0
        if let byDev = bucket.byDevice, byDev.count > 1 {
            let sumU = byDev.values.reduce(0) { $0 + $1.uptimeSeconds }
            if sumU > bucket.uptimeSeconds {
                onBoth = sumU - bucket.uptimeSeconds
            }
        }

        return HourDetail(
            hour: hour,
            startUtc: pieces.first?.start ?? result.dayStartUtc,
            endUtc: pieces.last?.end ?? result.dayStartUtc,
            uptimeSeconds: bucket.uptimeSeconds,
            appSeconds: apps.reduce(0) { $0 + $1.seconds },
            typicalUptimeSeconds: bucket.typicalUptimeSeconds,
            typicalAppSeconds: bucket.typicalAppSeconds,
            apps: sortedApps,
            sessions: sortedSessions,
            byDevice: bucket.byDevice,
            onBothSeconds: onBoth
        )
    }
}
