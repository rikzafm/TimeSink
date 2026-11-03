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
    private var effectiveNowUtc: Int64 {
        isLive ? state.liveNowUtc : result.nowUtc
    }
    private var nowFraction: CGFloat {
        CGFloat(effectiveNowUtc - result.dayStartUtc) / CGFloat(max(1, result.dayEndUtc - result.dayStartUtc))
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
        let isSlp = isAsleep(hour)
        let status = d.uptimeSeconds > 0
            ? "\(Formatters.paddedHM(d.uptimeSeconds)) uptime"
            : (d.appSeconds > 0 ? "\(Formatters.paddedHM(d.appSeconds)) app time"
                : (isSlp ? "asleep" : "no activity"))
        var lines = ["\(Formatters.hourRange(hour)) · \(status)"]
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
            EmptyView()
        } else {
            let slp = isAsleep(bucket.hour)
            let devSleepMap: [String: Int64] = Dictionary(grouping: result.sleepBands, by: { $0.deviceId ?? "" })
                .mapValues { bands in
                    let start = result.dayStartUtc + Int64(bucket.hour) * 3600
                    let end = start + 3600
                    return bands.reduce(0) { sum, b in
                        let o = min(b.endUtc, end) - max(b.startUtc, start)
                        return sum + (o > 0 ? o : 0)
                    }
                }
            if split {
                let parts = state.deviceSplit(bucket.byDevice?.mapValues(\.uptimeSeconds))
                let macSec = parts.mac >= 60 ? parts.mac : 0
                let pcSec = parts.pc >= 60 ? parts.pc : 0

                let slpSplit = state.deviceSplit(devSleepMap)
                let macSlpSec = slpSplit.mac >= 60 ? slpSplit.mac : 0
                let pcSlpSec = slpSplit.pc >= 60 ? slpSplit.pc : 0

                let macActiveH = macSec > 0 ? max(2, height(Double(macSec))) : 0
                var macSlpH = macSlpSec > 0 ? max(2, height(Double(macSlpSec))) : 0
                if macActiveH > 0 && macActiveH + macSlpH > barsHeight {
                    macSlpH = max(0, barsHeight - macActiveH)
                }

                let pcActiveH = pcSec > 0 ? max(2, height(Double(pcSec))) : 0
                var pcSlpH = pcSlpSec > 0 ? max(2, height(Double(pcSlpSec))) : 0
                if pcActiveH > 0 && pcActiveH + pcSlpH > barsHeight {
                    pcSlpH = max(0, barsHeight - pcActiveH)
                }

                if macActiveH > 0 || pcActiveH > 0 || macSlpH > 0 || pcSlpH > 0 {
                    OverlapBar(
                        mac: macActiveH,
                        pc: pcActiveH,
                        macColor: bucket.hour == nowHour ? ThemeColors.accent : Tokens.ramp[3],
                        pcColor: ThemeColors.deviceBDark,
                        macSleep: macSlpH,
                        pcSleep: pcSlpH,
                        radius: 2
                    )
                } else if slp {
                    Hatch(color: Tokens.ramp[2])
                        .frame(height: barsHeight * 0.7)
                        .clipShape(RoundedRectangle(cornerRadius: 2))
                } else {
                    EmptyView()
                }
            } else {
                let sec = bucket.uptimeSeconds >= 60 ? bucket.uptimeSeconds : 0
                let totalSlpSec = devSleepMap.values.reduce(0, +)
                let slpHourSec = totalSlpSec >= 60 ? totalSlpSec : (slp ? 1800 : 0)
                let actH = sec > 0 ? max(2, height(Double(sec))) : 0
                var slpH = slpHourSec > 0 ? max(2, height(Double(slpHourSec))) : 0
                if actH > 0 && actH + slpH > barsHeight {
                    slpH = max(0, barsHeight - actH)
                }
                let totalH = actH + slpH
                if totalH > 0 {
                    VStack(spacing: 0) {
                        if slpH > 0 {
                            Hatch(color: bucket.hour == nowHour ? ThemeColors.accent : Tokens.ramp[2])
                                .frame(height: slpH)
                                .clipShape(UnevenRoundedRectangle(topLeadingRadius: 2, topTrailingRadius: 2))
                        }
                        if actH > 0 {
                            UnevenRoundedRectangle(
                                topLeadingRadius: slpH > 0 ? 0 : 2,
                                topTrailingRadius: slpH > 0 ? 0 : 2
                            )
                            .fill(color(sec, hour: bucket.hour))
                            .frame(height: actH)
                        }
                    }
                } else {
                    EmptyView()
                }
            }
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
            Text(Formatters.clock12(effectiveNowUtc))
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
        let localName = state.deviceADisplayName()
        let remoteName = state.deviceBDisplayName()

        let devSleepMap: [String: Int64] = Dictionary(grouping: result.sleepBands, by: { $0.deviceId ?? "" })
            .mapValues { bands in
                let start = result.dayStartUtc + Int64(hour) * 3600
                let end = start + 3600
                return bands.reduce(0) { sum, b in
                    let o = min(b.endUtc, end) - max(b.startUtc, start)
                    return sum + (o > 0 ? o : 0)
                }
            }
        let slpSplit = state.deviceSplit(devSleepMap)
        let macSlpSec = slpSplit.mac >= 60 ? slpSplit.mac : 0
        let pcSlpSec = slpSplit.pc >= 60 ? slpSplit.pc : 0

        let subtitleText: String
        if d.uptimeSeconds > 0 || d.appSeconds > 0 {
            subtitleText = "\(Formatters.paddedHM(d.uptimeSeconds)) uptime · \(Formatters.paddedHM(d.appSeconds)) app time"
        } else if macSlpSec >= 60 && pcSlpSec >= 60 {
            subtitleText = "Both devices asleep"
        } else if macSlpSec >= 60 {
            subtitleText = "\(localName) asleep \(Formatters.paddedHM(macSlpSec))"
        } else if pcSlpSec >= 60 {
            subtitleText = "\(remoteName) asleep \(Formatters.paddedHM(pcSlpSec))"
        } else if isAsleep(hour) {
            subtitleText = "\(localName) asleep · paused"
        } else {
            subtitleText = "No activity this hour"
        }

        return VStack(alignment: .leading, spacing: 12) {
            // Header
            HStack(alignment: .top) {
                VStack(alignment: .leading, spacing: 3) {
                    Text(Formatters.hour24Range(hour))
                        .font(.system(size: 18, weight: .bold))
                        .foregroundColor(ThemeColors.textPrimary)
                    Text(subtitleText)
                        .font(.system(size: 11))
                        .foregroundColor(ThemeColors.textMuted)
                }
                Spacer()
                HStack(spacing: 8) {
                    if !d.sessions.isEmpty {
                        Text("\(d.sessions.count) session\(d.sessions.count == 1 ? "" : "s")")
                            .font(.system(size: 11))
                            .foregroundColor(ThemeColors.textMuted)
                            .padding(.top, 2)
                    }
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
            }

            // Dedicated device breakdown row
            let split = state.deviceSplit(d.byDevice?.mapValues { $0.uptimeSeconds > 0 ? $0.uptimeSeconds : $0.appSeconds })
            let showDevRow = state.showsDeviceSplit || macSlpSec >= 60 || pcSlpSec >= 60
            if showDevRow && (split.mac > 0 || split.pc > 0 || macSlpSec >= 60 || pcSlpSec >= 60) {
                HStack(spacing: 12) {
                    if split.mac > 0 || macSlpSec >= 60 {
                        HStack(spacing: 4) {
                            Circle().fill(ThemeColors.accent).frame(width: 7, height: 7)
                            let macText = split.mac > 0 && macSlpSec >= 60
                                ? "\(localName) \(Formatters.paddedHM(split.mac)) · asleep \(Formatters.paddedHM(macSlpSec))"
                                : (split.mac > 0 ? "\(localName) \(Formatters.paddedHM(split.mac))" : "\(localName) asleep \(Formatters.paddedHM(macSlpSec))")
                            Text(macText)
                                .font(.system(size: 11))
                                .foregroundColor(ThemeColors.textPrimary)
                        }
                    }
                    if split.pc > 0 || pcSlpSec >= 60 {
                        HStack(spacing: 4) {
                            RoundedRectangle(cornerRadius: 1.5).fill(ThemeColors.deviceB).frame(width: 7, height: 7)
                            let pcText = split.pc > 0 && pcSlpSec >= 60
                                ? "\(remoteName) \(Formatters.paddedHM(split.pc)) · asleep \(Formatters.paddedHM(pcSlpSec))"
                                : (split.pc > 0 ? "\(remoteName) \(Formatters.paddedHM(split.pc))" : "\(remoteName) asleep \(Formatters.paddedHM(pcSlpSec))")
                            Text(pcText)
                                .font(.system(size: 11))
                                .foregroundColor(ThemeColors.textPrimary)
                        }
                    }
                    if d.onBothSeconds > 0 {
                        Text("\(Formatters.paddedHM(d.onBothSeconds)) on both")
                            .font(.system(size: 11))
                            .foregroundColor(ThemeColors.textMuted)
                    }
                }
            }
            // Segmented app time bar & 2-column app list
            if !d.apps.isEmpty {
                segmentedBar(d.apps, totalSeconds: d.appSeconds)
                twoColumnAppList(d.apps)
            }

            // Session summary note
            let summary = sessionSummaryText(d)
            if !summary.isEmpty {
                Text(summary)
                    .font(.system(size: 11))
                    .foregroundColor(ThemeColors.textMuted)
                    .padding(.top, 2)
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
        .padding(14)
        .background(RoundedRectangle(cornerRadius: 12).fill(ThemeColors.surfaceActive))
        .overlay(RoundedRectangle(cornerRadius: 12).strokeBorder(ThemeColors.accent, lineWidth: 1))
    }

    private func segmentedBar(_ apps: [BarApp], totalSeconds: Int64) -> some View {
        GeometryReader { geo in
            let total = max(1, totalSeconds)
            let spacing: CGFloat = 2
            let totalSpacing = spacing * CGFloat(max(0, apps.count - 1))
            let availableWidth = max(0, geo.size.width - totalSpacing)
            HStack(spacing: spacing) {
                ForEach(apps, id: \.appId) { app in
                    let w = max(2, availableWidth * CGFloat(app.seconds) / CGFloat(total))
                    RoundedRectangle(cornerRadius: 2)
                        .fill(Tokens.appColor(app.name))
                        .frame(width: w, height: 8)
                }
            }
        }
        .frame(height: 8)
        .clipShape(RoundedRectangle(cornerRadius: 4))
    }

    private func twoColumnAppList(_ apps: [BarApp]) -> some View {
        let pairs = stride(from: 0, to: apps.count, by: 2).map { i -> (BarApp, BarApp?) in
            (apps[i], i + 1 < apps.count ? apps[i + 1] : nil)
        }
        return VStack(spacing: 7) {
            ForEach(Array(pairs.enumerated()), id: \.offset) { _, pair in
                HStack(spacing: 16) {
                    appRow(pair.0)
                        .frame(maxWidth: .infinity)
                    if let second = pair.1 {
                        appRow(second)
                            .frame(maxWidth: .infinity)
                    } else {
                        Spacer()
                            .frame(maxWidth: .infinity)
                    }
                }
            }
        }
        .padding(.top, 2)
    }

    private func appRow(_ app: BarApp) -> some View {
        Button(action: { state.openAppDetail(appId: app.appId) }) {
            HStack(spacing: 7) {
                Circle()
                    .fill(Tokens.appColor(app.name))
                    .frame(width: 8, height: 8)
                Text(app.name)
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundColor(ThemeColors.textPrimary)
                    .lineLimit(1)
                Spacer(minLength: 4)
                Text(Formatters.paddedHM(app.seconds))
                    .font(.system(size: 12, weight: .bold))
                    .foregroundColor(ThemeColors.textPrimary)
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help("Click to inspect \(app.name)")
    }

    private func sessionSummaryText(_ d: HourDetail) -> String {
        guard !d.sessions.isEmpty else { return "" }
        let hourStart = d.startUtc
        let hourEnd = d.endUtc

        let allFull = d.sessions.allSatisfy { $0.startUtc == hourStart && $0.endUtc == hourEnd }
        if allFull && !d.apps.isEmpty {
            let totalSec = max(1, d.appSeconds)
            let share = Int((Double(d.apps[0].seconds) / Double(totalSec) * 100).rounded())
            let startStr = Formatters.clock24(hourStart)
            let endStr = Formatters.clock24(hourEnd)
            return "Every session ran \(startStr) → \(endStr) · each app \(share)%"
        }

        let minStart = d.sessions.map(\.startUtc).min() ?? hourStart
        let maxEnd = d.sessions.map(\.endUtc).max() ?? hourEnd
        let spanStr = "Active \(Formatters.clock24(minStart)) → \(Formatters.clock24(maxEnd))"
        if let top = d.apps.first {
            let pct = d.appSeconds > 0 ? Int((Double(top.seconds) / Double(d.appSeconds) * 100).rounded()) : 0
            return "\(spanStr) · Top app: \(top.name) (\(Formatters.paddedHM(top.seconds)), \(pct)%)"
        }
        return spanStr
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
                asleepKey("Asleep")
            } else {
                HStack(spacing: 5) { swatch(ThemeColors.accent); Text("Apps open") }
                asleepKey("Mac asleep · paused")
            }
        }
        .font(.system(size: 10))
        .foregroundColor(ThemeColors.textMuted)
        .lineLimit(1)
    }
}