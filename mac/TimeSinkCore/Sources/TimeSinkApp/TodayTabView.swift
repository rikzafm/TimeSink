import SwiftUI
import TimeSinkCore

public struct TodayTabView: View {
    @ObservedObject var state: AppState
    @State private var expandedAppId: Int64?
    @State private var selectedHour: Int? = nil
    @State private var activeSessions: [Int64: Int] = [:]

    public init(state: AppState) {
        self.state = state
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
        HStack(alignment: .bottom) {
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
                    Text(Formatters.paddedHM(result.uptimeSeconds))
                        .font(.system(size: 34, weight: .bold))
                        .tracking(-0.8)
                        .foregroundColor(ThemeColors.accent)
                    Text("\(result.uptimeSeconds % 60)s")
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
                    Text(state.deviceScope == .all ? "\(result.runningCount) running · Mac" : "\(result.runningCount) running")
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
        let uptimes = result.deviceUptimes ?? [:]
        let mac = uptimes.first { state.isLocalDevice($0.key) }?.value
        let pc = uptimes.first { !state.isLocalDevice($0.key) }?.value
        var pcText = "PC \(Formatters.paddedHM(pc?.uptimeSeconds ?? 0))"
        if let pc, !pc.isLive, let last = pc.lastActiveUtc {
            pcText += " · off \(Formatters.formatClock(utcSeconds: last))"
        }
        return DeviceLegend(
            mac: "Mac \(Formatters.paddedHM(mac?.uptimeSeconds ?? 0))",
            pc: pcText,
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
                        expandedAppId = first.appId
                    }
                }
            }) {
                tile("FIRST APP", result.firstApp.map { Formatters.clock12($0.startUtc) } ?? "—", ThemeColors.textPrimary)
            }
            .buttonStyle(.plain)

            Button(action: {
                if let l = result.longestSession {
                    withAnimation(.easeInOut(duration: 0.18)) {
                        expandedAppId = l.appId
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

    private func subtitle(_ row: TodayAppRow, result: TodayResult) -> String {
        if row.isRunning, let since = row.runningSinceUtc {
            let where_ = state.deviceScope == .all ? "Running on Mac" : "Running"
            return "\(where_) · \(Formatters.paddedHM(max(0, result.nowUtc - since))) session"
        }
        if let closed = row.lastClosedUtc {
            return "Closed \(Formatters.clock12(closed))"
        }
        return "\(row.sessions.count) session\(row.sessions.count == 1 ? "" : "s")"
    }

    private func isExpanded(_ row: TodayAppRow, result: TodayResult) -> Bool {
        expandedAppId == row.appId
    }

    @ViewBuilder
    private func appRow(_ row: TodayAppRow, result: TodayResult) -> some View {
        let expanded = isExpanded(row, result: result)
        let activeIdx = activeSessions[row.appId]
        let rowActiveBinding = Binding<Int?>(
            get: { activeSessions[row.appId] },
            set: { activeSessions[row.appId] = $0 }
        )

        VStack(spacing: 0) {
            Button(action: {
                withAnimation(.easeInOut(duration: 0.18)) {
                    expandedAppId = expanded ? nil : row.appId
                }
            }) {
                HStack(spacing: 12) {
                    AppTile(name: row.name, png: state.iconCache.getCachedIcon(forAppId: row.appId))
                    VStack(alignment: .leading, spacing: 0) {
                        HStack {
                            Text(row.name)
                            Spacer()
                            Text(Formatters.paddedHM(row.seconds))
                        }
                        .font(.system(size: 13, weight: .bold))
                        .foregroundColor(ThemeColors.textPrimary)

                        Text(subtitle(row, result: result))
                            .font(.system(size: 10))
                            .foregroundColor(row.isRunning ? ThemeColors.accent : ThemeColors.textMuted)

                        timelines(row, result: result, expanded: expanded, activeSession: activeIdx).padding(.top, 4)
                    }
                }
                .frame(height: 50)
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)

            if expanded {
                SessionDialView(
                    appRow: row,
                    dayStartUtc: result.dayStartUtc,
                    dayEndUtc: result.dayEndUtc,
                    nowUtc: result.nowUtc,
                    isAllScope: state.showsDeviceSplit,
                    localDeviceId: state.localDeviceId,
                    activeSessionIndex: rowActiveBinding
                )
                .padding(.bottom, 10)
            }
        }
        .padding(.horizontal, expanded || state.showsDeviceSplit ? 8 : 0)
        .background(RoundedRectangle(cornerRadius: 10).fill(expanded ? ThemeColors.surfaceActive : .clear))
    }

    @ViewBuilder
    private func timelines(_ row: TodayAppRow, result: TodayResult, expanded: Bool, activeSession: Int?) -> some View {
        let shown = RowDetails.shownSessions(row)
        if state.showsDeviceSplit {
            if expanded {
                VStack(spacing: 2) {
                    strip(row.sessions.filter { state.isLocalDevice($0.deviceId) }, allShown: shown, result: result, color: ThemeColors.deviceA, height: 3, activeSession: activeSession)
                    strip(row.sessions.filter { !state.isLocalDevice($0.deviceId) }, allShown: shown, result: result, color: ThemeColors.deviceB, height: 3, activeSession: activeSession)
                }
            }
        } else {
            strip(row.sessions, allShown: shown, result: result, color: Tokens.appColor(row.name), height: 4, activeSession: activeSession)
        }
    }

    private func strip(
        _ sessions: [TodaySession],
        allShown: [TodaySession],
        result: TodayResult,
        color: Color,
        height: CGFloat,
        activeSession: Int?
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
                        .frame(width: max(3, geo.size.width * CGFloat(s.seconds) / day))
                        .offset(x: geo.size.width * CGFloat(max(0, s.startUtc - result.dayStartUtc)) / day)
                        .opacity(isDim ? 0.35 : 1.0)
                }
            }
        }
        .frame(height: height)
    }
}
