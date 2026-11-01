import SwiftUI
import TimeSinkCore

/// The Today strip: 24 hour bars, a now flag, and the typical day ghosted ahead of now.
/// Interactive: hovering previews that hour's time and top apps; clicking selects the hour and displays its detail panel.
public struct HourlyChartView: View {
    @ObservedObject var state: AppState
    public let result: TodayResult
    @Binding public var selectedHour: Int?
    @State private var hoveredHour: Int? = nil

    public init(state: AppState, result: TodayResult, selectedHour: Binding<Int?>? = nil) {
        self.state = state
        self.result = result
        self._selectedHour = selectedHour ?? .constant(nil)
    }

    private let barsHeight: CGFloat = 46
    private var split: Bool { state.showsDeviceSplit }
    private var isLive: Bool { state.daysBack == 0 }
    private var nowFraction: CGFloat {
        CGFloat(result.nowUtc - result.dayStartUtc) / CGFloat(max(1, result.dayEndUtc - result.dayStartUtc))
    }
    private var nowHour: Int { isLive ? min(23, Int(nowFraction * 24)) : 24 }
    private let scale: Double = 3600.0

    private func isAsleep(_ hour: Int) -> Bool {
        let start = result.dayStartUtc + Int64(hour) * 3600
        let end = start + 3600
        return result.sleepBands.contains { min($0.endUtc, end) - max($0.startUtc, start) >= 1800 }
    }

    private func height(_ seconds: Double) -> CGFloat {
        CGFloat(seconds / scale) * barsHeight
    }

    private func color(_ seconds: Int64, hour: Int) -> Color {
        if hour == nowHour { return ThemeColors.accent }
        let f = Double(seconds) / scale
        if f <= 0 { return Tokens.ramp[0] }
        return f > 0.75 ? Tokens.ramp[3] : f > 0.5 ? Tokens.ramp[2] : Tokens.ramp[1]
    }

    private func tooltipText(_ hour: Int) -> String {
        let d = BarDetail.forHour(result: result, hour: hour)
        var lines = ["\(Formatters.hourRange(hour)) · " + (d.uptimeSeconds > 0 ? "\(Formatters.paddedHM(d.uptimeSeconds)) uptime" : (d.appSeconds > 0 ? "\(Formatters.paddedHM(d.appSeconds)) app time" : "no activity"))]
        for a in d.apps.prefix(3) {
            lines.append("\(a.name) \(Formatters.paddedHM(a.seconds))")
        }
        return lines.joined(separator: "\n")
    }

    public var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            ZStack(alignment: .topLeading) {
                VStack(spacing: 0) {
                    Spacer(minLength: 0)
                    bars.frame(height: barsHeight)
                    axis.frame(height: 16)
                }
                if isLive { nowFlag }
            }
            .frame(height: 78)

            if let h = selectedHour {
                detailPanel(h)
                    .transition(.opacity.combined(with: .move(edge: .top)))
            }

            legend
        }
        .onExitCommand {
            withAnimation(.easeInOut(duration: 0.18)) { selectedHour = nil }
        }
    }

    private var bars: some View {
        HStack(alignment: .bottom, spacing: 3) {
            ForEach(result.hours, id: \.hour) { bucket in
                let isHovered = hoveredHour == bucket.hour
                let isSelected = selectedHour == bucket.hour
                Button(action: {
                    withAnimation(.easeInOut(duration: 0.18)) {
                        selectedHour = (selectedHour == bucket.hour ? nil : bucket.hour)
                    }
                }) {
                    ZStack(alignment: .bottom) {
                        if isHovered {
                            RoundedRectangle(cornerRadius: 2)
                                .fill(ThemeColors.textMuted.opacity(0.18))
                                .frame(height: barsHeight)
                        }
                        barView(bucket)
                            .frame(maxWidth: .infinity, alignment: .bottom)
                        if isSelected {
                            RoundedRectangle(cornerRadius: 2)
                                .strokeBorder(ThemeColors.accent, lineWidth: 1.5)
                                .frame(height: barsHeight)
                        }
                    }
                    .frame(maxWidth: .infinity)
                    .frame(height: barsHeight, alignment: .bottom)
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .help(tooltipText(bucket.hour))
                .onHover { isH in
                    if isH { hoveredHour = bucket.hour }
                    else if hoveredHour == bucket.hour { hoveredHour = nil }
                }
            }
        }
    }

    @ViewBuilder
    private func barView(_ bucket: HourBucket) -> some View {
        if bucket.hour > nowHour {
            RoundedRectangle(cornerRadius: 2)
                .strokeBorder(Tokens.typical, style: StrokeStyle(lineWidth: 1, dash: [1, 1.5]))
                .frame(height: max(2, height(bucket.typicalUptimeSeconds)))
                .opacity(result.hasTypical ? 1 : 0)
        } else if isAsleep(bucket.hour) {
            Hatch(color: Tokens.ramp[2])
                .frame(height: max(height(Double(bucket.uptimeSeconds)), barsHeight * 0.7))
                .clipShape(RoundedRectangle(cornerRadius: 2))
        } else if split {
            let parts = state.deviceSplit(bucket.byDevice?.mapValues(\.uptimeSeconds))
            OverlapBar(
                mac: height(Double(parts.mac)),
                pc: height(Double(parts.pc)),
                macColor: bucket.hour == nowHour ? ThemeColors.accent : Tokens.ramp[3],
                pcColor: ThemeColors.deviceBDark,
                radius: 2
            )
        } else {
            RoundedRectangle(cornerRadius: 2)
                .fill(color(bucket.uptimeSeconds, hour: bucket.hour))
                .frame(height: max(2, height(Double(bucket.uptimeSeconds))))
        }
    }

    private var axis: some View {
        HStack {
            ForEach(Array(["12 am", "6 am", "12 pm", "6 pm", "12 am"].enumerated()), id: \.offset) { i, label in
                if i > 0 { Spacer() }
                Text(label)
            }
        }
        .font(.system(size: 10))
        .foregroundColor(ThemeColors.textMuted)
        .frame(maxHeight: .infinity, alignment: .bottom)
    }

    private var nowFlag: some View {
        GeometryReader { geo in
            let x = nowFraction * geo.size.width
            Rectangle().fill(ThemeColors.accent).frame(width: 1, height: 62).offset(x: x)
            Text(Formatters.clock12(result.nowUtc))
                .font(.system(size: 10, weight: .bold))
                .foregroundColor(ThemeColors.background)
                .padding(.horizontal, 6)
                .frame(height: 16)
                .background(RoundedRectangle(cornerRadius: 4).fill(ThemeColors.accent))
                .fixedSize()
                .offset(x: min(x - 24, geo.size.width - 52))
        }
    }

    private func detailPanel(_ hour: Int) -> some View {
        let d = BarDetail.forHour(result: result, hour: hour)
        return VStack(alignment: .leading, spacing: 10) {
            // Header
            HStack(alignment: .top) {
                VStack(alignment: .leading, spacing: 2) {
                    Text(Formatters.hourRange(hour))
                        .font(.system(size: 13, weight: .bold))
                        .foregroundColor(ThemeColors.textPrimary)
                    Text(d.uptimeSeconds > 0 || d.appSeconds > 0
                         ? "\(Formatters.paddedHM(d.uptimeSeconds)) uptime · \(Formatters.paddedHM(d.appSeconds)) app time · \(d.sessions.count) session\(d.sessions.count == 1 ? "" : "s")"
                         : "No activity this hour")
                        .font(.system(size: 10))
                        .foregroundColor(ThemeColors.textMuted)
                }
                Spacer()
                Button(action: {
                    withAnimation(.easeInOut(duration: 0.18)) { selectedHour = nil }
                }) {
                    Text("✕")
                        .font(.system(size: 11, weight: .semibold))
                        .foregroundColor(ThemeColors.textMuted)
                        .padding(4)
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
            }

            // Apps with share progress bars
            if !d.apps.isEmpty {
                VStack(spacing: 7) {
                    ForEach(d.apps, id: \.appId) { app in
                        let sharePct = d.appSeconds > 0 ? Int((Double(app.seconds) / Double(d.appSeconds) * 100).rounded()) : 0
                        VStack(alignment: .leading, spacing: 3) {
                            HStack {
                                Text(app.name)
                                    .font(.system(size: 12, weight: .semibold))
                                    .foregroundColor(ThemeColors.textPrimary)
                                    .lineLimit(1)
                                Spacer()
                                Text("\(Formatters.paddedHM(app.seconds)) · \(sharePct)%")
                                    .font(.system(size: 10))
                                    .foregroundColor(ThemeColors.textMuted)
                            }
                            GeometryReader { geo in
                                ZStack(alignment: .leading) {
                                    RoundedRectangle(cornerRadius: 3).fill(ThemeColors.barBackground)
                                    RoundedRectangle(cornerRadius: 3)
                                        .fill(Tokens.appColor(app.name))
                                        .frame(width: max(4, geo.size.width * CGFloat(app.seconds) / CGFloat(max(1, d.appSeconds))))
                                }
                            }
                            .frame(height: 6)
                        }
                    }
                }
            }

            // Sessions
            if !d.sessions.isEmpty {
                VStack(alignment: .leading, spacing: 4) {
                    Text("Sessions")
                        .font(.system(size: 10, weight: .semibold))
                        .foregroundColor(ThemeColors.textMuted)
                        .padding(.top, 2)
                    ForEach(Array(d.sessions.enumerated()), id: \.offset) { _, s in
                        HStack {
                            Text(s.appName)
                                .font(.system(size: 11))
                                .foregroundColor(ThemeColors.textPrimary)
                                .lineLimit(1)
                            Spacer()
                            Text("\(Formatters.clock12(s.startUtc)) – \(Formatters.clock12(s.endUtc))")
                                .font(.system(size: 10))
                                .foregroundColor(ThemeColors.textMuted)
                            Text(Formatters.paddedHM(s.seconds))
                                .font(.system(size: 10, weight: .semibold))
                                .foregroundColor(ThemeColors.textPrimary)
                                .frame(width: 54, alignment: .trailing)
                        }
                    }
                }
            }

            // Comparison (Typical for this hour)
            if result.hasTypical {
                Divider().overlay(ThemeColors.borderSubtle).padding(.vertical, 2)
                let delta = d.appSeconds - Int64(d.typicalAppSeconds.rounded())
                let sign = delta >= 0 ? "+" : "-"
                HStack {
                    Text("Typical for this hour")
                        .font(.system(size: 11))
                        .foregroundColor(ThemeColors.textMuted)
                    Spacer()
                    Text("\(Formatters.paddedHM(Int64(d.typicalAppSeconds.rounded()))) · \(sign)\(Formatters.paddedHM(abs(delta)))")
                        .font(.system(size: 11, weight: .semibold))
                        .foregroundColor(delta >= 0 ? ThemeColors.accent : ThemeColors.warning)
                }
            }
        }
        .padding(10)
        .background(RoundedRectangle(cornerRadius: 10).fill(ThemeColors.surfaceActive))
        .overlay(RoundedRectangle(cornerRadius: 10).strokeBorder(ThemeColors.accent, lineWidth: 1))
    }

    private func swatch(_ color: Color) -> some View {
        RoundedRectangle(cornerRadius: 2).fill(color).frame(width: 8, height: 8)
    }

    private var typicalKey: some View {
        HStack(spacing: 5) {
            Rectangle()
                .stroke(ThemeColors.textMuted, style: StrokeStyle(lineWidth: 2, dash: [2, 2]))
                .frame(width: 14, height: 0.5)
            Text("Typical day")
        }
    }

    private func asleepKey(_ label: String) -> some View {
        HStack(spacing: 5) {
            Hatch(color: ThemeColors.textMuted, line: 1.5, gap: 1.5)
                .frame(width: 8, height: 8)
                .clipShape(RoundedRectangle(cornerRadius: 2))
            Text(label)
        }
    }

    private var legend: some View {
        HStack(spacing: split ? 9 : 14) {
            if split {
                HStack(spacing: 5) { swatch(Tokens.ramp[3]); Text("Mac on") }
                HStack(spacing: 5) { swatch(ThemeColors.deviceBDark); Text("PC on") }
                HStack(spacing: 5) {
                    ZStack(alignment: .bottomLeading) {
                        RoundedRectangle(cornerRadius: 1).fill(Tokens.ramp[3]).frame(width: 7, height: 8)
                        RoundedRectangle(cornerRadius: 1).fill(ThemeColors.deviceBDark).frame(width: 7, height: 5).offset(x: 5)
                    }
                    .frame(width: 12, height: 8, alignment: .leading)
                    Text("Overlapping = both on")
                }
                typicalKey
                asleepKey("Asleep")
            } else {
                HStack(spacing: 5) { swatch(ThemeColors.accent); Text("Apps open") }
                typicalKey
                asleepKey("Mac asleep · paused")
            }
        }
        .font(.system(size: 10))
        .foregroundColor(ThemeColors.textMuted)
        .lineLimit(1)
    }
}
