import SwiftUI
import TimeSinkCore

/// One ring of the 24-hour dial, 12 am at the top.
private struct DialRing: View {
    let diameter: CGFloat
    let width: CGFloat
    let segments: [(CGFloat, CGFloat)]
    let color: Color

    var body: some View {
        ZStack {
            Circle().stroke(Tokens.track, lineWidth: width)
            ForEach(Array(segments.enumerated()), id: \.offset) { _, seg in
                Circle()
                    .trim(from: seg.0, to: seg.1)
                    .stroke(color, style: StrokeStyle(lineWidth: width, lineCap: .butt))
                    .rotationEffect(.degrees(-90))
            }
        }
        .frame(width: diameter - width, height: diameter - width)
    }
}

public struct SessionDialView: View {
    public let appRow: TodayAppRow
    public let dayStartUtc: Int64
    public let dayEndUtc: Int64
    public let nowUtc: Int64
    public let isAllScope: Bool
    public let localDeviceId: String

    public init(appRow: TodayAppRow, dayStartUtc: Int64, dayEndUtc: Int64, nowUtc: Int64, isAllScope: Bool, localDeviceId: String = "") {
        self.appRow = appRow
        self.dayStartUtc = dayStartUtc
        self.dayEndUtc = dayEndUtc
        self.nowUtc = nowUtc
        self.isAllScope = isAllScope
        self.localDeviceId = localDeviceId
    }

    private var appColor: Color { Tokens.appColor(appRow.name) }
    private var dayLength: CGFloat { CGFloat(max(1, dayEndUtc - dayStartUtc)) }
    private var nowFraction: CGFloat { min(1, CGFloat(nowUtc - dayStartUtc) / dayLength) }
    private var average: Double { appRow.sevenDayAverageSeconds ?? 0 }
    private var longest: Int64 { appRow.sessions.map(\.seconds).max() ?? 0 }

    private func isLocal(_ deviceId: String) -> Bool {
        deviceId.isEmpty || localDeviceId.isEmpty || deviceId.caseInsensitiveCompare(localDeviceId) == .orderedSame
    }

    private func segments(_ sessions: [TodaySession]) -> [(CGFloat, CGFloat)] {
        sessions.map {
            (CGFloat(max(0, $0.startUtc - dayStartUtc)) / dayLength, min(1, CGFloat($0.endUtc - dayStartUtc) / dayLength))
        }
    }

    private var percentText: String {
        guard average > 0 else { return "—" }
        let pct = Int(((Double(appRow.seconds) - average) / average * 100).rounded())
        return "\(pct >= 0 ? "+" : "")\(pct)%"
    }

    public var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(alignment: .center, spacing: 14) {
                if isAllScope { dualDial } else { singleDial }
                if isAllScope { dualStats } else { singleStats }
                Spacer(minLength: 0)
            }
            if !isAllScope { sessionChips }
        }
        .padding(.top, 4)
        .padding(.horizontal, 4)
    }

    private func center(_ size: CGFloat) -> some View {
        VStack(spacing: 1) {
            Text(percentText)
                .font(.system(size: size, weight: .bold))
                .foregroundColor(ThemeColors.accent)
            Text("vs 7-day avg")
                .font(.system(size: 9))
                .foregroundColor(ThemeColors.textMuted)
        }
    }

    private func nowDot(_ size: CGFloat, inset: CGFloat) -> some View {
        Circle()
            .fill(ThemeColors.accent)
            .overlay(Circle().strokeBorder(ThemeColors.surfaceActive, lineWidth: 2))
            .frame(width: 12, height: 12)
            .offset(y: -(size / 2 - inset))
            .rotationEffect(.degrees(Double(nowFraction) * 360))
    }

    private func tick(_ size: CGFloat, top: CGFloat, length: CGFloat) -> some View {
        Rectangle()
            .fill(ThemeColors.textPrimary)
            .frame(width: 2, height: length)
            .offset(y: -(size / 2 - top - length / 2))
            .rotationEffect(.degrees(average / 86400 * 360))
    }

    private var totalFraction: CGFloat { min(1, CGFloat(appRow.seconds) / 86400) }

    private var singleDial: some View {
        ZStack {
            DialRing(diameter: 150, width: 11, segments: segments(appRow.sessions), color: appColor)
            DialRing(diameter: 116, width: 11, segments: [(0, totalFraction)], color: appColor.opacity(0.32))
            if average > 0 { tick(150, top: 12, length: 22) }
            nowDot(150, inset: 6)
            center(20)
            Text("12 am")
                .font(.system(size: 9))
                .foregroundColor(ThemeColors.textMuted)
                .offset(y: -82)
        }
        .frame(width: 150, height: 150)
        .padding(.top, 6)
    }

    private var dualDial: some View {
        let mac = appRow.sessions.filter { isLocal($0.deviceId) }
        let pc = appRow.sessions.filter { !isLocal($0.deviceId) }
        return ZStack {
            DialRing(diameter: 156, width: 10, segments: segments(mac), color: ThemeColors.deviceA)
            DialRing(diameter: 130, width: 10, segments: segments(pc), color: ThemeColors.deviceB)
            DialRing(diameter: 104, width: 10, segments: [(0, totalFraction)], color: appColor.opacity(0.32))
            if average > 0 { tick(156, top: 24, length: 16) }
            nowDot(156, inset: 5)
            center(18)
        }
        .frame(width: 156, height: 156)
    }

    private func stat(_ label: String, _ value: String, size: CGFloat) -> some View {
        VStack(alignment: .leading, spacing: 0) {
            Text(label).caps(9)
            Text(value)
                .font(.system(size: size, weight: .bold))
                .foregroundColor(ThemeColors.textPrimary)
                .lineLimit(1)
        }
    }

    private var singleStats: some View {
        VStack(alignment: .leading, spacing: 10) {
            stat("SESSIONS", "\(appRow.sessions.count)", size: 16)
            stat("LONGEST", Formatters.paddedHM(longest), size: 16)
            stat("7-DAY AVG", average > 0 ? Formatters.paddedHM(Int64(average)) : "—", size: 16)
        }
    }

    private func deviceLine(_ sessions: [TodaySession]) -> String {
        let total = sessions.reduce(0) { $0 + $1.seconds }
        return "\(Formatters.paddedHM(total)) · \(sessions.count) session\(sessions.count == 1 ? "" : "s")"
    }

    private var dualStats: some View {
        VStack(alignment: .leading, spacing: 9) {
            HStack(spacing: 6) {
                Circle().fill(ThemeColors.deviceA).frame(width: 8, height: 8)
                stat("MAC · OUTER RING", deviceLine(appRow.sessions.filter { isLocal($0.deviceId) }), size: 13)
            }
            HStack(spacing: 6) {
                RoundedRectangle(cornerRadius: 2).fill(ThemeColors.deviceB).frame(width: 8, height: 8)
                stat("PC · MIDDLE RING", deviceLine(appRow.sessions.filter { !isLocal($0.deviceId) }), size: 13)
            }
            stat("OVERLAP", "\(Formatters.paddedHM(appRow.onBothSeconds)) on both", size: 13)
            stat("7-DAY AVG", average > 0 ? Formatters.paddedHM(Int64(average)) : "—", size: 13)
        }
    }

    private var sessionChips: some View {
        HStack(spacing: 6) {
            ForEach(Array(appRow.sessions.prefix(2).enumerated()), id: \.offset) { i, s in
                let text = s.isOpen
                    ? "\(Formatters.clock12(s.startUtc)) → now"
                    : "\(Formatters.clock12(s.startUtc)) → \(Formatters.clock12(s.endUtc)) · \(Formatters.paddedHM(s.seconds))"
                Text(text)
                    .font(.system(size: 10))
                    .foregroundColor(i == 0 ? appColor : ThemeColors.textDim)
                    .lineLimit(1)
                    .padding(.horizontal, 8)
                    .padding(.vertical, 3)
                    .overlay(RoundedRectangle(cornerRadius: 10).strokeBorder(i == 0 ? appColor : ThemeColors.border, lineWidth: 1))
            }
        }
    }
}
