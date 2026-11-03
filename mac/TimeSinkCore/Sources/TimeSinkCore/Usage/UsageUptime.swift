import Foundation

public enum UsageUptime {
    /// Seconds the PC was on within [from, to), sleep excluded. Overlap across multiple devices counted once.
    public static func seconds(
        segs: [UsageSeg],
        from: Int64,
        to: Int64,
        live: Bool,
        onSince: Int64?,
        gamesOnly: Bool = false,
        localDeviceId: String? = nil
    ) -> Int64 {
        breakdown(segs: segs, from: from, to: to, live: live, onSince: onSince, gamesOnly: gamesOnly, localDeviceId: localDeviceId).unionSeconds
    }

    /// Computes the uptime intervals for a single device within [from, to), sleep excluded.
    public static func intervals(
        segs: [UsageSeg],
        from: Int64,
        to: Int64,
        live: Bool,
        onSince: Int64?,
        gamesOnly: Bool = false
    ) -> [UptimeInterval] {
        if gamesOnly {
            var raw: [UptimeInterval] = []
            for s in segs where s.end > from && s.start < to {
                let st = max(s.start, from)
                let en = min(s.end, to)
                if en > st {
                    raw.append(UptimeInterval(startUtc: st, endUtc: en))
                }
            }
            return union(raw)
        }

        let inRange = segs.filter { $0.end > from && $0.start < to }
        let start: Int64
        if !inRange.isEmpty {
            let minStart = inRange.map(\.start).min() ?? Int64.max
            start = max(from, min(onSince ?? Int64.max, minStart))
        } else if let on = onSince {
            start = max(from, on)
        } else {
            return []
        }

        let end: Int64
        if live {
            end = to
        } else if !inRange.isEmpty {
            let maxEnd = inRange.map(\.end).max() ?? start
            end = min(to, maxEnd)
        } else {
            end = start
        }

        if end <= start { return [] }

        let bands = inactiveBands(segs: segs, d0: start, nowUtc: end)
        var result: [UptimeInterval] = []
        var cursor = start
        for b in bands.sorted(by: { $0.startUtc < $1.startUtc }) {
            let bStart = max(start, b.startUtc)
            let bEnd = min(end, b.endUtc)
            if bEnd <= bStart { continue }

            if bStart > cursor {
                result.append(UptimeInterval(startUtc: cursor, endUtc: bStart))
            }
            cursor = max(cursor, bEnd)
        }

        if cursor < end {
            result.append(UptimeInterval(startUtc: cursor, endUtc: end))
        }
        return result
    }

    /// Unions overlapping or adjacent intervals, sorted by start time.
    public static func union(_ intervals: [UptimeInterval]) -> [UptimeInterval] {
        let sorted = intervals.filter { $0.endUtc > $0.startUtc }.sorted {
            if $0.startUtc != $1.startUtc { return $0.startUtc < $1.startUtc }
            return $0.endUtc < $1.endUtc
        }
        if sorted.isEmpty { return [] }

        var merged: [UptimeInterval] = []
        var current = sorted[0]

        for i in 1..<sorted.count {
            let next = sorted[i]
            if next.startUtc <= current.endUtc {
                current = UptimeInterval(startUtc: current.startUtc, endUtc: max(current.endUtc, next.endUtc))
            } else {
                merged.append(current)
                current = next
            }
        }
        merged.append(current)
        return merged
    }

    /// Computes union uptime, per-device uptime, and overlap ("on both") seconds within [from, to).
    public static func breakdown(
        segs: [UsageSeg],
        from: Int64,
        to: Int64,
        live: Bool,
        onSince: Int64?,
        gamesOnly: Bool = false,
        localDeviceId: String? = nil
    ) -> DeviceUptimeBreakdown {
        if segs.isEmpty {
            if live, onSince != nil, !gamesOnly {
                let inters = intervals(segs: segs, from: from, to: to, live: live, onSince: onSince, gamesOnly: gamesOnly)
                let secs = inters.reduce(0) { $0 + ($1.endUtc - $1.startUtc) }
                let dev = localDeviceId ?? ""
                return DeviceUptimeBreakdown(
                    unionSeconds: secs,
                    deviceSeconds: [dev: secs],
                    onBothSeconds: 0,
                    deviceIntervals: [dev: inters],
                    unionIntervals: inters
                )
            }
            return DeviceUptimeBreakdown(
                unionSeconds: 0,
                deviceSeconds: [:],
                onBothSeconds: 0,
                deviceIntervals: [:],
                unionIntervals: []
            )
        }

        // Group by deviceId
        var groups: [String: [UsageSeg]] = [:]
        for s in segs {
            groups[s.deviceId, default: []].append(s)
        }

        if groups.count <= 1 {
            let dev = groups.keys.first ?? ""
            let groupSegs = groups.values.first ?? []
            let inters = intervals(segs: groupSegs, from: from, to: to, live: live, onSince: onSince, gamesOnly: gamesOnly)
            let secs = inters.reduce(0) { $0 + ($1.endUtc - $1.startUtc) }
            return DeviceUptimeBreakdown(
                unionSeconds: secs,
                deviceSeconds: [dev: secs],
                onBothSeconds: 0,
                deviceIntervals: [dev: inters],
                unionIntervals: inters
            )
        }

        var devIntervals: [String: [UptimeInterval]] = [:]
        var devSeconds: [String: Int64] = [:]
        for (dev, groupSegs) in groups {
            let isLocal = (localDeviceId == nil || localDeviceId!.isEmpty)
                ? (dev == groups.keys.first)
                : (dev.caseInsensitiveCompare(localDeviceId!) == .orderedSame)
            let devOnSince = isLocal ? onSince : nil
            let devLive = live && (isLocal || groupSegs.contains { $0.open })
            let inters = intervals(segs: groupSegs, from: from, to: to, live: devLive, onSince: devOnSince, gamesOnly: gamesOnly)
            devIntervals[dev] = inters
            devSeconds[dev] = inters.reduce(0) { $0 + ($1.endUtc - $1.startUtc) }
        }

        let allInters = devIntervals.values.flatMap { $0 }
        let unionInters = union(allInters)
        let unionSecs = unionInters.reduce(0) { $0 + ($1.endUtc - $1.startUtc) }
        let sumDevSecs = devSeconds.values.reduce(0, +)
        let onBoth = max(0, sumDevSecs - unionSecs)

        return DeviceUptimeBreakdown(
            unionSeconds: unionSecs,
            deviceSeconds: devSeconds,
            onBothSeconds: onBoth,
            deviceIntervals: devIntervals,
            unionIntervals: unionInters
        )
    }

    /// The union duration of segments overlapping [from, to), overlaps counted once.
    public static func openSeconds(segs: [UsageSeg], from: Int64, to: Int64) -> Int64 {
        var total: Int64 = 0
        var runEnd: Int64 = from
        let sorted = segs.filter { $0.end > from && $0.start < to }.sorted { $0.start < $1.start }
        for s in sorted {
            let start = max(max(s.start, from), runEnd)
            let end = min(s.end, to)
            if end > start {
                total += end - start
            }
            runEnd = max(runEnd, end)
        }
        return total
    }

    /// Inactive gaps (sleep or shutdown) to subtract from uptime.
    public static func inactiveBands(segs: [UsageSeg], d0: Int64, nowUtc: Int64) -> [SleepBand] {
        var bands: [(start: Int64, end: Int64)] = []
        for s in segs where (s.sleep || s.off) && s.end >= d0 && s.end <= nowUtc {
            let startsAfter = segs.filter { $0.start > s.end }.map(\.start)
            let next = startsAfter.min() ?? nowUtc
            bands.append((start: s.end, end: min(next, nowUtc)))
        }
        return mergeBands(bands)
    }

    private static func mergeBands(_ bands: [(start: Int64, end: Int64)], deviceId: String? = nil) -> [SleepBand] {
        let sorted = bands.sorted { $0.start < $1.start }
        var merged: [SleepBand] = []
        for b in sorted {
            if b.end <= b.start { continue }
            if let last = merged.last, b.start <= last.endUtc {
                merged[merged.count - 1] = SleepBand(startUtc: last.startUtc, endUtc: max(last.endUtc, b.end), deviceId: deviceId)
            } else {
                merged.append(SleepBand(startUtc: b.start, endUtc: b.end, deviceId: deviceId))
            }
        }
        return merged
    }

    /// From each sleep-close to the next session start of any app (or now), merged and clipped.
    public static func sleepBands(segs: [UsageSeg], d0: Int64, nowUtc: Int64, liveDeviceId: String? = nil) -> [SleepBand] {
        var result: [SleepBand] = []
        let byDev = Dictionary(grouping: segs, by: { $0.deviceId })
        for (devId, devSegs) in byDev {
            let sorted = devSegs.sorted(by: { $0.start < $1.start })
            let isLive = liveDeviceId != nil && devId.caseInsensitiveCompare(liveDeviceId!) == .orderedSame
            var devBands: [(start: Int64, end: Int64)] = []
            for s in sorted where s.sleep && s.end >= d0 && s.end <= nowUtc {
                let startsAfter = sorted.filter { $0.start > s.end }.map(\.start)
                if let next = startsAfter.min() {
                    devBands.append((start: s.end, end: min(next, nowUtc)))
                } else if isLive {
                    devBands.append((start: s.end, end: nowUtc))
                }
            }
            result.append(contentsOf: mergeBands(devBands, deviceId: devId.isEmpty ? nil : devId))
        }
        return result.sorted { $0.startUtc < $1.startUtc }
    }

    /// Uptime seconds and per-device breakdown per local day from `segs`.
    public static func perDayBreakdown(
        segs: [UsageSeg],
        zone: TimeZone,
        nowUtc: Int64,
        onSinceUtc: Int64? = nil,
        gamesOnly: Bool = false,
        localDeviceId: String? = nil
    ) -> [String: DeviceUptimeBreakdown] {
        let todayStr = LocalDays.dayString(for: nowUtc, timeZone: zone)
        var byDay: [String: [UsageSeg]] = [:]

        for slice in UsageDays.slices(segs, zone: zone) {
            byDay[slice.day, default: []].append(slice.seg)
        }
        if onSinceUtc != nil && !gamesOnly && byDay[todayStr] == nil {
            byDay[todayStr] = []
        }

        var result: [String: DeviceUptimeBreakdown] = [:]
        for (dayStr, list) in byDay {
            let live = (dayStr == todayStr)
            let comps = dayStr.split(separator: "-").compactMap { Int($0) }
            guard comps.count == 3 else { continue }
            let from = LocalDays.startOf(year: comps[0], month: comps[1], day: comps[2], timeZone: zone)
            let to = live ? nowUtc : LocalDays.startOfNextDay(for: from, timeZone: zone)
            result[dayStr] = breakdown(
                segs: list,
                from: from,
                to: to,
                live: live,
                onSince: live ? onSinceUtc : nil,
                gamesOnly: gamesOnly,
                localDeviceId: localDeviceId
            )
        }
        return result
    }

    /// Uptime seconds per local day unioned across devices.
    public static func perDay(
        segs: [UsageSeg],
        zone: TimeZone,
        nowUtc: Int64,
        onSinceUtc: Int64? = nil,
        gamesOnly: Bool = false,
        localDeviceId: String? = nil
    ) -> [String: Int64] {
        perDayBreakdown(segs: segs, zone: zone, nowUtc: nowUtc, onSinceUtc: onSinceUtc, gamesOnly: gamesOnly, localDeviceId: localDeviceId)
            .mapValues { $0.unionSeconds }
    }
}

