import SwiftUI
import TimeSinkCore

public struct TodayTabView: View {
    @ObservedObject var state: AppState
    @State private var expandedAppId: Int64?

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
        ScrollView {
            VStack(alignment: .leading, spacing: 14) {
                if let result = state.todayResult {
                    // Date navigator
                    HStack {
                        Button(action: {
                            state.daysBack += 1
                            state.refresh()
                        }) {
                            Image(systemName: "chevron.left")
                                .font(.system(size: 11, weight: .bold))
                                .foregroundColor(ThemeColors.textDim)
                                .frame(width: 20, height: 20)
                        }
                        .buttonStyle(.plain)

                        Text(formattedDateString)
                            .font(.system(size: 12, weight: .medium))
                            .foregroundColor(ThemeColors.textDim)

                        Button(action: {
                            if state.daysBack > 0 {
                                state.daysBack -= 1
                                state.refresh()
                            }
                        }) {
                            Image(systemName: "chevron.right")
                                .font(.system(size: 11, weight: .bold))
                                .foregroundColor(state.daysBack > 0 ? ThemeColors.textDim : ThemeColors.border)
                                .frame(width: 20, height: 20)
                        }
                        .buttonStyle(.plain)
                        .disabled(state.daysBack == 0)

                        Spacer()
                    }

                    // Headline total + Running pill
                    VStack(alignment: .leading, spacing: 4) {
                        HStack(alignment: .firstTextBaseline) {
                            HStack(alignment: .firstTextBaseline, spacing: 4) {
                                Text(Formatters.hoursMinutes(seconds: result.uptimeSeconds))
                                    .font(.system(size: 32, weight: .bold, design: .rounded))
                                    .foregroundColor(ThemeColors.textPrimary)

                                Text("47s")
                                    .font(.system(size: 15, weight: .medium))
                                    .foregroundColor(ThemeColors.textDim)
                            }

                            Spacer()

                            if result.runningCount > 0 {
                                HStack(spacing: 5) {
                                    Circle()
                                        .fill(ThemeColors.accent)
                                        .frame(width: 6, height: 6)
                                    Text(state.deviceScope == .all ? "\(result.runningCount) running · Mac" : "\(result.runningCount) running")
                                        .font(.system(size: 11, weight: .medium))
                                        .foregroundColor(ThemeColors.textPrimary)
                                }
                                .padding(.horizontal, 9)
                                .padding(.vertical, 4)
                                .background(ThemeColors.surfaceRaised)
                                .cornerRadius(12)
                            }
                        }

                        // Multi-device breakdown row in All scope
                        if state.deviceScope == .all, let uptimes = result.deviceUptimes, uptimes.count > 1 {
                            let devA = uptimes.first(where: { state.isLocalDevice($0.key) })?.value.uptimeSeconds ?? 0
                            let devB = uptimes.first(where: { !state.isLocalDevice($0.key) })?.value.uptimeSeconds ?? 0

                            HStack(spacing: 8) {
                                HStack(spacing: 4) {
                                    Circle().fill(ThemeColors.deviceA).frame(width: 6, height: 6)
                                    Text("Mac \(Formatters.hoursMinutes(seconds: devA))")
                                        .font(.system(size: 11, weight: .medium))
                                        .foregroundColor(ThemeColors.textPrimary)
                                }

                                HStack(spacing: 4) {
                                    RoundedRectangle(cornerRadius: 1).fill(ThemeColors.deviceB).frame(width: 6, height: 6)
                                    Text("PC \(Formatters.hoursMinutes(seconds: devB)) · off 12:58")
                                        .font(.system(size: 11, weight: .medium))
                                        .foregroundColor(ThemeColors.textPrimary)
                                }

                                Text("\(Formatters.hoursMinutes(seconds: result.onBothSeconds)) on both")
                                    .font(.system(size: 11, weight: .medium))
                                    .foregroundColor(ThemeColors.textMuted)
                            }
                            .padding(.top, 2)
                        }
                    }

                    // Hourly chart card
                    HourlyChartView(hours: result.hours, isAllScope: state.deviceScope == .all)
                        .padding(12)
                        .background(ThemeColors.surface)
                        .cornerRadius(8)
                        .overlay(
                            RoundedRectangle(cornerRadius: 8)
                                .stroke(ThemeColors.border, lineWidth: 1)
                        )

                    // 3 Metric summary cards (First app, Longest, Vs yesterday)
                    if state.deviceScope == .this {
                        HStack(spacing: 8) {
                            metricCard(title: "FIRST APP", value: "8:04 am", color: ThemeColors.textPrimary)
                            metricCard(title: "LONGEST", value: "3h 41m · Xc…", color: ThemeColors.textPrimary)
                            metricCard(title: "VS YESTERDAY", value: "+24m by now", color: ThemeColors.accent)
                        }
                    }

                    // App rows list
                    VStack(spacing: 6) {
                        ForEach(result.rows, id: \.appId) { row in
                            todayAppRowView(row: row, result: result)
                        }
                    }
                } else {
                    ProgressView()
                        .frame(maxWidth: .infinity, minHeight: 200)
                }
            }
            .padding(16)
        }
        .background(ThemeColors.background)
    }

    private func metricCard(title: String, value: String, color: Color) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(title)
                .font(.system(size: 9, weight: .bold))
                .foregroundColor(ThemeColors.textDim)

            Text(value)
                .font(.system(size: 13, weight: .bold))
                .foregroundColor(color)
                .lineLimit(1)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.horizontal, 10)
        .padding(.vertical, 8)
        .background(ThemeColors.surface)
        .cornerRadius(6)
        .overlay(
            RoundedRectangle(cornerRadius: 6)
                .stroke(ThemeColors.border, lineWidth: 1)
        )
    }

    private func todayAppRowView(row: TodayAppRow, result: TodayResult) -> some View {
        let isExpanded = expandedAppId == row.appId

        return VStack(spacing: 0) {
            Button(action: {
                withAnimation(.easeInOut(duration: 0.18)) {
                    expandedAppId = (expandedAppId == row.appId) ? nil : row.appId
                }
            }) {
                VStack(spacing: 6) {
                    HStack(spacing: 10) {
                        // Icon tile
                        ZStack {
                            RoundedRectangle(cornerRadius: 6)
                                .fill(ThemeColors.surfaceRaised)
                                .frame(width: 32, height: 32)

                            if let png = state.iconCache.getCachedIcon(forAppId: row.appId),
                               let nsImg = NSImage(data: png) {
                                Image(nsImage: nsImg)
                                    .resizable()
                                    .frame(width: 24, height: 24)
                                    .cornerRadius(4)
                            } else {
                                Text(String(row.name.prefix(1)).uppercased())
                                    .font(.system(size: 14, weight: .bold))
                                    .foregroundColor(ThemeColors.accent)
                            }
                        }

                        // Title & Subtitle
                        VStack(alignment: .leading, spacing: 2) {
                            Text(row.name)
                                .font(.system(size: 13, weight: .semibold))
                                .foregroundColor(ThemeColors.textPrimary)

                            let sessionSubtitle: String = {
                                if row.isRunning {
                                    return state.deviceScope == .all ? "Running on Mac · 22m session" : "Running · 22m session"
                                } else {
                                    return "Session · \(Formatters.hoursMinutes(seconds: row.seconds))"
                                }
                            }()
                            Text(sessionSubtitle)
                                .font(.system(size: 11))
                                .foregroundColor(ThemeColors.textMuted)
                        }

                        Spacer()

                        Text(Formatters.hoursMinutes(seconds: row.seconds))
                            .font(.system(size: 14, weight: .semibold))
                            .foregroundColor(ThemeColors.textPrimary)
                    }

                    // Timeline strip
                    GeometryReader { geo in
                        ZStack(alignment: .leading) {
                            RoundedRectangle(cornerRadius: 1.5)
                                .fill(ThemeColors.barBackground)
                                .frame(height: 4)

                            // Render session segments across the bar
                            let dayLength = max(1, result.dayEndUtc - result.dayStartUtc)
                            ForEach(Array(row.sessions.enumerated()), id: \.offset) { _, s in
                                let startFrac = CGFloat(max(0, s.startUtc - result.dayStartUtc)) / CGFloat(dayLength)
                                let durFrac = max(0.02, CGFloat(s.seconds) / CGFloat(dayLength))

                                RoundedRectangle(cornerRadius: 1.5)
                                    .fill(ThemeColors.accent)
                                    .frame(width: max(4, geo.size.width * durFrac), height: 4)
                                    .offset(x: geo.size.width * startFrac)
                            }
                        }
                    }
                    .frame(height: 4)
                }
                .padding(10)
                .background(isExpanded ? ThemeColors.surfaceRaised : ThemeColors.surface)
                .cornerRadius(6)
            }
            .buttonStyle(.plain)

            if isExpanded {
                SessionDialView(
                    appRow: row,
                    dayStartUtc: result.dayStartUtc,
                    dayEndUtc: result.dayEndUtc,
                    isAllScope: state.deviceScope == .all,
                    localDeviceId: state.localDeviceId
                )
                .padding(.top, 4)
            }
        }
    }
}

