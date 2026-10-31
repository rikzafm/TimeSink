import Foundation

/// Maintains the derived `daily_usage` table. Ranges are split at local midnight of the given time zone
/// (DST-aware: days can be 23 or 25 hours long). A reversed range subtracts.
public final class UsageAccumulator: @unchecked Sendable {
    private let store: SessionStore
    private let timeZone: TimeZone

    public init(store: SessionStore, timeZone: TimeZone) {
        self.store = store
        self.timeZone = timeZone
    }

    public func add(appId: Int64, from: Date, to: Date) throws {
        try add(appId: appId, fromUtc: Int64(from.timeIntervalSince1970), toUtc: Int64(to.timeIntervalSince1970))
    }

    public func add(appId: Int64, fromUtc: Int64, toUtc: Int64) throws {
        for item in split(from: fromUtc, to: toUtc) {
            try store.addDailyUsage(appId: appId, day: item.day, deltaSeconds: item.seconds)
        }
    }

    /// Recomputes `daily_usage` from sessions (open sessions count up to `last_seen`).
    public func rebuildAll() throws {
        struct Key: Hashable {
            let appId: Int64
            let day: String
        }

        var totals: [Key: Int64] = [:]
        for s in try store.getAllSessions() {
            let end = s.endUtc ?? s.lastSeenUtc
            for item in split(from: s.startUtc, to: end) {
                let k = Key(appId: s.appId, day: item.day)
                totals[k, default: 0] += item.seconds
            }
        }

        let rows = totals
            .filter { $0.value > 0 }
            .map { DailyUsageRow(appId: $0.key.appId, day: $0.key.day, seconds: $0.value) }
            .sorted { ($0.appId, $0.day) < ($1.appId, $1.day) }

        try store.replaceDailyUsage(rows)
    }

    public func split(from: Int64, to: Int64) -> [(day: String, seconds: Int64)] {
        if from == to { return [] }

        var start = from
        var end = to
        var sign: Int64 = 1
        if end < start {
            swap(&start, &end)
            sign = -1
        }

        var results: [(day: String, seconds: Int64)] = []
        var cur = start
        while cur < end {
            let dayStr = LocalDays.dayString(for: cur, timeZone: timeZone)
            var next = min(end, LocalDays.startOfNextDay(for: cur, timeZone: timeZone))
            if next <= cur {
                next = end // defensive: never loop forever
            }
            results.append((day: dayStr, seconds: sign * (next - cur)))
            cur = next
        }

        return results
    }
}
