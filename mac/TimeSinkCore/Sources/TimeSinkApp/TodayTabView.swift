import SwiftUI
import TimeSinkCore

public struct TodayTabView: View {
    @ObservedObject var state: AppState

    @State private var selectedHour: Int? = nil
    @State private var hoveredSessions: [Int64: Int] = [:]
    @State private var pinnedSessions: [Int64: Int] = [:]
    @State private var stripTip: StripTip? = nil

    private struct StripTip: Equatable {
        let appId: Int64
        let text: String
        let x: CGFloat
    }

    public init(state: AppState) {
        self.state = state
    }

    private var isLive: Bool { state.daysBack == 0 }
    private var effectiveNowUtc: Int64 {
        isLive ? state.liveNowUtc : (state.todayResult?.nowUtc ?? Int64(Date().timeIntervalSince1970))
    }

    private var formattedDateString: String {
        let df = DateFormatter()
        df.dateFormat = "EEEE, d MMM"
        let date = Calendar.current.date(byAdding: .day, value: -state.daysBack, to: Date()) ?? Date()
        return df.string(from: date)
    }

    public var body: some View {
        ScrollView(.vertical, showsIndicators: false) {
            VStack(alignment: .leading, spacing: 8) {
                if let result = state.todayResult {
                    headline(result)
                    if state.showsDeviceSplit { deviceRow(result) }
                    HourlyChartView(state: state, result: result, selectedHour: $selectedHour)
                    if !state.showsDeviceSplit { tiles(result) }
                    VStack(spacing: 0) {
                        ForEach(result.rows, id: \.appId) { row in
                            appRow(row, result: result)
                        }
                    }
                } else {
                    ProgressView().frame(maxWidth: .infinity, minHeight: 200)
                }
            }
            .padding(.horizontal, 16)
            .padding(.top, 10)
        }
    }

    private func headline(_ result: TodayResult) -> some View {
        let uptime = result.uptimeAt(effectiveNowUtc, live: isLive)
        return HStack(alignment: .bottom) {
            VStack(alignment: .leading, spacing: 0) {
                HStack(spacing: 10) {
                    PeriodArrow(left: true, enabled: result.canPrev, iconSize: 8) {
                        state.previousDay()
                    }
                    Text(formattedDateString)
                        .font(.system(size: 11))
                        .foregroundColor(ThemeColors.textMuted)
                    PeriodArrow(left: false, enabled: state.daysBack > 0, iconSize: 8) {
                        state.nextDay()
                    }
                }

                HStack(alignment: .firstTextBaseline, spacing: 3) {
                    Text(Formatters.paddedHM(uptime))
                        .font(.system(size: 34, weight: .bold))
                        .tracking(-0.8)
                        .foregroundColor(ThemeColors.accent)
                    Text("\(uptime % 60)s")
                        .font(.system(size: 14, weight: .medium))
                        .foregroundColor(ThemeColors.textMuted)
                }
                .frame(height: 40)
            }

            Spacer()

            if result.runningCount > 0 {
                HStack(spacing: 6) {
                    Circle()
                        .fill(ThemeColors.accent)
                        .frame(width: 7, height: 7)
                        .background(Circle().fill(ThemeColors.greenLow).frame(width: 13, height: 13))
                    Text(state.deviceScope == .all ? "\(result.runningCount) running · \(state.deviceADisplayName())" : "\(result.runningCount) running")
                        .font(.system(size: 11, weight: .semibold))
                        .foregroundColor(ThemeColors.textPrimary)
                }
                .padding(.horizontal, 10)
                .frame(height: state.deviceScope == .all ? 22 : 24)
                .card(radius: 12)
                .padding(.bottom, state.deviceScope == .all ? 4 : 6)
            }
        }
    }

    private func deviceRow(_ result: TodayResult) -> some View {
        let elapsed = isLive ? result.elapsedAt(effectiveNowUtc) : 0
        let uptimes = result.deviceUptimes ?? [:]
        let local = uptimes.first { state.isLocalDevice($0.key) }?.value
        let remote = uptimes.first { !state.isLocalDevice($0.key) }
        let localName = state.deviceADisplayName()
        let remoteName = state.deviceDisplayName(remote?.key)
        let localUptime = (local?.uptimeSeconds ?? 0) + elapsed
        var remoteText = "\(remoteName) \(Formatters.paddedHM(remote?.value.uptimeSeconds ?? 0))"
        if let pc = remote?.value, !pc.isLive, let last = pc.lastActiveUtc {
            remoteText += " · off \(Formatters.formatClock(utcSeconds: last))"
        }
        return DeviceLegend(
            mac: "\(localName) \(Formatters.paddedHM(localUptime))",
            pc: remoteText,
            both: "\(Formatters.paddedHM(result.onBothSeconds)) on both"
        )
    }

    private func tiles(_ result: TodayResult) -> some View {
        let delta = result.uptimeVsYesterdaySeconds
        var longest = "—"
        if let l = result.longestSession {
            longest = "\(Formatters.paddedHM(l.seconds)) · \(l.appName)"
        }
        return HStack(spacing: 8) {
            Button(action: {
                if let first = result.firstApp {
                    withAnimation(.easeInOut(duration: 0.18)) {
                        state.todayExpandedAppId = first.appId
                    }
                }
            }) {
                tile("FIRST APP", result.firstApp.map { Formatters.clock12($0.startUtc) } ?? "—", ThemeColors.textPrimary)
            }
            .buttonStyle(.plain)

            Button(action: {
                if let l = result.longestSession {
                    withAnimation(.easeInOut(duration: 0.18)) {
                        state.todayExpandedAppId = l.appId
                    }
                }
            }) {
                tile("LONGEST", longest, ThemeColors.textPrimary)
            }
            .buttonStyle(.plain)

            Button(action: {
                if result.canPrev {
                    state.previousDay()
                }
            }) {
                tile("VS YESTERDAY", "\(delta >= 0 ? "+" : "-")\(Formatters.paddedHM(abs(delta))) by now", delta >= 0 ? ThemeColors.accent : ThemeColors.warning)
            }
            .buttonStyle(.plain)
            .disabled(!result.canPrev)
        }
    }

    private func tile(_ title: String, _ value: String, _ color: Color) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(title).caps(9)
            Text(value)
                .font(.system(size: 14, weight: .bold))
                .foregroundColor(color)
                .lineLimit(1)
                .truncationMode(.tail)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.horizontal, 10)
        .padding(.vertical, 7)
        .overlay(RoundedRectangle(cornerRadius: 10).strokeBorder(ThemeColors.border, lineWidth: 1))
    }

    private func remoteRunningSession(_ row: TodayAppRow) -> TodaySession? {
        guard state.deviceScope == .all else { return nil }
        return row.sessions.first { $0.isOpen && !state.isLocalDevice($0.deviceId) }
    }

    private func subtitle(_ row: TodayAppRow, result: TodayResult) -> String {
        if row.isRunning, let since = row.runningSinceUtc {
            let devName = state.deviceADisplayName()
            let where_ = state.deviceScope == .all ? "Running on \(devName)" : "Running"
            return "\(where_) · \(Formatters.paddedHM(max(0, effectiveNowUtc - since))) session"
        }
        if let remote = remoteRunningSession(row) {
            let devName = state.deviceBDisplayName()
            return "Running on \(devName) · \(Formatters.paddedHM(max(0, effectiveNowUtc - remote.startUtc))) session"
        }
        if let closed = row.lastClosedUtc {
            return "Closed \(Formatters.clock12(closed))"
        }
        return "\(row.sessions.count) session\(row.sessions.count == 1 ? "" : "s")"
    }

    private func isExpanded(_ row: TodayAppRow, result: TodayResult) -> Bool {
        state.todayExpandedAppId == row.appId
    }

    @ViewBuilder
    private func appRow(_ row: TodayAppRow, result: TodayResult) -> some View {
        let expanded = isExpanded(row, result: result)
        let activeIdx = hoveredSessions[row.appId] ?? pinnedSessions[row.appId]
        let isMultiDev = state.showsDeviceSplit && (row.deviceSeconds?.count ?? 0) > 1
        let hoverBinding = Binding<Int?>(
            get: { hoveredSessions[row.appId] },
            set: { hoveredSessions[row.appId] = $0 }
        )
        let pinBinding = Binding<Int?>(
            get: { pinnedSessions[row.appId] },
            set: { pinnedSessions[row.appId] = $0 }
        )
        let toggleRow = {
            withAnimation(.easeInOut(duration: 0.18)) {
                state.todayExpandedAppId = expanded ? nil : row.appId
            }
        }

        VStack(spacing: 0) {
            do {
                HStack(spacing: 12) {
                    AppTile(name: row.name, png: state.iconCache.getCachedIcon(forAppId: row.appId))
                    VStack(alignment: .leading, spacing: 0) {
                        HStack {
                            Text(row.name)
                            Spacer()
                            Text(Formatters.paddedHM(result.rowSecondsAt(row, nowUtc: effectiveNowUtc)))
                        }
                        .font(.system(size: 13, weight: .bold))
                        .foregroundColor(ThemeColors.textPrimary)

                        Text(subtitle(row, result: result))
                            .font(.system(size: 10))
                            .foregroundColor((row.isRunning || remoteRunningSession(row) != nil) ? ThemeColors.accent : ThemeColors.textMuted)

                        timelines(row, result: result, expanded: expanded, activeSession: activeIdx, toggleRow: toggleRow).padding(.top, 4)
                    }
                }
                .frame(height: isMultiDev ? 54 : 50)
                .contentShape(Rectangle())
                .onTapGesture { toggleRow() }
            }

            if expanded {
                SessionDialView(
                    appRow: row,
                    dayStartUtc: result.dayStartUtc,
                    dayEndUtc: result.dayEndUtc,
                    nowUtc: effectiveNowUtc,
                    isAllScope: state.showsDeviceSplit,
                    localDeviceId: state.localDeviceId,
                    hoveredSessionIndex: hoverBinding,
                    pinnedSessionIndex: pinBinding,
                    onOpenSettings: {
                        state.todayEditingAppId = row.appId
                    },
                    onOpenDetail: {
                        state.openAppDetail(appId: row.appId)
                    }
                )
                .padding(.bottom, 10)
            }
        }
        .padding(.horizontal, expanded || state.showsDeviceSplit ? 8 : 0)
        .background(RoundedRectangle(cornerRadius: 10).fill(expanded ? ThemeColors.surfaceActive : .clear))
    }

    @ViewBuilder
    private func timelines(_ row: TodayAppRow, result: TodayResult, expanded: Bool, activeSession: Int?, toggleRow: @escaping () -> Void) -> some View {
        let shown = RowDetails.shownSessions(row)
        let isMultiDev = state.showsDeviceSplit && (row.deviceSeconds?.count ?? 0) > 1
        if isMultiDev {
            VStack(spacing: 2) {
                strip(row.sessions.filter { state.isLocalDevice($0.deviceId) }, allShown: shown, result: result, color: ThemeColors.deviceA, height: 3, activeSession: activeSession, row: row, toggleRow: toggleRow)
                strip(row.sessions.filter { !state.isLocalDevice($0.deviceId) }, allShown: shown, result: result, color: ThemeColors.deviceB, height: 3, activeSession: activeSession, row: row, toggleRow: toggleRow)
            }
        } else {
            strip(row.sessions, allShown: shown, result: result, color: Tokens.appColor(row.name), height: 4, activeSession: activeSession, row: row, toggleRow: toggleRow)
        }
    }

    private func tipText(_ s: TodaySession) -> String {
        let end = s.isOpen ? "now" : Formatters.clock12(s.endUtc)
        return "\(Formatters.clock12(s.fullStartUtc)) – \(end) · \(Formatters.paddedHM(s.fullSeconds))"
    }

    /// Segment under x (with a few points of slop, as on Windows), nearest wins.
    private func segmentIndex(_ sessions: [TodaySession], at x: CGFloat, width: CGFloat, height: CGFloat, result: TodayResult) -> Int? {
        let day = CGFloat(max(1, result.dayEndUtc - result.dayStartUtc))
        var best: Int? = nil
        var bestGap = CGFloat.greatestFiniteMagnitude
        for (i, s) in sessions.enumerated() where s.endUtc > s.startUtc {
            let x0 = min(max(CGFloat(s.startUtc - result.dayStartUtc) / day, 0), 1) * width
            let x1 = max(x0 + height, min(max(CGFloat(s.endUtc - result.dayStartUtc) / day, 0), 1) * width)
            let gap = x < x0 ? x0 - x : (x > x1 ? x - x1 : 0)
            if gap <= 4 && gap < bestGap { best = i; bestGap = gap }
        }
        return best
    }

    private func strip(
        _ sessions: [TodaySession],
        allShown: [TodaySession],
        result: TodayResult,
        color: Color,
        height: CGFloat,
        activeSession: Int?,
        row: TodayAppRow,
        toggleRow: @escaping () -> Void
    ) -> some View {
        GeometryReader { geo in
            let day = CGFloat(max(1, result.dayEndUtc - result.dayStartUtc))
            ZStack(alignment: .leading) {
                RoundedRectangle(cornerRadius: 2).fill(ThemeColors.barBackground)
                ForEach(Array(sessions.enumerated()), id: \.offset) { _, s in
                    let shownIdx = allShown.firstIndex(of: s)
                    let isDim = activeSession != nil && shownIdx != activeSession
                    RoundedRectangle(cornerRadius: 2)
                        .fill(color)
                        .frame(width: max(height, geo.size.width * CGFloat(s.seconds) / day))
                        .offset(x: geo.size.width * CGFloat(max(0, s.startUtc - result.dayStartUtc)) / day)
                        .opacity(isDim ? 0.35 : 1.0)
                }
            }
            .frame(height: height)
            .frame(maxHeight: .infinity, alignment: .center)
            .padding(.vertical, 0)
            .contentShape(Rectangle())
            .onContinuousHover { phase in
                switch phase {
                case .active(let loc):
                    if let i = segmentIndex(sessions, at: loc.x, width: geo.size.width, height: height, result: result) {
                        let s = sessions[i]
                        hoveredSessions[row.appId] = allShown.firstIndex(of: s)
                        stripTip = StripTip(appId: row.appId, text: tipText(s), x: loc.x)
                    } else {
                        hoveredSessions[row.appId] = nil
                        if stripTip?.appId == row.appId { stripTip = nil }
                    }
                case .ended:
                    hoveredSessions[row.appId] = nil
                    if stripTip?.appId == row.appId { stripTip = nil }
                }
            }
            .onTapGesture(coordinateSpace: .local) { loc in
                // A click off the segments falls through to the row, which toggles it.
                guard let i = segmentIndex(sessions, at: loc.x, width: geo.size.width, height: height, result: result) else {
                    toggleRow()
                    return
                }
                let at = allShown.firstIndex(of: sessions[i])
                withAnimation(.easeInOut(duration: 0.18)) {
                    state.todayExpandedAppId = row.appId
                }
                pinnedSessions[row.appId] = (pinnedSessions[row.appId] == at) ? nil : at
            }
            .overlay(alignment: .topLeading) {
                if let tip = stripTip, tip.appId == row.appId {
                    Text(tip.text)
                        .font(.system(size: 10, weight: .medium))
                        .foregroundColor(ThemeColors.textPrimary)
                        .fixedSize()
                        .padding(.horizontal, 7)
                        .padding(.vertical, 3)
                        .background(RoundedRectangle(cornerRadius: 5).fill(ThemeColors.surface))
                        .overlay(RoundedRectangle(cornerRadius: 5).strokeBorder(ThemeColors.border, lineWidth: 1))
                        .offset(x: min(tip.x + 8, max(0, geo.size.width - 150)), y: -26)
                        .allowsHitTesting(false)
                }
            }
        }
        .frame(height: height + 8)
        .padding(.vertical, -4)
        .zIndex(1)
    }
}
