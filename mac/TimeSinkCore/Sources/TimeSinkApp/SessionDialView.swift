import SwiftUI
import TimeSinkCore

private struct RingSegment: Identifiable {
    let index: Int
    let start: CGFloat
    let end: CGFloat
    let tail: CGFloat?

    var id: Int { index }
}

/// One ring of the 24-hour dial, 12 am at the top.
private struct DialRing: View {
    let diameter: CGFloat
    let width: CGFloat
    let segments: [RingSegment]
    let color: Color
    let activeIndex: Int?

    var body: some View {
        ZStack {
            Circle().stroke(Tokens.track, lineWidth: width)
            ForEach(segments) { seg in
                let isDim = activeIndex != nil && activeIndex != seg.index
                if let tail = seg.tail {
                    Circle()
                        .trim(from: tail, to: 1.0)
                        .stroke(color, style: StrokeStyle(lineWidth: width, lineCap: .butt, dash: [1, 3]))
                        .rotationEffect(.degrees(-90))
                        .opacity(isDim ? 0.35 : 0.75)
                }
                Circle()
                    .trim(from: seg.start, to: seg.end)
                    .stroke(color, style: StrokeStyle(lineWidth: width, lineCap: .butt))
                    .rotationEffect(.degrees(-90))
                    .opacity(isDim ? 0.35 : 1.0)
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
    @Binding public var activeSessionIndex: Int?
    public var onOpenSettings: (() -> Void)? = nil
    public var onOpenDetail: (() -> Void)? = nil

    @State private var hoveredSessionIndex: Int? = nil
    @State private var pinnedSessionIndex: Int? = nil

    public init(
        appRow: TodayAppRow,
        dayStartUtc: Int64,
        dayEndUtc: Int64,
        nowUtc: Int64,
        isAllScope: Bool,
        localDeviceId: String = "",
        activeSessionIndex: Binding<Int?>? = nil,
        onOpenSettings: (() -> Void)? = nil,
        onOpenDetail: (() -> Void)? = nil
    ) {
        self.appRow = appRow
        self.dayStartUtc = dayStartUtc
        self.dayEndUtc = dayEndUtc
        self.nowUtc = nowUtc
        self.isAllScope = isAllScope
        self.localDeviceId = localDeviceId
        self._activeSessionIndex = activeSessionIndex ?? .constant(nil)
        self.onOpenSettings = onOpenSettings
        self.onOpenDetail = onOpenDetail
    }

    private var detail: RowDetail { RowDetails.of(appRow) }
    private var shownSessions: [TodaySession] { detail.shown }
    private var appColor: Color { Tokens.appColor(appRow.name) }
    private var dayLength: CGFloat { CGFloat(max(1, dayEndUtc - dayStartUtc)) }
    private var nowFraction: CGFloat { min(1, CGFloat(nowUtc - dayStartUtc) / dayLength) }
    private var average: Double { appRow.sevenDayAverageSeconds ?? 0 }
    private var longest: Int64 { appRow.sessions.map(\.seconds).max() ?? 0 }

    private var effectiveActive: Int? {
        hoveredSessionIndex ?? pinnedSessionIndex
    }

    private func isLocal(_ deviceId: String) -> Bool {
        deviceId.isEmpty || localDeviceId.isEmpty || deviceId.caseInsensitiveCompare(localDeviceId) == .orderedSame
    }

    private func dayFractionBefore(_ utc: Int64) -> Double {
        let length = Double(max(1, dayEndUtc - dayStartUtc))
        return min(max(1.0 + Double(utc - dayStartUtc) / length, 0), 1)
    }

    private func makeSegments(_ sessions: [(Int, TodaySession)]) -> [RingSegment] {
        sessions.map { idx, s in
            let sF = CGFloat(max(0, s.startUtc - dayStartUtc)) / dayLength
            let eF = min(1, CGFloat(s.endUtc - dayStartUtc) / dayLength)
            let tail = s.carriesOver ? CGFloat(dayFractionBefore(s.fullStartUtc)) : nil
            return RingSegment(index: idx, start: sF, end: max(eF, sF + 0.001), tail: tail)
        }
    }

    private func segmentAt(location: CGPoint, size: CGFloat) -> Int? {
        guard size > 0 else { return nil }
        let cx = size / 2
        let cy = size / 2
        let dx = location.x - cx
        let dy = location.y - cy
        let r = sqrt(dx * dx + dy * dy)
        var f = atan2(dx, -dy) / (2 * .pi)
        if f < 0 { f += 1 }
        let slop: CGFloat = 0.008

        let shown = shownSessions
        if isAllScope {
            let macR: CGFloat = (156 - 10) / 2 // 73
            let pcR: CGFloat = (130 - 10) / 2  // 60
            if abs(r - macR) <= 9 {
                for (idx, s) in shown.enumerated() where isLocal(s.deviceId) {
                    let sF = CGFloat(max(0, s.startUtc - dayStartUtc)) / dayLength
                    let eF = min(1, CGFloat(s.endUtc - dayStartUtc) / dayLength)
                    if f >= sF - slop && f <= max(eF, sF + 2 * slop) + slop { return idx }
                    if s.carriesOver {
                        let tail = CGFloat(dayFractionBefore(s.fullStartUtc))
                        if f >= tail - slop { return idx }
                    }
                }
            }
            if abs(r - pcR) <= 9 {
                for (idx, s) in shown.enumerated() where !isLocal(s.deviceId) {
                    let sF = CGFloat(max(0, s.startUtc - dayStartUtc)) / dayLength
                    let eF = min(1, CGFloat(s.endUtc - dayStartUtc) / dayLength)
                    if f >= sF - slop && f <= max(eF, sF + 2 * slop) + slop { return idx }
                    if s.carriesOver {
                        let tail = CGFloat(dayFractionBefore(s.fullStartUtc))
                        if f >= tail - slop { return idx }
                    }
                }
            }
            return nil
        } else {
            let ringR: CGFloat = (150 - 11) / 2 // 69.5
            if abs(r - ringR) <= 10 {
                for (idx, s) in shown.enumerated() {
                    let sF = CGFloat(max(0, s.startUtc - dayStartUtc)) / dayLength
                    let eF = min(1, CGFloat(s.endUtc - dayStartUtc) / dayLength)
                    if f >= sF - slop && f <= max(eF, sF + 2 * slop) + slop { return idx }
                    if s.carriesOver {
                        let tail = CGFloat(dayFractionBefore(s.fullStartUtc))
                        if f >= tail - slop { return idx }
                    }
                }
            }
            return nil
        }
    }

    private var percentText: String {
        guard average > 0 else { return "—" }
        let pct = Int(((Double(appRow.seconds) - average) / average * 100).rounded())
        return "\(pct >= 0 ? "+" : "")\(pct)%"
    }

    public var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(alignment: .top, spacing: 14) {
                HStack(alignment: .center, spacing: 14) {
                if isAllScope { dualDial } else { singleDial }
                if isAllScope { dualStats } else { singleStats }
                }
                Spacer(minLength: 0)
                actionPill
            }
            sessionChips
        }
        .padding(.top, 4)
        .padding(.horizontal, 4)
        .onChange(of: effectiveActive) { _, newActive in
            activeSessionIndex = newActive
        }
    }

    private var actionPill: some View {
        HStack(spacing: 0) {
            PillHalfButton(help: "Settings") {
                onOpenSettings?()
            } content: {
                IconView(.sliders, size: 14, weight: 1.4, color: ThemeColors.textDim)
            }

            PillHalfButton(help: "App detail") {
                onOpenDetail?()
            } content: {
                IconView(.chartBars, size: 14, weight: 1.6, color: ThemeColors.textDim)
            }
        }
        .frame(width: 60, height: 28)
        .background(
            Capsule()
                .fill(ThemeColors.surfaceRaised)
                .overlay(Capsule().strokeBorder(ThemeColors.borderSubtle, lineWidth: 1))
        )
        .clipShape(Capsule())
    }

    private struct PillHalfButton<Content: View>: View {
        let help: String
        let action: () -> Void
        @ViewBuilder let content: () -> Content
        @State private var hovered = false

        var body: some View {
            Button(action: action) {
                content()
                    .foregroundColor(hovered ? ThemeColors.accent : ThemeColors.textDim)
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                    .background(hovered ? ThemeColors.surface : Color.clear)
                    .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .onHover { hovered = $0 }
            .help(help)
        }
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
        let indexed = Array(shownSessions.enumerated())
        let segs = makeSegments(indexed)
        let hasCarried = shownSessions.contains(where: \.carriesOver)
        return ZStack {
            DialRing(diameter: 150, width: 11, segments: segs, color: appColor, activeIndex: effectiveActive)
            DialRing(diameter: 116, width: 11, segments: [RingSegment(index: -1, start: 0, end: totalFraction, tail: nil)], color: appColor.opacity(0.32), activeIndex: nil)
            if average > 0 { tick(150, top: 12, length: 22) }
            if hasCarried {
                Rectangle()
                    .fill(ThemeColors.textPrimary)
                    .frame(width: 1.5, height: 12)
                    .offset(y: -(75 - 6))
            }
            nowDot(150, inset: 6)
            center(20)
            Text("12 am")
                .font(.system(size: 9))
                .foregroundColor(ThemeColors.textMuted)
                .offset(y: -82)
        }
        .frame(width: 150, height: 150)
        .padding(.top, 6)
        .contentShape(Circle())
        .onContinuousHover { phase in
            switch phase {
            case .active(let loc):
                hoveredSessionIndex = segmentAt(location: loc, size: 150)
            case .ended:
                hoveredSessionIndex = nil
            }
        }
        .onTapGesture { loc in
            if let hit = segmentAt(location: loc, size: 150) {
                withAnimation(.easeInOut(duration: 0.15)) {
                    pinnedSessionIndex = (pinnedSessionIndex == hit ? nil : hit)
                }
            }
        }
    }

    private var dualDial: some View {
        let indexed = Array(shownSessions.enumerated())
        let macSegs = makeSegments(indexed.filter { isLocal($0.1.deviceId) })
        let pcSegs = makeSegments(indexed.filter { !isLocal($0.1.deviceId) })
        let hasCarried = shownSessions.contains(where: \.carriesOver)
        return ZStack {
            DialRing(diameter: 156, width: 10, segments: macSegs, color: ThemeColors.deviceA, activeIndex: effectiveActive)
            DialRing(diameter: 130, width: 10, segments: pcSegs, color: ThemeColors.deviceB, activeIndex: effectiveActive)
            DialRing(diameter: 104, width: 10, segments: [RingSegment(index: -1, start: 0, end: totalFraction, tail: nil)], color: appColor.opacity(0.32), activeIndex: nil)
            if average > 0 { tick(156, top: 24, length: 16) }
            if hasCarried {
                Rectangle()
                    .fill(ThemeColors.textPrimary)
                    .frame(width: 1.5, height: 12)
                    .offset(y: -(78 - 6))
            }
            nowDot(156, inset: 5)
            center(18)
        }
        .frame(width: 156, height: 156)
        .contentShape(Circle())
        .onContinuousHover { phase in
            switch phase {
            case .active(let loc):
                hoveredSessionIndex = segmentAt(location: loc, size: 156)
            case .ended:
                hoveredSessionIndex = nil
            }
        }
        .onTapGesture { loc in
            if let hit = segmentAt(location: loc, size: 156) {
                withAnimation(.easeInOut(duration: 0.15)) {
                    pinnedSessionIndex = (pinnedSessionIndex == hit ? nil : hit)
                }
            }
        }
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
        VStack(alignment: .leading, spacing: 4) {
            FlowLayout(spacing: 6, lineSpacing: 6) {
                ForEach(Array(shownSessions.enumerated()), id: \.offset) { i, s in
                    let isActive = effectiveActive == i
                    let isDim = effectiveActive != nil && !isActive
                    let startClock = Formatters.clock12(s.carriesOver ? s.fullStartUtc : s.startUtc)
                    let endClock = s.isOpen ? "now" : Formatters.clock12(s.endUtc)
                    let dur = Formatters.paddedHM(s.fullSeconds)

                    Button(action: {
                        withAnimation(.easeInOut(duration: 0.15)) {
                            pinnedSessionIndex = (pinnedSessionIndex == i ? nil : i)
                        }
                    }) {
                        HStack(spacing: 5) {
                            if isAllScope {
                                if isLocal(s.deviceId) {
                                    Circle().fill(ThemeColors.deviceA).frame(width: 6, height: 6)
                                } else {
                                    RoundedRectangle(cornerRadius: 1.5).fill(ThemeColors.deviceB).frame(width: 6, height: 6)
                                }
                            }
                            if s.carriesOver {
                                Image(systemName: "moon.fill")
                                    .font(.system(size: 8))
                                    .foregroundColor(appColor)
                            }
                            Text("\(startClock) → \(endClock) · \(dur)")
                                .font(.system(size: 10, weight: s.isOpen ? .semibold : .regular))
                                .foregroundColor(s.isOpen ? ThemeColors.accent : (isActive ? ThemeColors.textPrimary : ThemeColors.textDim))
                        }
                        .padding(.horizontal, 8)
                        .padding(.vertical, 3.5)
                        .background(RoundedRectangle(cornerRadius: 10).fill(ThemeColors.surface))
                        .overlay(
                            RoundedRectangle(cornerRadius: 10)
                                .strokeBorder(isActive || s.isOpen ? ThemeColors.accent : ThemeColors.border, lineWidth: 1)
                        )
                        .opacity(isDim ? 0.45 : 1.0)
                        .contentShape(RoundedRectangle(cornerRadius: 10))
                    }
                    .buttonStyle(.plain)
                    .onHover { isH in
                        if isH {
                            hoveredSessionIndex = i
                        } else if hoveredSessionIndex == i {
                            hoveredSessionIndex = nil
                        }
                    }
                }
            }

            if detail.blips > 0 {
                Text("Blips under 1m hidden: \(detail.blips)")
                    .font(.system(size: 10))
                    .foregroundColor(ThemeColors.textMuted)
                    .padding(.top, 2)
            }
        }
    }
}
