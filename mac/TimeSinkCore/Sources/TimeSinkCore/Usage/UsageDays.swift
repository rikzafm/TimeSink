import Foundation

public enum UsageDays {
    /// Seconds of `s` inside [a, b).
    public static func overlap(_ s: UsageSeg, _ a: Int64, _ b: Int64) -> Int64 {
        max(0, min(s.end, b) - max(s.start, a))
    }

    /// Slices sessions at local day boundaries.
    public static func slices(_ segs: [UsageSeg], zone: TimeZone) -> [UsageSlice] {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = zone
        var result: [UsageSlice] = []

        for seg in segs {
            if seg.end <= seg.start { continue }
            let startDate = Date(timeIntervalSince1970: TimeInterval(seg.start))
            let endDate = Date(timeIntervalSince1970: TimeInterval(seg.end - 1))

            var startDayDate = calendar.startOfDay(for: startDate)
            let endDayDate = calendar.startOfDay(for: endDate)

            while startDayDate <= endDayDate {
                let comps = calendar.dateComponents([.year, .month, .day], from: startDayDate)
                guard let y = comps.year, let m = comps.month, let d = comps.day else { break }
                let dayStr = String(format: "%04d-%02d-%02d", y, m, d)

                let dayStart = Int64(startDayDate.timeIntervalSince1970)
                let nextDayDate = calendar.date(byAdding: .day, value: 1, to: startDayDate)!
                let nextDayStart = Int64(calendar.startOfDay(for: nextDayDate).timeIntervalSince1970)

                let a = max(seg.start, dayStart)
                let b = min(seg.end, nextDayStart)
                if b > a {
                    result.append(UsageSlice(day: dayStr, year: y, month: m, dayOfMonth: d, start: a, end: b, seg: seg))
                }

                guard let next = calendar.date(byAdding: .day, value: 1, to: startDayDate) else { break }
                startDayDate = next
            }
        }
        return result
    }

    /// Seconds per group on each local day the sessions touch.
    public static func perDayGroup(_ segs: [UsageSeg], zone: TimeZone) -> [String: [Int64: Int64]] {
        var days: [String: [Int64: Int64]] = [:]
        for sl in slices(segs, zone: zone) {
            var grps = days[sl.day, default: [:]]
            grps[sl.seg.group, default: 0] += (sl.end - sl.start)
            days[sl.day] = grps
        }
        return days
    }

    /// Cuts [a, b) at every local whole hour.
    public static func hourPieces(from a: Int64, to b: Int64, zone: TimeZone) -> [HourPiece] {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = zone
        var pieces: [HourPiece] = []
        var t = a
        while t < b {
            let date = Date(timeIntervalSince1970: TimeInterval(t))
            let comps = calendar.dateComponents([.hour, .minute, .second], from: date)
            let minute = comps.minute ?? 0
            let second = comps.second ?? 0
            let hour = comps.hour ?? 0
            let secondsIntoHour = minute * 60 + second
            let nextHourSecs = t + Int64(3600 - secondsIntoHour)
            let next = min(nextHourSecs, b)
            pieces.append(HourPiece(hour: hour, start: t, end: next))
            t = (next <= t) ? b : next
        }
        return pieces
    }

    /// Seconds per wall-clock hour column (0-23) of one local day.
    public static func hourSums(
        segs: [UsageSeg],
        year: Int,
        month: Int,
        day: Int,
        zone: TimeZone
    ) -> [Int64] {
        let d0 = LocalDays.startOf(year: year, month: month, day: day, timeZone: zone)
        let d1 = LocalDays.startOfNextDay(for: d0, timeZone: zone)
        var sums = [Int64](repeating: 0, count: 24)

        for s in segs {
            let a = max(s.start, d0)
            let b = min(s.end, d1)
            if b <= a { continue }
            for p in hourPieces(from: a, to: b, zone: zone) {
                if p.hour >= 0 && p.hour < 24 {
                    sums[p.hour] += (p.end - p.start)
                }
            }
        }
        return sums
    }
}

