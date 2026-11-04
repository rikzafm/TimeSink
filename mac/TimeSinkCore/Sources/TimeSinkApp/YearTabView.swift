import SwiftUI
import TimeSinkCore

public struct YearTabView: View {
    @ObservedObject var state: AppState
    @State private var selectedMonth: Int?
    @State private var hoveredMonth: Int?
    @State private var selectedRow: Int64?
    @State private var savedFeedback = false

    public init(state: AppState) {
        self.state = state
    }

    private var isWrapped: Bool { state.yearSubView == .wrapped }

    public var body: some View {
        VStack(spacing: isWrapped ? 10 : 12) {
            yearSubViewSwitcher

            switch state.yearSubView {
            case .pace:
                ScrollView(.vertical, showsIndicators: false) { paceView }
            case .wrapped:
                wrappedView
            case .matrix:
                ScrollView(.vertical, showsIndicators: false) { matrixView }
            }
        }
        .padding(.horizontal, isWrapped ? 16 : 14)
        .padding(.top, isWrapped ? 10 : 12)
    }

    private var yearSubViewSwitcher: some View {
        HStack(spacing: 4) {
            ForEach(YearSubView.allCases, id: \.self) { sub in
                let selected = state.yearSubView == sub
                Button(action: { state.yearSubView = sub }) {
                    Text(sub.rawValue)
                        .font(.system(size: 11, weight: selected ? .semibold : .medium))
                        .foregroundColor(selected ? ThemeColors.background : ThemeColors.textDim)
                        .padding(.horizontal, 10)
                        .padding(.vertical, 4)
                        .background(
                            RoundedRectangle(cornerRadius: 6)
                                .fill(selected ? ThemeColors.accent : Color.clear)
                        )
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
            }
            Spacer()
            yearNavigator
        }
    }

    private var yearNavigator: some View {
        let currentYear = Calendar.current.component(.year, from: Date()) - state.yearOffset
        return HStack(spacing: 6) {
            PeriodArrow(left: true, enabled: state.canYearGoPrev, box: 22, iconSize: 10) {
                state.previousYear()
            }
            Text(String(currentYear))
                .font(.system(size: 13, weight: .semibold))
                .foregroundColor(ThemeColors.textPrimary)
            PeriodArrow(left: false, enabled: state.canYearGoNext, box: 22, iconSize: 10) {
                state.nextYear()
            }
        }
    }

    // MARK: - Pace

    private func h(_ seconds: Int64) -> String { Formatters.hours(seconds) }

    private func monthValueLabel(_ m: YearMonth) -> String {
        switch m.kind {
        case .beforeTracking:
            return ""
        case .future:
            return "~\(Formatters.decimalHours(m.projectedSeconds))"
        case .current:
            return Formatters.decimalHours(m.seconds)
        case .tracked:
            return m.seconds > 0 ? Formatters.decimalHours(m.seconds) : ""
        }
    }

    @ViewBuilder
    private var paceView: some View {
        if let result = state.yearResult {
            let activeMonth = selectedMonth ?? result.months.first(where: { $0.kind == .current })?.month ?? result.bestMonth ?? 1
            let scope: String = {
                if state.deviceScope == .all {
                    return "uptime, all devices (overlap once)"
                }
                if let totals = result.deviceTotals, !totals.isEmpty {
                    let hasLocal = totals.keys.contains { state.isLocalDevice($0) && (totals[$0] ?? 0) > 0 }
                    let hasRemote = totals.keys.contains { !state.isLocalDevice($0) && (totals[$0] ?? 0) > 0 }
                    if !hasLocal && hasRemote {
                        return "PC uptime"
                    }
                    if hasLocal && !hasRemote {
                        return "Mac uptime"
                    }
                }
                return "PC uptime"
            }()
            VStack(alignment: .leading, spacing: 12) {
                HStack(alignment: .bottom) {
                    VStack(alignment: .leading, spacing: 0) {
                        Text(state.yearOffset == 0 ? "\(String(result.year)) so far · \(scope)" : "\(String(result.year)) · \(scope)")
                            .font(.system(size: 11))
                            .foregroundColor(ThemeColors.textMuted)
                        Text(Formatters.paddedHM(result.totalSeconds))
                            .font(.system(size: 30, weight: .semibold))
                            .tracking(-0.5)
                            .foregroundColor(ThemeColors.accent)
                            .frame(height: 36)
                    }
                    Spacer()
                    VStack(alignment: .trailing, spacing: 0) {
                        if let avg = result.averageMonthSeconds { Text("Avg \(Formatters.decimalHours(avg)) h / month").frame(height: 16) }
                        if let best = result.bestMonth {
                            Text("Best \(Formatters.monthName(best, short: true)) \(Formatters.decimalHours(result.bestMonthSeconds)) h").frame(height: 16)
                        }
                    }
                    .font(.system(size: 11))
                    .foregroundColor(ThemeColors.textMuted)
                }

                if state.showsDeviceSplit {
                    let t = state.deviceSplit(result.deviceTotals)
                    let localName = state.deviceADisplayName()
                    let remoteName = state.deviceBDisplayName()
                    DeviceLegend(mac: "\(localName) \(h(t.mac))", pc: "\(remoteName) \(h(t.pc))", both: "\(h(result.onBothSeconds)) on both")
                }

                if let pace = result.paceYearSeconds {
                    let paceHours = "\(Formatters.decimalHours(pace)) h"
                    let bannerText: String = {
                        if result.recentMonthCount > 1 {
                            return "On pace for \(paceHours) by December, based on your last \(result.recentMonthCount) months"
                        } else {
                            return "On pace for \(paceHours) by December, based on this month so far"
                        }
                    }()
                    Text(bannerText)
                        .font(.system(size: 12))
                        .foregroundColor(ThemeColors.textPrimary)
                        .lineLimit(1)
                        .minimumScaleFactor(0.85)
                        .padding(.horizontal, 12)
                        .padding(.vertical, 9)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .card(fill: ThemeColors.surface, border: ThemeColors.borderSubtle, radius: 10)
                }

                paceChart(result)

                monthCard(result, activeMonth: activeMonth)
            }
        }
    }

    private func paceChart(_ result: YearResult) -> some View {
        let height: CGFloat = 160
        let top = max(3600, max(result.months.map { max($0.seconds, $0.projectedSeconds) }.max() ?? 3600, result.averageMonthSeconds ?? 0))
        let plotHeight: CGFloat = 126
        let k = plotHeight / CGFloat(top)
        let activeMonth = selectedMonth ?? result.months.first(where: { $0.kind == .current })?.month ?? result.bestMonth ?? 1
        let currentMonth = result.months.first(where: { $0.kind == .current })?.month

        return VStack(alignment: .leading, spacing: 0) {
            ZStack(alignment: .bottom) {
                // Dashed monthly average reference line
                if let avg = result.averageMonthSeconds, avg > 0 {
                    let avgY = CGFloat(avg) * k
                    GeometryReader { geo in
                        let lineY = geo.size.height - avgY
                        Path { path in
                            path.move(to: CGPoint(x: 0, y: lineY))
                            path.addLine(to: CGPoint(x: geo.size.width, y: lineY))
                        }
                        .stroke(ThemeColors.textMuted.opacity(0.35), style: StrokeStyle(lineWidth: 1, dash: [4, 3]))
                    }
                }

                // 12 monthly columns
                HStack(alignment: .bottom, spacing: 4) {
                    ForEach(result.months, id: \.month) { m in
                        let isSelected = m.month == activeMonth
                        let isHovered = hoveredMonth == m.month
                        let isCur = m.month == currentMonth
                        let valText = monthValueLabel(m)

                        let tooltipText: String = {
                            let name = Formatters.monthName(m.month)
                            switch m.kind {
                            case .beforeTracking:
                                return "\(name) \(result.year)\nBefore tracking began"
                            case .future:
                                return "\(name) \(result.year) (Projected)\n~\(Formatters.decimalHours(m.projectedSeconds)) h"
                            case .current:
                                return "\(name) \(result.year) (Current)\n\(Formatters.decimalHours(m.seconds)) h tracked · ~\(Formatters.decimalHours(m.projectedSeconds)) h projected"
                            case .tracked:
                                return "\(name) \(result.year)\n\(Formatters.decimalHours(m.seconds)) h tracked"
                            }
                        }()

                        Button(action: { selectedMonth = m.month }) {
                            ZStack(alignment: .bottom) {
                                // Background highlight for selected / hovered
                                if isSelected {
                                    RoundedRectangle(cornerRadius: 3)
                                        .fill(ThemeColors.surfaceRaised)
                                        .frame(height: height)
                                } else if isHovered {
                                    RoundedRectangle(cornerRadius: 3)
                                        .fill(ThemeColors.surfaceRaised.opacity(0.5))
                                        .frame(height: height)
                                }

                                VStack(spacing: 2) {
                                    // Top value label
                                    Text(valText)
                                        .font(.system(size: 9.5, weight: isSelected ? .bold : .medium))
                                        .foregroundColor(isCur ? ThemeColors.accent : ThemeColors.textMuted)
                                        .lineLimit(1)
                                        .minimumScaleFactor(0.65)
                                        .frame(height: 14)

                                    // Bar representation
                                    paceColumn(m, k: k)
                                        .frame(maxWidth: .infinity, maxHeight: plotHeight, alignment: .bottom)
                                }
                                .padding(.horizontal, 1)
                            }
                            .frame(maxWidth: .infinity, maxHeight: height, alignment: .bottom)
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                        .help(tooltipText)
                        .onHover { isH in
                            hoveredMonth = isH ? m.month : (hoveredMonth == m.month ? nil : hoveredMonth)
                        }
                    }
                }
            }
            .frame(height: height)

            // Baseline
            Rectangle()
                .fill(ThemeColors.borderSubtle)
                .frame(height: 1)

            // Month labels row below bars
            HStack(spacing: 4) {
                ForEach(result.months, id: \.month) { m in
                    let isSelected = m.month == activeMonth
                    Button(action: { selectedMonth = m.month }) {
                        Text(Formatters.monthName(m.month, short: true))
                            .font(.system(size: 10, weight: isSelected ? .bold : .regular))
                            .foregroundColor(isSelected ? ThemeColors.textPrimary : ThemeColors.textMuted)
                            .frame(maxWidth: .infinity)
                            .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .help(Formatters.monthName(m.month))
                }
            }
            .padding(.top, 4)

            // Legend
            HStack(spacing: 12) {
                if state.showsDeviceSplit {
                    HStack(spacing: 4) { DeviceMarker(isLocal: true, size: 7); Text("Mac") }
                    HStack(spacing: 4) { DeviceMarker(isLocal: false, size: 7); Text("PC") }
                } else {
                    HStack(spacing: 4) {
                        RoundedRectangle(cornerRadius: 1.5).fill(Tokens.ramp[3]).frame(width: 8, height: 7)
                        Text("Tracked")
                    }
                }
                HStack(spacing: 4) {
                    ZStack {
                        Hatch(color: Tokens.ramp[3], line: 1.5, gap: 2)
                        RoundedRectangle(cornerRadius: 1)
                            .strokeBorder(Tokens.ramp[3], style: StrokeStyle(lineWidth: 1, dash: [2, 1]))
                    }
                    .frame(width: 10, height: 7)
                    Text("Projected")
                }
                HStack(spacing: 4) {
                    Rectangle().stroke(ThemeColors.textMuted, style: StrokeStyle(lineWidth: 1, dash: [3, 2])).frame(width: 12, height: 0.5)
                    Text("Monthly avg")
                }

                Spacer()

                Text("Tap a month")
                    .foregroundColor(ThemeColors.textMuted)
            }
            .font(.system(size: 10))
            .foregroundColor(ThemeColors.textMuted)
            .padding(.top, 8)
        }
    }

    @ViewBuilder
    private func paceColumn(_ m: YearMonth, k: CGFloat) -> some View {
        switch m.kind {
        case .beforeTracking:
            Rectangle()
                .stroke(ThemeColors.textMuted.opacity(0.4), style: StrokeStyle(lineWidth: 1, dash: [1, 2]))
                .frame(height: 1)
        case .future:
            let projH = CGFloat(m.projectedSeconds) * k
            if projH > 0.5 {
                ZStack(alignment: .bottom) {
                    Hatch(color: Tokens.ramp[3], line: 1.5, gap: 3)
                        .clipShape(UnevenRoundedRectangle(topLeadingRadius: 2, topTrailingRadius: 2))
                    OpenDashedBorder(color: Tokens.ramp[3], radius: 2)
                }
                .frame(height: projH)
            }
        case .tracked:
            if state.showsDeviceSplit {
                let parts = state.deviceSplit(m.deviceSeconds)
                let mac = CGFloat(parts.mac) * k
                let pc = CGFloat(parts.pc) * k
                OverlapBar(mac: mac, pc: pc, macColor: ThemeColors.deviceA, pcColor: ThemeColors.deviceB)
                    .frame(height: max(mac, pc))
            } else {
                let barH = CGFloat(m.seconds) * k
                if barH > 0.5 {
                    UnevenRoundedRectangle(topLeadingRadius: 2, topTrailingRadius: 2)
                        .fill(Tokens.ramp[3])
                        .frame(height: barH)
                }
            }
        case .current:
            let actH = CGFloat(m.seconds) * k
            let extraH = CGFloat(max(0, m.projectedSeconds - m.seconds)) * k
            VStack(spacing: 0) {
                if extraH > 0.5 {
                    ZStack(alignment: .bottom) {
                        Hatch(color: Tokens.ramp[3], line: 1.5, gap: 3)
                            .clipShape(UnevenRoundedRectangle(topLeadingRadius: 2, topTrailingRadius: 2))
                        OpenDashedBorder(color: Tokens.ramp[3], radius: 2)
                    }
                    .frame(height: extraH)
                }
                if state.showsDeviceSplit {
                    let parts = state.deviceSplit(m.deviceSeconds)
                    let mac = CGFloat(parts.mac) * k
                    let pc = CGFloat(parts.pc) * k
                    OverlapBar(mac: mac, pc: pc, macColor: ThemeColors.deviceA, pcColor: ThemeColors.deviceB)
                        .frame(height: max(mac, pc))
                } else if actH > 0.5 {
                    Rectangle()
                        .fill(ThemeColors.accent)
                        .frame(height: actH)
                }
            }
        }
    }

    @ViewBuilder
    private func monthCard(_ result: YearResult, activeMonth: Int) -> some View {
        if let month = result.months.first(where: { $0.month == activeMonth }) {
            let isFuture = month.kind == .future
            let isBefore = month.kind == .beforeTracking

            let title = Formatters.monthName(month.month) + (isFuture ? " (projected)" : "")
            let valText: String = {
                if isBefore { return "—" }
                if isFuture { return "~\(Formatters.decimalHours(month.projectedSeconds)) h" }
                return "\(Formatters.decimalHours(month.seconds)) h"
            }()

            VStack(alignment: .leading, spacing: 6) {
                HStack {
                    Text(title)
                        .font(.system(size: 13, weight: .bold))
                        .foregroundColor(ThemeColors.textPrimary)
                    Spacer()
                    Text(valText)
                        .font(.system(size: 14, weight: .bold))
                        .foregroundColor(ThemeColors.accent)
                }

                Text(monthLine(month, year: result.year, bestMonth: result.bestMonth))
                    .font(.system(size: 11))
                    .foregroundColor(ThemeColors.textMuted)
                    .lineLimit(1)

                if !month.topApps.isEmpty {
                    let maxSecs = CGFloat(max(1, month.topApps.first?.seconds ?? 1))
                    VStack(spacing: 6) {
                        ForEach(month.topApps.prefix(4), id: \.appId) { app in
                            PaceAppRowItem(
                                state: state,
                                app: app,
                                maxSecs: maxSecs,
                                year: result.year,
                                month: month.month
                            )
                        }
                    }
                    .padding(.top, 4)
                } else if month.kind == .tracked || month.kind == .current {
                    Text("No app time recorded")
                        .font(.system(size: 11))
                        .foregroundColor(ThemeColors.textMuted)
                        .padding(.top, 2)
                }
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 10)
            .card()
        }
    }

    private func monthLine(_ m: YearMonth, year: Int, bestMonth: Int?) -> String {
        switch m.kind {
        case .beforeTracking:
            return "Before tracking began"
        case .future:
            return "Projected from your recent months"
        case .current:
            let tracked = "\(Formatters.decimalHours(m.seconds)) h"
            let projected = "\(Formatters.decimalHours(m.projectedSeconds)) h"
            if let prev = m.previousSeconds, prev > 0 {
                let pct = Int((Double(m.projectedSeconds - prev) / Double(prev) * 100).rounded())
                let sign = pct >= 0 ? "+" : ""
                let prevName = Formatters.monthName(m.month == 1 ? 12 : m.month - 1, short: true)
                return "\(tracked) so far · on pace for \(projected), \(sign)\(pct)% vs \(prevName)"
            } else {
                return "\(tracked) so far · on pace for \(projected)"
            }
        case .tracked:
            var parts: [String] = []
            if let prev = m.previousSeconds, prev > 0 {
                let pct = Int((Double(m.seconds - prev) / Double(prev) * 100).rounded())
                let sign = pct >= 0 ? "+" : ""
                let prevName = Formatters.monthName(m.month == 1 ? 12 : m.month - 1, short: true)
                parts.append("\(sign)\(pct)% vs \(prevName)")
            }
            if m.month == bestMonth {
                parts.append("your best month")
            }
            return parts.isEmpty ? "No apps tracked" : parts.joined(separator: " · ")
        }
    }

    // MARK: - Wrapped

    @ViewBuilder
    private var wrappedView: some View {
        if let result = state.wrappedResult {
            let count = WrappedCardView.cardCount(state: state, result: result)
            let curIndex = min(state.wrappedCardIndex, max(0, count - 1))
            VStack(spacing: 10) {
                WrappedCardView(state: state, result: result, index: curIndex)
                    .frame(maxHeight: .infinity)
                    .contentShape(Rectangle())
                    .gesture(
                        DragGesture(minimumDistance: 25)
                            .onEnded { value in
                                let dx = value.translation.width
                                if dx < -40 && curIndex < count - 1 {
                                    state.wrappedCardIndex = curIndex + 1
                                } else if dx > 40 && curIndex > 0 {
                                    state.wrappedCardIndex = curIndex - 1
                                }
                            }
                    )

                HStack {
                    WrappedDeckPager(
                        count: count,
                        currentIndex: curIndex,
                        onSelect: { target in
                            state.wrappedCardIndex = max(0, min(count - 1, target))
                        }
                    )

                    Spacer()

                    Button(action: {
                        if state.saveWrappedImage() {
                            savedFeedback = true
                            DispatchQueue.main.asyncAfter(deadline: .now() + 3) {
                                savedFeedback = false
                            }
                        }
                    }) {
                        Text(savedFeedback ? "Saved ✓" : "Save as image")
                            .font(.system(size: 12, weight: .bold))
                            .foregroundColor(ThemeColors.background)
                            .padding(.horizontal, 16)
                            .frame(height: 32)
                            .background(Capsule().fill(ThemeColors.accent))
                            .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                }
                .frame(height: 34)
            }
        }
    }
}

// MARK: - Dynamic Sliding-Window Pager

struct WrappedDeckPager: View {
    let count: Int
    let currentIndex: Int
    let onSelect: (Int) -> Void

    private let buttonSize: CGFloat = 32
    private let buttonGap: CGFloat = 8
    private let dotDiameter: CGFloat = 6
    private let activeDotWidth: CGFloat = 20
    private let dotSpacing: CGFloat = 6
    private let maxViewportWidth: CGFloat = 92
    private let dotBaseColor = Color(hex: 0x627352)

    var body: some View {
        let (vpWidth, totalTrackWidth) = computeViewport(count: count)
        let (centers, widths, scrollOffset) = computeTrack(
            activeIdx: currentIndex,
            count: count,
            vpWidth: vpWidth,
            totalTrackWidth: totalTrackWidth
        )
        let maxScroll = max(0, totalTrackWidth - vpWidth)

        HStack(spacing: buttonGap) {
            deckButton("‹", enabled: currentIndex > 0) {
                onSelect(currentIndex - 1)
            }

            ZStack(alignment: .leading) {
                ForEach(0..<count, id: \.self) { i in
                    let cx = centers[i] - scrollOffset
                    let isActive = (i == currentIndex)
                    let baseW = widths[i]
                    let scale = computeDotScale(
                        cx: cx,
                        vpWidth: vpWidth,
                        scrollOffset: scrollOffset,
                        maxScroll: maxScroll
                    )
                    let w = max(2, baseW * scale)
                    let h = max(2, dotDiameter * scale)

                    Button(action: { onSelect(i) }) {
                        Capsule()
                            .fill(isActive ? ThemeColors.accent : dotBaseColor)
                            .frame(width: w, height: h)
                            .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .position(x: cx, y: buttonSize / 2)
                    .opacity(cx < -4 || cx > vpWidth + 4 ? 0 : 1)
                    .animation(.spring(response: 0.30, dampingFraction: 0.85), value: currentIndex)
                }
            }
            .frame(width: vpWidth, height: buttonSize)
            .clipped()

            deckButton("›", enabled: currentIndex < count - 1) {
                onSelect(currentIndex + 1)
            }
        }
    }

    private func computeViewport(count: Int) -> (CGFloat, CGFloat) {
        guard count > 0 else { return (0, 0) }
        let total = CGFloat(count - 1) * (dotDiameter + dotSpacing) + activeDotWidth
        return (min(maxViewportWidth, total), total)
    }

    private func computeTrack(activeIdx: Int, count: Int, vpWidth: CGFloat, totalTrackWidth: CGFloat) -> ([CGFloat], [CGFloat], CGFloat) {
        guard count > 0 else { return ([], [], 0) }
        var widths = [CGFloat](repeating: dotDiameter, count: count)
        var centers = [CGFloat](repeating: 0, count: count)

        for i in 0..<count {
            widths[i] = (i == activeIdx) ? activeDotWidth : dotDiameter
        }

        centers[0] = widths[0] / 2
        for i in 1..<count {
            centers[i] = centers[i - 1] + widths[i - 1] / 2 + dotSpacing + widths[i] / 2
        }

        if totalTrackWidth <= vpWidth {
            return (centers, widths, -(vpWidth - totalTrackWidth) / 2)
        }
        let activeCenter = centers[min(count - 1, max(0, activeIdx))]
        let scroll = max(0, min(totalTrackWidth - vpWidth, activeCenter - vpWidth / 2))
        return (centers, widths, scroll)
    }

    private func computeDotScale(cx: CGFloat, vpWidth: CGFloat, scrollOffset: CGFloat, maxScroll: CGFloat) -> CGFloat {
        guard maxScroll > 0.5 else { return 1.0 }

        let leftShrink = maxScroll > 0.5 ? min(1.0, max(0.0, scrollOffset / 10.0)) : 0.0
        let rightShrink = maxScroll > 0.5 ? min(1.0, max(0.0, (maxScroll - scrollOffset) / 10.0)) : 0.0

        var scaleL: CGFloat = 1.0
        if leftShrink > 0 {
            if cx < 6 { scaleL = 0.42 }
            else if cx < 16 { scaleL = 0.42 + (cx - 6) / 10.0 * 0.58 }
            scaleL = 1.0 - (1.0 - scaleL) * leftShrink
        }

        var scaleR: CGFloat = 1.0
        let dR = vpWidth - cx
        if rightShrink > 0 {
            if dR < 6 { scaleR = 0.42 }
            else if dR < 16 { scaleR = 0.42 + (dR - 6) / 10.0 * 0.58 }
            scaleR = 1.0 - (1.0 - scaleR) * rightShrink
        }

        return min(scaleL, scaleR)
    }

    private func deckButton(_ glyph: String, enabled: Bool, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            ZStack {
                RoundedRectangle(cornerRadius: 8)
                    .fill(ThemeColors.controlBackground)
                RoundedRectangle(cornerRadius: 8)
                    .strokeBorder(ThemeColors.border, lineWidth: 1)
                Text(glyph)
                    .font(.system(size: 15, weight: .medium))
                    .foregroundColor(enabled ? ThemeColors.textPrimary : ThemeColors.textDisabled)
            }
            .frame(width: buttonSize, height: buttonSize)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .disabled(!enabled)
        .opacity(enabled ? 1 : 0.45)
    }
}

extension YearTabView {

    // MARK: - Matrix

    @ViewBuilder
    private var matrixView: some View {
        if let result = state.matrixResult {
            let row = result.rows.first { $0.appId == selectedRow } ?? result.rows.first
            VStack(alignment: .leading, spacing: 12) {
                Text("\(String(result.year)) · hours per app per month · since \(Formatters.monthName(result.firstMonth, short: true)) · \(state.deviceScope == .all ? "all devices" : "this Mac")")
                    .font(.system(size: 11))
                    .foregroundColor(ThemeColors.textMuted)
                    .lineLimit(1)

                matrixTable(result, selected: row?.appId)

                HStack(spacing: 8) {
                    Text("Colour is relative to each app's own best month.")
                    Spacer()
                    HStack(spacing: 2) {
                        ForEach(0..<5, id: \.self) { RoundedRectangle(cornerRadius: 2).fill(Tokens.heat($0)).frame(width: 12, height: 8) }
                    }
                }
                .font(.system(size: 10))
                .foregroundColor(ThemeColors.textMuted)

                if let row { matrixCard(row, result: result) }
            }
        }
    }

    private func matrixTable(_ result: MatrixResult, selected: Int64?) -> some View {
        let months = result.months
        return VStack(spacing: 3) {
            HStack(spacing: 3) {
                Color.clear.frame(width: 78, height: 1)
                ForEach(months, id: \.month) { m in
                    Text(String(Formatters.monthName(m.month).prefix(1))).frame(maxWidth: .infinity)
                }
                Text("Total").frame(width: 40, alignment: .trailing)
            }
            .font(.system(size: 10))
            .foregroundColor(ThemeColors.textMuted)

            ForEach(result.rows, id: \.appId) { row in
                let on = row.appId == selected
                MatrixRowItem(
                    state: state,
                    row: row,
                    selected: on,
                    year: result.year,
                    onSelect: { selectedRow = row.appId }
                )
            }

            HStack(spacing: 3) {
                Text("Total")
                    .font(.system(size: 11, weight: .semibold))
                    .foregroundColor(ThemeColors.textPrimary)
                    .frame(width: 78, alignment: .leading)
                ForEach(months, id: \.month) { m in
                    Text("\((m.totalSeconds + 1800) / 3600)")
                        .font(.system(size: 10, weight: .semibold))
                        .foregroundColor(ThemeColors.textDim)
                        .frame(maxWidth: .infinity)
                }
                Text(Formatters.grouped((result.grandSeconds + 1800) / 3600))
                    .font(.system(size: 11, weight: .bold))
                    .foregroundColor(ThemeColors.accent)
                    .frame(width: 40, alignment: .trailing)
            }
            .padding(.top, 6)
            .topRule()
        }
        .padding(.top, 10)
        .padding(.horizontal, 8)
        .padding(.bottom, 8)
        .card()
    }

    private func matrixCard(_ row: MatrixRow, result: MatrixResult) -> some View {
        let secs = row.cells.map(\.seconds)
        let last3 = secs.suffix(3).reduce(0, +)
        let prev3 = secs.dropLast(3).suffix(3).reduce(0, +)
        var line = "Peak \(Formatters.monthName(row.peakMonth, short: true)) (\(h(row.peakSeconds)))."
        if prev3 > 0 {
            let pct = Int((Double(last3 - prev3) / Double(prev3) * 100).rounded())
            line += " Last 3 months \(pct >= 0 ? "up" : "down") \(abs(pct))% on the 3 before."
        }
        return VStack(alignment: .leading, spacing: 4) {
            HStack {
                Text(row.name)
                Spacer()
                Text("\(h(row.totalSeconds)) · \(row.sharePercent)%")
            }
            .font(.system(size: 12, weight: .semibold))
            .foregroundColor(ThemeColors.textPrimary)

            Text(line)
                .font(.system(size: 11))
                .foregroundColor(ThemeColors.textMuted)
                .lineLimit(1)
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 10)
        .card()
    }
}

/// One card of the Wrapped deck. Also what Save as image renders.
public struct WrappedCardView: View {
    public enum CardItem {
        case total
        case topApps
        case deviceTop(eyebrow: String, title: String, podium: [WrappedApp], others: [WrappedApp])
        case games
        case gacha
        case socialMedia
        case browsing
        case coding
        case productivity
        case entertainment
        case longestSessions
        case habits
    }

    public static func cards(state: AppState, result: WrappedResult) -> [CardItem] {
        var list: [CardItem] = [.total, .topApps]
        if state.deviceScope == .all {
            if state.wrappedGroupByPlatform {
                // Local device first on Mac: Mac then PC
                if let macTop = result.macTop, !macTop.podium.isEmpty {
                    let title = macTop.podium.first.map { "\(.name) kept you company most." } ?? "No apps yet."
                    list.append(.deviceTop(eyebrow: "YOUR MAC'S TOP APPS", title: title, podium: macTop.podium, others: macTop.others))
                }
                if let pcTop = result.pcTop, !pcTop.podium.isEmpty {
                    let title = pcTop.podium.first.map { "\(.name) kept you company most." } ?? "No apps yet."
                    list.append(.deviceTop(eyebrow: "YOUR PC'S TOP APPS", title: title, podium: pcTop.podium, others: pcTop.others))
                }
            } else if let devTops = result.deviceTops, !devTops.isEmpty {
                let localId = state.localDeviceId
                if !localId.isEmpty, let localTop = devTops[localId], !localTop.podium.isEmpty {
                    let name = state.deviceDisplayName(localId).uppercased()
                    let title = localTop.podium.first.map { "\(.name) kept you company most." } ?? "No apps yet."
                    list.append(.deviceTop(eyebrow: "YOUR \(name)'S TOP APPS", title: title, podium: localTop.podium, others: localTop.others))
                }
                for (devId, top) in devTops.sorted(by: { .key < .key }) {
                    if devId == localId || top.podium.isEmpty { continue }
                    let name = state.deviceDisplayName(devId).uppercased()
                    let title = top.podium.first.map { "\(.name) kept you company most." } ?? "No apps yet."
                    list.append(.deviceTop(eyebrow: "YOUR \(name)'S TOP APPS", title: title, podium: top.podium, others: top.others))
                }
            }
        }
        list.append(.games)
        list.append(.gacha)
        list.append(.socialMedia)
        list.append(.browsing)
        list.append(.coding)
        list.append(.productivity)
        list.append(.entertainment)
        list.append(.longestSessions)
        list.append(.habits)
        return list
    }

    public static func cardCount(state: AppState, result: WrappedResult?) -> Int {
        guard let result = result else { return 6 }
        return cards(state: state, result: result).count
    }

    @ObservedObject var state: AppState
    let result: WrappedResult
    let index: Int

    public static let cardCount = 6

    private func h(_ seconds: Int64) -> String { "\((seconds + 1800) / 3600)h" }
    private var yearText: String { String(result.year) }

    public var body: some View {
        ZStack(alignment: .topTrailing) {
            RoundedRectangle(cornerRadius: 16).fill(ThemeColors.surface)
            dots
            content
                .padding(.top, 20)
                .padding(.horizontal, 20)
                .padding(.bottom, index == 0 ? 18 : 16)
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        }
        .clipShape(RoundedRectangle(cornerRadius: 16))
        .overlay(RoundedRectangle(cornerRadius: 16).strokeBorder(ThemeColors.borderSubtle, lineWidth: 1))
    }

    private var dots: some View {
        Canvas { ctx, size in
            for x in stride(from: CGFloat(3.5), to: size.width, by: 7) {
                for y in stride(from: CGFloat(3.5), to: size.height, by: 7) {
                    ctx.fill(Path(ellipseIn: CGRect(x: x - 1.2, y: y - 1.2, width: 2.4, height: 2.4)), with: .color(Tokens.ramp[2].opacity(0.55)))
                }
            }
        }
        .frame(width: 200, height: 170)
        .overlay(LinearGradient(stops: [.init(color: .clear, location: 0.2), .init(color: ThemeColors.surface, location: 0.85)], startPoint: .topTrailing, endPoint: .bottomLeading))
    }

    @ViewBuilder
    private var content: some View {
        let allCards = Self.cards(state: state, result: result)
        let item = index < allCards.count ? allCards[index] : .habits
        switch item {
        case .total: totalCard
        case .topApps: podiumCard(eyebrow: "YOUR TOP APPS", title: result.podium.first.map { "\($0.name) kept you company most." } ?? "No apps yet.", podium: result.podium, others: result.others, emptyMessage: "No apps tracked yet.")
        case .deviceTop(let eyebrow, let title, let podium, let others): podiumCard(eyebrow: eyebrow, title: title, podium: podium, others: others, emptyMessage: "No apps tracked yet.")
        case .games: podiumCard(eyebrow: "YOUR TOP GAMES", title: result.gamesTop?.podium.first.map { "\($0.name) ate your evenings." } ?? "No games yet.", podium: result.gamesTop?.podium ?? [], others: result.gamesTop?.others ?? [], emptyMessage: "No games tracked yet. Mark one in Add app.")
        case .gacha: podiumCard(eyebrow: "YOUR TOP GACHA", title: result.gachaTop?.podium.first.map { "\($0.name) got your pulls." } ?? "No gacha games yet.", podium: result.gachaTop?.podium ?? [], others: result.gachaTop?.others ?? [], emptyMessage: "No gacha games tracked yet. Mark one in Add app.")
        case .socialMedia: podiumCard(eyebrow: "YOUR TOP SOCIAL MEDIA", title: result.socialMediaTop?.podium.first.map { "\($0.name) kept you connected." } ?? "No social media apps yet.", podium: result.socialMediaTop?.podium ?? [], others: result.socialMediaTop?.others ?? [], emptyMessage: "No social media apps tracked yet. Mark one in Add app.")
        case .browsing: podiumCard(eyebrow: "YOUR TOP BROWSING", title: result.browsingTop?.podium.first.map { "\($0.name) was your window to the web." } ?? "No browsing apps yet.", podium: result.browsingTop?.podium ?? [], others: result.browsingTop?.others ?? [], emptyMessage: "No browsing apps tracked yet. Mark one in Add app.")
        case .coding: podiumCard(eyebrow: "YOUR TOP CODING", title: result.codingTop?.podium.first.map { "\($0.name) saw your commits." } ?? "No coding apps yet.", podium: result.codingTop?.podium ?? [], others: result.codingTop?.others ?? [], emptyMessage: "No coding apps tracked yet. Mark one in Add app.")
        case .productivity: podiumCard(eyebrow: "YOUR TOP PRODUCTIVITY", title: result.productivityTop?.podium.first.map { "\($0.name) drove your focus." } ?? "No productivity apps yet.", podium: result.productivityTop?.podium ?? [], others: result.productivityTop?.others ?? [], emptyMessage: "No productivity apps tracked yet. Mark one in Add app.")
        case .entertainment: podiumCard(eyebrow: "YOUR TOP ENTERTAINMENT", title: result.entertainmentTop?.podium.first.map { "\($0.name) kept you entertained." } ?? "No entertainment apps yet.", podium: result.entertainmentTop?.podium ?? [], others: result.entertainmentTop?.others ?? [], emptyMessage: "No entertainment apps tracked yet. Mark one in Add app.")
        case .longestSessions: podiumCard(eyebrow: "YOUR LONGEST SESSIONS", title: result.longestSessions?.first.map { "\($0.name) held on the longest." } ?? "No sessions yet.", podium: result.longestSessions ?? [], others: [], emptyMessage: "No sessions tracked yet.")
        case .habits: habitsCard
        }
    }

    private func eyebrow(_ text: String) -> some View {
        Text(text).caps(10, tracking: 2, weight: .bold)
    }

    private var totalCard: some View {
        let split = state.deviceSplit(result.deviceTotals)
        let total = CGFloat(max(1, result.totalSeconds))

        let deviceName: String = {
            if state.deviceScope == .all {
                return "Your devices were on"
            }
            if let totals = result.deviceTotals, !totals.isEmpty {
                let hasLocal = totals.keys.contains { state.isLocalDevice($0) && (totals[$0] ?? 0) > 0 }
                let hasRemote = totals.keys.contains { !state.isLocalDevice($0) && (totals[$0] ?? 0) > 0 }
                if !hasLocal && hasRemote {
                    return "Your \(state.deviceBDisplayName()) was on"
                }
            }
            return "Your \(state.deviceADisplayName()) was on"
        }()

        var parts: [String] = []
        if result.equivalentDays > 0 {
            parts.append("\(result.equivalentDays) day\(result.equivalentDays == 1 ? "" : "s")")
        }
        if result.equivalentHours > 0 || parts.isEmpty {
            parts.append("\(result.equivalentHours) hour\(result.equivalentHours == 1 ? "" : "s")")
        }
        let highlight = parts.joined(separator: " and ")

        return VStack(alignment: .leading, spacing: 0) {
            eyebrow(state.yearOffset == 0 ? "YOUR \(yearText) SO FAR" : "YOUR \(yearText) IN REVIEW")

            HStack(alignment: .lastTextBaseline, spacing: 8) {
                Text(Formatters.grouped(result.totalHours))
                    .font(.system(size: 92, weight: .heavy))
                    .tracking(-4)
                    .foregroundColor(ThemeColors.accent)
                    .lineLimit(1)
                    .minimumScaleFactor(0.5)
                Text("hours")
                    .font(.system(size: 22, weight: .bold))
                    .foregroundColor(ThemeColors.textPrimary)
            }
            .frame(height: 84)
            .padding(.top, 22)

            (Text("\(deviceName) for ") + Text(highlight).foregroundColor(ThemeColors.accent) + Text(" this year."))
                .font(.system(size: 17, weight: .semibold))
                .lineSpacing(4)
                .foregroundColor(ThemeColors.textPrimary)
                .frame(maxWidth: 300, alignment: .leading)
                .padding(.top, 22)

            if state.showsDeviceSplit {
                GeometryReader { geo in
                    ZStack(alignment: .leading) {
                        Rectangle().fill(ThemeColors.barBackground)
                        Rectangle().fill(ThemeColors.deviceB.opacity(0.8))
                            .frame(width: geo.size.width * CGFloat(split.pc) / total)
                            .frame(maxWidth: .infinity, alignment: .trailing)
                        Rectangle().fill(ThemeColors.deviceA.opacity(0.9))
                            .frame(width: geo.size.width * CGFloat(split.mac) / total)
                            .blendMode(.normal)
                    }
                }
                .frame(width: 300, height: 8)
                .clipShape(Capsule())
                .padding(.top, 18)

                HStack {
                    HStack(spacing: 5) {
                        DeviceMarker(isLocal: true, size: 7)
                        Text("\(state.deviceADisplayName()) \(Formatters.hours(split.mac))")
                            .lineLimit(1)
                            .truncationMode(.tail)
                    }
                    Spacer(minLength: 4)
                    Text("\(Formatters.hours(result.onBothSeconds)) on both")
                        .lineLimit(1)
                        .fixedSize(horizontal: true, vertical: false)
                    Spacer(minLength: 4)
                    HStack(spacing: 5) {
                        RoundedRectangle(cornerRadius: 2).fill(ThemeColors.deviceB).frame(width: 7, height: 7)
                        Text("\(state.deviceBDisplayName()) \(Formatters.hours(split.pc))")
                            .lineLimit(1)
                            .truncationMode(.tail)
                    }
                }
                .font(.system(size: 10))
                .foregroundColor(ThemeColors.textMuted)
                .frame(width: 300)
                .padding(.top, 5)
            }

            Spacer(minLength: 8)

            HStack(spacing: 8) {
                statTile("\(result.daysTracked)", result.trackingStarted.map { "days tracked since \(Formatters.dayText($0, format: "d MMM"))" } ?? "days tracked")
                statTile("\(result.daysWithActivity)", "days with an app open")
                statTile("\(result.appsTracked)", "apps tracked")
            }
        }
    }

    private func statTile(_ value: String, _ label: String) -> some View {
        VStack(alignment: .leading, spacing: 0) {
            Text(value)
                .font(.system(size: 18, weight: .bold))
                .foregroundColor(ThemeColors.textPrimary)
            Text(label)
                .font(.system(size: 10))
                .lineSpacing(1)
                .foregroundColor(ThemeColors.textMuted)
                .fixedSize(horizontal: false, vertical: true)
                .frame(height: 26, alignment: .top)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.horizontal, 10)
        .padding(.vertical, 8)
        .overlay(RoundedRectangle(cornerRadius: 10).strokeBorder(ThemeColors.border, lineWidth: 1))
    }

    private func headline(_ text: String) -> some View {
        Text(text)
            .font(.system(size: 20, weight: .bold))
            .lineSpacing(2)
            .foregroundColor(ThemeColors.textPrimary)
            .frame(maxWidth: 310, alignment: .leading)
            .padding(.top, 8)
    }

    private func podiumCard(eyebrow label: String, title: String, podium: [WrappedApp], others: [WrappedApp], emptyMessage: String) -> some View {
        return VStack(alignment: .leading, spacing: 0) {
            eyebrow(label)
            headline(podium.isEmpty ? emptyMessage : title)
            Spacer(minLength: 8)
            if !podium.isEmpty {
                let order: [Int] = {
                    var ord = [0]
                    for i in 1..<podium.count {
                        if i % 2 == 1 { ord.insert(i, at: 0) }
                        else { ord.append(i) }
                    }
                    return ord
                }()

                HStack(alignment: .bottom, spacing: podium.count > 3 ? 6 : 8) {
                    ForEach(order, id: \.self) { idx in
                        let rank = idx + 1
                        slot(podium[idx], rank: rank, count: podium.count)
                    }
                }
                .frame(maxWidth: .infinity, alignment: .bottom)

                if !others.isEmpty {
                    (Text("Then ") + others.prefix(3).enumerated().reduce(Text("")) { acc, item in
                        acc + Text(item.offset > 0 ? " · " : "") + Text("\(item.element.name) \(h(item.element.seconds))").foregroundColor(ThemeColors.textPrimary)
                    })
                    .font(.system(size: 10))
                    .foregroundColor(ThemeColors.textMuted)
                    .lineLimit(1)
                    .padding(.top, 12)
                }
            } else {
                Spacer()
            }
        }
    }

    @ViewBuilder
    private func slot(_ app: WrappedApp, rank: Int, count: Int) -> some View {
        let isDense = count > 3
        let iconSize: CGFloat = isDense ? 28 : 34
        let iconRadius: CGFloat = isDense ? 7 : 9
        let nameFont: CGFloat = isDense ? 10.5 : 12
        let rankFont: CGFloat = {
            switch rank {
            case 1: return isDense ? 26 : 30
            case 2: return isDense ? 22 : 26
            case 3: return isDense ? 20 : 24
            default: return 18
            }
        }()
        let pillarH: CGFloat = {
            switch rank {
            case 1: return 108
            case 2: return 84
            case 3: return 68
            case 4: return 54
            default: return 42
            }
        }()
        let inset: CGFloat = {
            switch rank {
            case 1: return 12
            case 2: return 10
            default: return 8
            }
        }()

        VStack(spacing: 0) {
            AppTile(name: app.name, png: state.iconCache.getCachedIcon(forAppId: app.appId), size: iconSize, radius: iconRadius, fontSize: isDense ? 13 : 15, border: ThemeColors.border)
            Text(app.name)
                .font(.system(size: nameFont, weight: .bold))
                .foregroundColor(ThemeColors.textPrimary)
                .lineLimit(1)
                .padding(.top, 6)
            Text(h(app.seconds))
                .font(.system(size: 10))
                .foregroundColor(ThemeColors.textDim)
                .padding(.bottom, 6)
            Text("\(rank)")
                .font(.system(size: rankFont, weight: .heavy))
                .foregroundColor(ThemeColors.background)
                .padding(.top, inset)
                .frame(maxWidth: .infinity, alignment: .top)
                .frame(height: pillarH, alignment: .top)
                .background(UnevenRoundedRectangle(topLeadingRadius: 8, topTrailingRadius: 8).fill(Tokens.appColor(app.name)))
        }
        .frame(maxWidth: .infinity)
    }

    private var streakSpan: String {
        guard result.longestStreakDays > 0, let start = result.streakStart else { return "" }
        if result.longestStreakDays == 1 {
            return Formatters.dayText(start, format: "d MMM")
        }
        if let end = result.streakEnd {
            return "\(Formatters.dayText(start, format: "d MMM")) to \(Formatters.dayText(end, format: "d MMM"))"
        }
        return Formatters.dayText(start, format: "d MMM")
    }

    private var busiestDelta: String {
        guard let m = result.busiestMonth,
              let month = state.yearResult?.months.first(where: { $0.month == m }),
              let prev = month.previousSeconds, prev > 0 else { return "your biggest month" }
        let pct = Int((Double(month.seconds - prev) / Double(prev) * 100).rounded())
        return "\(pct >= 0 ? "+" : "")\(pct)% vs \(Formatters.monthName(m == 1 ? 12 : m - 1, short: true))"
    }

    private var peakText: String {
        guard let hour = result.peakHour else { return "—" }
        func label(_ h: Int) -> (String, String) {
            let v = h % 12 == 0 ? 12 : h % 12
            return ("\(v)", h % 24 < 12 ? "am" : "pm")
        }
        let a = label(hour), b = label(hour + 1)
        return a.1 == b.1 ? "\(a.0)–\(b.0) \(b.1)" : "\(a.0) \(a.1)–\(b.0) \(b.1)"
    }

    private var habitsCard: some View {
        VStack(alignment: .leading, spacing: 0) {
            eyebrow("YOUR HABITS")
            headline("Your rhythm this year.")
            VStack(spacing: 10) {
                HStack(spacing: 10) {
                    habit("LONGEST STREAK", "\(result.longestStreakDays) day\(result.longestStreakDays == 1 ? "" : "s")", streakSpan)
                    habit("BUSIEST MONTH", result.busiestMonth.map { "\(Formatters.monthName($0, short: true)) · \(h(result.busiestMonthSeconds))" } ?? "—", busiestDelta)
                }
                HStack(spacing: 10) {
                    habit("PEAK HOUR", peakText, "Busiest Time")
                    habit("BEFORE NOON", "\(result.beforeNoonPercent)%", result.beforeNoonPercent >= 50 ? "of your time, an early bird" : "of your time")
                }
            }
            .padding(.top, 16)
        }
    }

    private func habit(_ title: String, _ value: String, _ note: String) -> some View {
        VStack(alignment: .leading, spacing: 0) {
            Text(title).caps(9, tracking: 1.4, weight: .bold)
            Spacer(minLength: 4)
            Text(value)
                .font(.system(size: 26, weight: .heavy))
                .foregroundColor(ThemeColors.accent)
                .minimumScaleFactor(0.7)
            Text(note)
                .font(.system(size: 10))
                .foregroundColor(ThemeColors.textDim)
                .lineLimit(1)
                .padding(.top, 4)
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 12)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
        .overlay(RoundedRectangle(cornerRadius: 12).strokeBorder(ThemeColors.border, lineWidth: 1))
    }
}

private struct OpenDashedBorder: View {
    let color: Color
    let radius: CGFloat

    var body: some View {
        Canvas { ctx, size in
            var path = Path()
            path.move(to: CGPoint(x: 0, y: size.height))
            path.addLine(to: CGPoint(x: 0, y: radius))
            if radius > 0 {
                path.addQuadCurve(to: CGPoint(x: radius, y: 0), control: CGPoint(x: 0, y: 0))
            }
            path.addLine(to: CGPoint(x: size.width - radius, y: 0))
            if radius > 0 {
                path.addQuadCurve(to: CGPoint(x: size.width, y: radius), control: CGPoint(x: size.width, y: 0))
            }
            path.addLine(to: CGPoint(x: size.width, y: size.height))
            ctx.stroke(path, with: .color(color), style: StrokeStyle(lineWidth: 1, dash: [3, 2]))
        }
    }
}


private struct PaceAppRowItem: View {
    @ObservedObject var state: AppState
    let app: YearAppShare
    let maxSecs: CGFloat
    let year: Int
    let month: Int
    @State private var isHovered = false

    var body: some View {
        HStack(spacing: 8) {
            if let img = state.iconCache.getCachedIcon(forAppId: app.appId),
               let nsImage = NSImage(data: img) {
                Image(nsImage: nsImage)
                    .resizable()
                    .frame(width: 16, height: 16)
                    .cornerRadius(3)
            } else {
                RoundedRectangle(cornerRadius: 2)
                    .fill(Tokens.appColor(app.name))
                    .frame(width: 10, height: 10)
                    .padding(3)
            }

            Text(app.name)
                .font(.system(size: 12, weight: .semibold))
                .foregroundColor(ThemeColors.textPrimary)
                .lineLimit(1)
                .truncationMode(.tail)
                .frame(width: 86, alignment: .leading)

            GeometryReader { geo in
                ZStack(alignment: .leading) {
                    RoundedRectangle(cornerRadius: 2.5)
                        .fill(ThemeColors.border)
                        .frame(height: 5)
                    RoundedRectangle(cornerRadius: 2.5)
                        .fill(Tokens.appColor(app.name))
                        .frame(width: max(2, geo.size.width * CGFloat(app.seconds) / maxSecs), height: 5)
                }
                .frame(maxHeight: .infinity, alignment: .center)
            }
            .frame(height: 12)

            Text("\(Formatters.decimalHours(app.seconds)) h")
                .font(.system(size: 11.5, weight: .regular))
                .foregroundColor(ThemeColors.textPrimary)
                .frame(width: 48, alignment: .trailing)
        }
        .padding(.horizontal, 4)
        .frame(height: 22)
        .background(RoundedRectangle(cornerRadius: 4).fill(isHovered ? ThemeColors.surfaceActive : Color.clear))
        .contentShape(Rectangle())
        .onHover { h in isHovered = h }
        .onTapGesture(count: 2) {
            state.openAppDetail(appId: app.appId, range: .year, anchor: String(format: "%04d-%02d-01", year, month), year: year)
        }
    }
}

private struct MatrixRowItem: View {
    @ObservedObject var state: AppState
    let row: MatrixRow
    let selected: Bool
    let year: Int
    let onSelect: () -> Void
    @State private var isHovered = false

    var body: some View {
        HStack(spacing: 3) {
            Text(row.name)
                .font(.system(size: 11, weight: selected ? .bold : .medium))
                .foregroundColor(selected ? ThemeColors.accent : ThemeColors.textPrimary)
                .lineLimit(1)
                .frame(width: 78, alignment: .leading)

            ForEach(Array(row.cells.enumerated()), id: \.offset) { _, cell in
                let hours = (cell.seconds + 1800) / 3600
                Text(hours > 0 ? "\(hours)" : "")
                    .font(.system(size: 10, weight: .semibold))
                    .foregroundColor(cell.level >= 3 ? ThemeColors.background : ThemeColors.textDim)
                    .frame(maxWidth: .infinity)
                    .frame(height: 26)
                    .background(RoundedRectangle(cornerRadius: 4).fill(Tokens.heat(cell.level)))
            }

            Text(Formatters.grouped((row.totalSeconds + 1800) / 3600))
                .font(.system(size: 11, weight: .semibold))
                .foregroundColor(ThemeColors.textPrimary)
                .frame(width: 40, alignment: .trailing)
        }
        .padding(.horizontal, 4)
        .background(RoundedRectangle(cornerRadius: 6).fill(isHovered ? ThemeColors.surfaceActive : Color.clear))
        .contentShape(Rectangle())
        .onHover { h in isHovered = h }
        .onTapGesture(count: 2) {
            state.openAppDetail(appId: row.appId, range: .matrix, anchor: "\(year)-01-01", year: year)
        }
        .simultaneousGesture(TapGesture().onEnded {
            onSelect()
        })
    }
}
