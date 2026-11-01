import Foundation

/// What an expanded Today row shows: the sessions worth drawing, how many blips were left out, the
/// longest of them, the app's time against its 7-day average and the two dial fractions of a 24 h day.
public struct RowDetail: Sendable, Equatable {
    public let shown: [TodaySession]
    public let blips: Int
    public let longestSeconds: Int64
    public let averageSeconds: Double?
    public let totalFraction: Double
    public let averageFraction: Double?

    public init(
        shown: [TodaySession],
        blips: Int,
        longestSeconds: Int64,
        averageSeconds: Double?,
        totalFraction: Double,
        averageFraction: Double?
    ) {
        self.shown = shown
        self.blips = blips
        self.longestSeconds = longestSeconds
        self.averageSeconds = averageSeconds
        self.totalFraction = totalFraction
        self.averageFraction = averageFraction
    }
}

public enum RowDetails {
    /// A session shorter than this is a blip.
    public static let minShownSessionSeconds: Int64 = 60

    public static func shownSessions(_ row: TodayAppRow) -> [TodaySession] {
        row.sessions.filter { $0.seconds >= minShownSessionSeconds }
    }

    public static func of(_ row: TodayAppRow) -> RowDetail {
        let shown = shownSessions(row)
        let blips = row.sessions.count - shown.count
        let longest = shown.map(\.seconds).max() ?? 0
        let avg = (row.sevenDayAverageSeconds ?? 0) > 0 ? row.sevenDayAverageSeconds : nil
        return RowDetail(
            shown: shown,
            blips: blips,
            longestSeconds: longest,
            averageSeconds: avg,
            totalFraction: min(1.0, Double(row.seconds) / 86400.0),
            averageFraction: avg.map { min(1.0, $0 / 86400.0) }
        )
    }
}

public extension TodayResult {
    /// Position of `utc` in the day as a fraction from 0 to 1.
    func dayFraction(_ utc: Int64) -> Double {
        let length = Double(max(1, dayEndUtc - dayStartUtc))
        return min(max(Double(utc - dayStartUtc) / length, 0), 1)
    }

    /// Where an instant before the day started falls on the 24 h dial.
    func dayFractionBefore(_ utc: Int64) -> Double {
        let length = Double(max(1, dayEndUtc - dayStartUtc))
        return min(max(1.0 + Double(utc - dayStartUtc) / length, 0), 1)
    }
}

