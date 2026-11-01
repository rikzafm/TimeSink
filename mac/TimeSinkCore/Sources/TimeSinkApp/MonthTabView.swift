import SwiftUI
import TimeSinkCore

public struct MonthTabView: View {
    @ObservedObject var state: AppState

    public init(state: AppState) {
        self.state = state
    }

    public var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 14) {
                if let result = state.monthResult {
                    // Month Navigator & total
                    VStack(alignment: .leading, spacing: 4) {
                        HStack {
                            Button(action: {
                                state.monthsBack += 1
                                state.refresh()
                            }) {
                                Image(systemName: "chevron.left")
                                    .font(.system(size: 11, weight: .bold))
                                    .foregroundColor(ThemeColors.textDim)
                                    .frame(width: 20, height: 20)
                            }
                            .buttonStyle(.plain)

                            Text(monthTitle(year: result.year, month: result.month))
                                .font(.system(size: 12, weight: .medium))
                                .foregroundColor(ThemeColors.textDim)

                            Button(action: {
                                if state.monthsBack > 0 {
                                    state.monthsBack -= 1
                                    state.refresh()
                                }
                            }) {
                                Image(systemName: "chevron.right")
                                    .font(.system(size: 11, weight: .bold))
                                    .foregroundColor(state.monthsBack > 0 ? ThemeColors.textDim : ThemeColors.border)
                                    .frame(width: 20, height: 20)
                            }
                            .buttonStyle(.plain)
                            .disabled(state.monthsBack == 0)

                            Spacer()

                            if result.dailyAverageSeconds > 0 {
                                Text("\(Formatters.hoursMinutes(seconds: Int64(result.dailyAverageSeconds))) / day")
                                    .font(.system(size: 11, weight: .medium))
                                    .foregroundColor(ThemeColors.textDim)
                            }
                        }

                        // Headline total
                        HStack(alignment: .firstTextBaseline, spacing: 10) {
                            Text(Formatters.hoursMinutes(seconds: result.totalSeconds))
                                .font(.system(size: 28, weight: .bold, design: .rounded))
                                .foregroundColor(ThemeColors.textPrimary)

                            if result.previousSeconds > 0 {
                                let delta = result.deltaSeconds
                                HStack(spacing: 3) {
                                    Image(systemName: delta >= 0 ? "arrow.up.right" : "arrow.down.right")
                                        .font(.system(size: 10, weight: .bold))
                                    Text("\(delta >= 0 ? "+" : "-")\(Formatters.hoursOrMinutes(seconds: abs(delta)))")
                                        .font(.system(size: 11, weight: .medium))
                                }
                                .foregroundColor(delta >= 0 ? ThemeColors.accent : ThemeColors.warning)
                                .padding(.horizontal, 6)
                                .padding(.vertical, 2)
                                .background(ThemeColors.surfaceRaised)
                                .cornerRadius(5)
                            }
                        }

                        // Device Summary Row in All Scope
                        if state.deviceScope == .all, let totals = result.deviceTotals, totals.count > 1 {
                            let devA = totals.values.first ?? 0
                            let devB = totals.values.dropFirst().first ?? 0
                            DeviceSummaryRowView(
                                localName: state.localDeviceName,
                                remoteName: state.remoteDeviceName,
                                localSeconds: devA,
                                remoteSeconds: devB,
                                onBothSeconds: result.onBothSeconds
                            )
                        }
                    }

                    // Calendar Grid Card
                    VStack(alignment: .leading, spacing: 8) {
                        HStack {
                            Text("MONTH OVERVIEW")
                                .font(.system(size: 10, weight: .semibold))
                                .foregroundColor(ThemeColors.textDim)

                            Spacer()

                            if let best = result.best {
                                Text("Best: \(best.date) · \(Formatters.hoursMinutes(seconds: best.seconds))")
                                    .font(.system(size: 10))
                                    .foregroundColor(ThemeColors.textDim)
                            }
                        }

                        monthCalendarGridView(weeks: result.weeks)
                    }
                    .padding(12)
                    .background(ThemeColors.surface)
                    .cornerRadius(8)
                    .overlay(
                        RoundedRectangle(cornerRadius: 8)
                            .stroke(ThemeColors.border, lineWidth: 1)
                    )

                    // Top 4 apps list
                    VStack(alignment: .leading, spacing: 6) {
                        Text("TOP 4 APPS THIS MONTH")
                            .font(.system(size: 10, weight: .semibold))
                            .foregroundColor(ThemeColors.textDim)
                            .padding(.top, 4)

                        if result.topApps.isEmpty {
                            Text("No apps tracked in this month.")
                                .font(.system(size: 13))
                                .foregroundColor(ThemeColors.textMuted)
                                .padding(.vertical, 16)
                        } else {
                            ForEach(result.topApps, id: \.appId) { appRow in
                                monthAppRowView(app: appRow)
                            }
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

    private func monthTitle(year: Int, month: Int) -> String {
        let df = DateFormatter()
        let monthName = df.monthSymbols[max(0, min(11, month - 1))]
        return "\(monthName) \(year)"
    }

    private func monthCalendarGridView(weeks: [MonthWeek]) -> some View {
        let dayLabels = ["M", "T", "W", "T", "F", "S", "S"]
        return VStack(spacing: 4) {
            // Weekday labels
            HStack(spacing: 4) {
                ForEach(0..<7, id: \.self) { idx in
                    Text(dayLabels[idx])
                        .font(.system(size: 9, weight: .semibold))
                        .foregroundColor(ThemeColors.textDim)
                        .frame(maxWidth: .infinity)
                }
            }

            ForEach(Array(weeks.enumerated()), id: \.offset) { _, week in
                HStack(spacing: 4) {
                    ForEach(0..<7, id: \.self) { dayIdx in
                        let day = dayIdx < week.days.count ? week.days[dayIdx] : nil
                        cellView(day: day)
                    }
                }
            }
        }
    }

    private func cellView(day: MonthDay?) -> some View {
        ZStack {
            if let d = day {
                RoundedRectangle(cornerRadius: 3)
                    .fill(heatColor(level: d.heat, isToday: d.isToday))
                    .frame(height: 24)

                let dayNum = d.date.split(separator: "-").last.map(String.init) ?? ""
                Text(dayNum)
                    .font(.system(size: 10, weight: d.isToday ? .bold : .regular))
                    .foregroundColor(d.heat >= 2 ? ThemeColors.background : ThemeColors.textPrimary)
            } else {
                RoundedRectangle(cornerRadius: 3)
                    .fill(Color.clear)
                    .frame(height: 24)
            }
        }
        .frame(maxWidth: .infinity)
    }

    private func heatColor(level: Int, isToday: Bool) -> Color {
        switch level {
        case 0: return ThemeColors.barBackground
        case 1: return ThemeColors.accentDark.opacity(0.4)
        case 2: return ThemeColors.accentDark
        case 3: return ThemeColors.accent.opacity(0.8)
        default: return ThemeColors.accent
        }
    }

    private func monthAppRowView(app: MonthTopApp) -> some View {
        Button(action: {
            state.openAppDetail(appId: app.appId)
        }) {
            HStack(spacing: 10) {
                // Icon tile
                ZStack {
                    RoundedRectangle(cornerRadius: 6)
                        .fill(ThemeColors.surfaceRaised)
                        .frame(width: 28, height: 28)

                    if let png = state.iconCache.getCachedIcon(forAppId: app.appId),
                       let nsImg = NSImage(data: png) {
                        Image(nsImage: nsImg)
                            .resizable()
                            .frame(width: 20, height: 20)
                            .cornerRadius(3)
                    } else {
                        Text(String(app.name.prefix(1)).uppercased())
                            .font(.system(size: 12, weight: .bold))
                            .foregroundColor(ThemeColors.accent)
                    }
                }

                Text(app.name)
                    .font(.system(size: 13, weight: .medium))
                    .foregroundColor(ThemeColors.textPrimary)

                Spacer()

                if app.previousSeconds > 0 {
                    let delta = app.deltaSeconds
                    HStack(spacing: 2) {
                        Image(systemName: delta >= 0 ? "arrow.up" : "arrow.down")
                            .font(.system(size: 9, weight: .bold))
                        Text(Formatters.hoursOrMinutes(seconds: abs(delta)))
                            .font(.system(size: 10, weight: .medium))
                    }
                    .foregroundColor(delta >= 0 ? ThemeColors.accent : ThemeColors.warning)
                    .padding(.horizontal, 5)
                    .padding(.vertical, 2)
                    .background(ThemeColors.surfaceRaised)
                    .cornerRadius(4)
                }

                Text(Formatters.hoursMinutes(seconds: app.seconds))
                    .font(.system(size: 12, weight: .medium, design: .monospaced))
                    .foregroundColor(ThemeColors.textPrimary)
                    .frame(width: 60, alignment: .trailing)
            }
            .padding(.horizontal, 10)
            .padding(.vertical, 7)
            .background(ThemeColors.surface)
            .cornerRadius(6)
        }
        .buttonStyle(.plain)
    }
}

