import SwiftUI
import TimeSinkCore

public struct SessionDialView: View {
    public let appRow: TodayAppRow
    public let dayStartUtc: Int64
    public let dayEndUtc: Int64
    public let isAllScope: Bool
    public let localDeviceId: String

    public init(appRow: TodayAppRow, dayStartUtc: Int64, dayEndUtc: Int64, isAllScope: Bool, localDeviceId: String = "") {
        self.appRow = appRow
        self.dayStartUtc = dayStartUtc
        self.dayEndUtc = dayEndUtc
        self.isAllScope = isAllScope
        self.localDeviceId = localDeviceId
    }

    private func isLocalSession(_ deviceId: String) -> Bool {
        if deviceId.isEmpty || localDeviceId.isEmpty { return true }
        return deviceId.caseInsensitiveCompare(localDeviceId) == .orderedSame
    }

    private var longestSessionSeconds: Int64 {
        appRow.sessions.map(\.seconds).max() ?? appRow.seconds
    }

    private var percentVsAverage: Int {
        if let avg = appRow.sevenDayAverageSeconds, avg > 0 {
            let pct = (Double(appRow.seconds) - avg) / avg * 100.0
            return Int(pct)
        }
        return 0
    }

    public var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack(spacing: 20) {
                // Circular Gauge
                ZStack {
                    // Top 12 am marker
                    VStack {
                        Text("12 am")
                            .font(.system(size: 8))
                            .foregroundColor(ThemeColors.textDim)
                        Spacer()
                    }
                    .frame(width: 90, height: 110)

                    if isAllScope {
                        // Outer Ring: Mac
                        Circle()
                            .stroke(ThemeColors.barBackground, lineWidth: 6)
                            .frame(width: 86, height: 86)

                        // Middle Ring: PC
                        Circle()
                            .stroke(ThemeColors.barBackground, lineWidth: 6)
                            .frame(width: 72, height: 72)

                        // Outer ring sessions (Mac)
                        ForEach(Array(appRow.sessions.filter { isLocalSession($0.deviceId) }.enumerated()), id: \.offset) { _, session in
                            let dayLength = max(1, dayEndUtc - dayStartUtc)
                            let startFrac = Double(max(0, session.startUtc - dayStartUtc)) / Double(dayLength)
                            let durFrac = Double(session.seconds) / Double(dayLength)

                            Circle()
                                .trim(from: CGFloat(startFrac), to: CGFloat(min(1.0, startFrac + durFrac)))
                                .stroke(ThemeColors.deviceA, style: StrokeStyle(lineWidth: 6, lineCap: .round))
                                .rotationEffect(.degrees(-90))
                                .frame(width: 86, height: 86)
                        }

                        // Middle ring sessions (PC)
                        ForEach(Array(appRow.sessions.filter { !isLocalSession($0.deviceId) }.enumerated()), id: \.offset) { _, session in
                            let dayLength = max(1, dayEndUtc - dayStartUtc)
                            let startFrac = Double(max(0, session.startUtc - dayStartUtc)) / Double(dayLength)
                            let durFrac = Double(session.seconds) / Double(dayLength)

                            Circle()
                                .trim(from: CGFloat(startFrac), to: CGFloat(min(1.0, startFrac + durFrac)))
                                .stroke(ThemeColors.deviceB, style: StrokeStyle(lineWidth: 6, lineCap: .round))
                                .rotationEffect(.degrees(-90))
                                .frame(width: 72, height: 72)
                        }
                    } else {
                        // Single ring (This Mac)
                        Circle()
                            .stroke(ThemeColors.barBackground, lineWidth: 8)
                            .frame(width: 84, height: 84)

                        ForEach(Array(appRow.sessions.enumerated()), id: \.offset) { _, session in
                            let dayLength = max(1, dayEndUtc - dayStartUtc)
                            let startFrac = Double(max(0, session.startUtc - dayStartUtc)) / Double(dayLength)
                            let durFrac = Double(session.seconds) / Double(dayLength)

                            Circle()
                                .trim(from: CGFloat(startFrac), to: CGFloat(min(1.0, startFrac + durFrac)))
                                .stroke(ThemeColors.accent, style: StrokeStyle(lineWidth: 8, lineCap: .round))
                                .rotationEffect(.degrees(-90))
                                .frame(width: 84, height: 84)
                        }
                    }

                    // Inside Gauge Text: "+9% vs 7-day avg"
                    VStack(spacing: 1) {
                        let pct = percentVsAverage
                        Text("\(pct >= 0 ? "+" : "")\(pct)%")
                            .font(.system(size: 16, weight: .bold))
                            .foregroundColor(pct >= 0 ? ThemeColors.accent : ThemeColors.warning)

                        Text("vs 7-day avg")
                            .font(.system(size: 8))
                            .foregroundColor(ThemeColors.textDim)
                    }
                }
                .frame(width: 96, height: 96)

                // Stats on Right Side
                if isAllScope {
                    let localSessions = appRow.sessions.filter { isLocalSession($0.deviceId) }
                    let remoteSessions = appRow.sessions.filter { !isLocalSession($0.deviceId) }
                    let localSecs = localSessions.reduce(0) { $0 + $1.seconds }
                    let remoteSecs = remoteSessions.reduce(0) { $0 + $1.seconds }

                    VStack(alignment: .leading, spacing: 6) {
                        VStack(alignment: .leading, spacing: 1) {
                            HStack(spacing: 4) {
                                Circle().fill(ThemeColors.deviceA).frame(width: 5, height: 5)
                                Text("MAC · OUTER RING")
                                    .font(.system(size: 9, weight: .bold))
                                    .foregroundColor(ThemeColors.textDim)
                            }
                            Text("\(Formatters.hoursMinutes(seconds: localSecs)) · \(localSessions.count) sessions")
                                .font(.system(size: 12, weight: .semibold))
                                .foregroundColor(ThemeColors.textPrimary)
                        }

                        VStack(alignment: .leading, spacing: 1) {
                            HStack(spacing: 4) {
                                RoundedRectangle(cornerRadius: 1).fill(ThemeColors.deviceB).frame(width: 5, height: 5)
                                Text("PC · MIDDLE RING")
                                    .font(.system(size: 9, weight: .bold))
                                    .foregroundColor(ThemeColors.textDim)
                            }
                            Text("\(Formatters.hoursMinutes(seconds: remoteSecs)) · \(remoteSessions.count) session\(remoteSessions.count == 1 ? "" : "s")")
                                .font(.system(size: 12, weight: .semibold))
                                .foregroundColor(ThemeColors.textPrimary)
                        }

                        if appRow.onBothSeconds > 0 {
                            VStack(alignment: .leading, spacing: 1) {
                                Text("OVERLAP")
                                    .font(.system(size: 9, weight: .bold))
                                    .foregroundColor(ThemeColors.textDim)
                                Text("\(Formatters.hoursMinutes(seconds: appRow.onBothSeconds)) on both")
                                    .font(.system(size: 12, weight: .semibold))
                                    .foregroundColor(ThemeColors.textPrimary)
                            }
                        }

                        if let avg = appRow.sevenDayAverageSeconds, avg > 0 {
                            VStack(alignment: .leading, spacing: 1) {
                                Text("7-DAY AVG")
                                    .font(.system(size: 9, weight: .bold))
                                    .foregroundColor(ThemeColors.textDim)
                                Text(Formatters.hoursMinutes(seconds: Int64(avg)))
                                    .font(.system(size: 12, weight: .semibold))
                                    .foregroundColor(ThemeColors.textPrimary)
                            }
                        }
                    }
                } else {
                    // This Mac stats
                    VStack(alignment: .leading, spacing: 6) {
                        VStack(alignment: .leading, spacing: 1) {
                            Text("SESSIONS")
                                .font(.system(size: 9, weight: .bold))
                                .foregroundColor(ThemeColors.textDim)
                            Text("\(appRow.sessions.count)")
                                .font(.system(size: 14, weight: .bold))
                                .foregroundColor(ThemeColors.textPrimary)
                        }

                        VStack(alignment: .leading, spacing: 1) {
                            Text("LONGEST")
                                .font(.system(size: 9, weight: .bold))
                                .foregroundColor(ThemeColors.textDim)
                            Text(Formatters.hoursMinutes(seconds: longestSessionSeconds))
                                .font(.system(size: 14, weight: .bold))
                                .foregroundColor(ThemeColors.textPrimary)
                        }

                        if let avg = appRow.sevenDayAverageSeconds, avg > 0 {
                            VStack(alignment: .leading, spacing: 1) {
                                Text("7-DAY AVG")
                                    .font(.system(size: 9, weight: .bold))
                                    .foregroundColor(ThemeColors.textDim)
                                Text(Formatters.hoursMinutes(seconds: Int64(avg)))
                                    .font(.system(size: 14, weight: .bold))
                                    .foregroundColor(ThemeColors.textPrimary)
                            }
                        }
                    }
                }

                Spacer()
            }

            // Session ranges below dial (e.g. 8:04 am → 11:45 am · 3h 41m    1:10 pm → now)
            if !appRow.sessions.isEmpty {
                HStack(spacing: 8) {
                    ForEach(Array(appRow.sessions.prefix(2).enumerated()), id: \.offset) { _, s in
                        let startStr = formatSessionTime(s.startUtc)
                        let endStr = s.isOpen ? "now" : formatSessionTime(s.endUtc)
                        Text("\(startStr) → \(endStr) · \(Formatters.hoursMinutes(seconds: s.seconds))")
                            .font(.system(size: 10))
                            .foregroundColor(ThemeColors.textMuted)
                    }
                }
            }
        }
        .padding(12)
        .background(ThemeColors.surface)
        .cornerRadius(8)
    }

    private func formatSessionTime(_ utc: Int64) -> String {
        let df = DateFormatter()
        df.dateFormat = "h:mm a"
        let date = Date(timeIntervalSince1970: TimeInterval(utc))
        return df.string(from: date).lowercased()
    }
}

