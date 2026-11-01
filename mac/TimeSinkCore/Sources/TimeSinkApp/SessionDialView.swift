import SwiftUI
import TimeSinkCore

public struct SessionDialView: View {
    public let appRow: TodayAppRow
    public let dayStartUtc: Int64
    public let dayEndUtc: Int64
    public let isAllScope: Bool

    public init(appRow: TodayAppRow, dayStartUtc: Int64, dayEndUtc: Int64, isAllScope: Bool) {
        self.appRow = appRow
        self.dayStartUtc = dayStartUtc
        self.dayEndUtc = dayEndUtc
        self.isAllScope = isAllScope
    }

    public var body: some View {
        ZStack {
            // Background track
            Circle()
                .stroke(ThemeColors.barBackground, lineWidth: 6)
                .frame(width: 80, height: 80)

            if isAllScope {
                // Outer ring: Device A (Mac)
                Circle()
                    .stroke(ThemeColors.barBackground, lineWidth: 4)
                    .frame(width: 84, height: 84)

                // Middle ring: Device B (PC)
                Circle()
                    .stroke(ThemeColors.barBackground, lineWidth: 4)
                    .frame(width: 72, height: 72)

                // Draw sessions on concentric rings
                ForEach(Array(appRow.sessions.enumerated()), id: \.offset) { _, session in
                    let dayLength = max(1, dayEndUtc - dayStartUtc)
                    let startFrac = Double(max(0, session.startUtc - dayStartUtc)) / Double(dayLength)
                    let durFrac = Double(session.seconds) / Double(dayLength)
                    let isMac = session.deviceId.lowercased().contains("mac") || session.deviceId.isEmpty

                    Circle()
                        .trim(from: CGFloat(startFrac), to: CGFloat(min(1.0, startFrac + durFrac)))
                        .stroke(
                            isMac ? ThemeColors.deviceA : ThemeColors.deviceB,
                            style: StrokeStyle(lineWidth: 4, lineCap: .round)
                        )
                        .rotationEffect(.degrees(-90))
                        .frame(width: isMac ? 84 : 72, height: isMac ? 84 : 72)
                }
            } else {
                // Single ring for this device
                ForEach(Array(appRow.sessions.enumerated()), id: \.offset) { _, session in
                    let dayLength = max(1, dayEndUtc - dayStartUtc)
                    let startFrac = Double(max(0, session.startUtc - dayStartUtc)) / Double(dayLength)
                    let durFrac = Double(session.seconds) / Double(dayLength)

                    Circle()
                        .trim(from: CGFloat(startFrac), to: CGFloat(min(1.0, startFrac + durFrac)))
                        .stroke(
                            ThemeColors.accent,
                            style: StrokeStyle(lineWidth: 6, lineCap: .round)
                        )
                        .rotationEffect(.degrees(-90))
                        .frame(width: 80, height: 80)
                }
            }

            // Center: 7-day average comparison or session duration
            VStack(spacing: 1) {
                Text(Formatters.hoursMinutes(seconds: appRow.seconds))
                    .font(.system(size: 11, weight: .bold, design: .monospaced))
                    .foregroundColor(ThemeColors.textPrimary)

                if let avg = appRow.sevenDayAverageSeconds, avg > 0 {
                    let delta = Double(appRow.seconds) - avg
                    let deltaH = Int64(abs(delta))
                    Text("\(delta >= 0 ? "+" : "-")\(Formatters.hoursOrMinutes(seconds: deltaH))")
                        .font(.system(size: 9, weight: .medium))
                        .foregroundColor(delta >= 0 ? ThemeColors.accent : ThemeColors.warning)
                }
            }
        }
        .frame(width: 90, height: 90)
    }
}
