import SwiftUI
import TimeSinkCore

public struct WeekTabView: View {
    @ObservedObject var state: AppState

    public init(state: AppState) {
        self.state = state
    }

    public var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 14) {
                if let result = state.weekResult {
                    // Navigator + period total
                    VStack(alignment: .leading, spacing: 4) {
                        HStack {
                            Button(action: {
                                state.weeksBack += 1
                                state.refresh()
                            }) {
                                Image(systemName: "chevron.left")
                                    .font(.system(size: 11, weight: .bold))
                                    .foregroundColor(ThemeColors.textDim)
                                    .frame(width: 20, height: 20)
                            }
                            .buttonStyle(.plain)

                            Text(result.shownWeekStart)
                                .font(.system(size: 12, weight: .medium))
                                .foregroundColor(ThemeColors.textDim)

                            Button(action: {
                                if state.weeksBack > 0 {
                                    state.weeksBack -= 1
                                    state.refresh()
                                }
                            }) {
                                Image(systemName: "chevron.right")
                                    .font(.system(size: 11, weight: .bold))
                                    .foregroundColor(state.weeksBack > 0 ? ThemeColors.textDim : ThemeColors.border)
                                    .frame(width: 20, height: 20)
                            }
                            .buttonStyle(.plain)
                            .disabled(state.weeksBack == 0)

                            Spacer()

                            if result.streakDays > 0 {
                                HStack(spacing: 4) {
                                    Text("🔥 \(result.streakDays)d streak")
                                        .font(.system(size: 11, weight: .semibold))
                                        .foregroundColor(ThemeColors.accent)
                                }
                                .padding(.horizontal, 7)
                                .padding(.vertical, 2)
                                .background(ThemeColors.surfaceRaised)
                                .cornerRadius(10)
                            }
                        }

                        // Headline total
                        HStack(alignment: .firstTextBaseline, spacing: 10) {
                            Text(Formatters.hoursMinutes(seconds: result.totalSeconds))
                                .font(.system(size: 28, weight: .bold, design: .rounded))
                                .foregroundColor(ThemeColors.textPrimary)

                            if result.previousWeekTotalSeconds > 0 {
                                let delta = result.totalSeconds - result.previousWeekTotalSeconds
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

                    // 7-day columns
                    VStack(alignment: .leading, spacing: 8) {
                        HStack {
                            Text("DAILY UPTIME")
                                .font(.system(size: 10, weight: .semibold))
                                .foregroundColor(ThemeColors.textDim)

                            Spacer()

                            if state.deviceScope == .all {
                                HStack(spacing: 6) {
                                    CompoundDeviceSwatch(width: 14, height: 8)
                                    Text("Overlap")
                                        .font(.system(size: 10))
                                        .foregroundColor(ThemeColors.textDim)
                                }
                            }
                        }

                        weekColumnsView(days: result.days)
                    }
                    .padding(12)
                    .background(ThemeColors.surface)
                    .cornerRadius(8)
                    .overlay(
                        RoundedRectangle(cornerRadius: 8)
                            .stroke(ThemeColors.border, lineWidth: 1)
                    )

                    // Top apps list
                    VStack(alignment: .leading, spacing: 6) {
                        Text("TOP APPS THIS WEEK")
                            .font(.system(size: 10, weight: .semibold))
                            .foregroundColor(ThemeColors.textDim)
                            .padding(.top, 4)

                        if result.rows.isEmpty {
                            Text("No activity recorded for this week.")
                                .font(.system(size: 13))
                                .foregroundColor(ThemeColors.textMuted)
                                .padding(.vertical, 16)
                        } else {
                            ForEach(result.rows, id: \.appId) { appRow in
                                weekAppRowView(row: appRow, maxSeconds: result.rows.map(\.seconds).max() ?? 1)
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

    private func weekColumnsView(days: [WeekDay]) -> some View {
        let maxSec = max(3600, days.map(\.seconds).max() ?? 3600)
        let chartHeight: CGFloat = 80
        let dayLabels = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"]

        return VStack(spacing: 4) {
            HStack(alignment: .bottom, spacing: 8) {
                ForEach(Array(days.enumerated()), id: \.offset) { idx, day in
                    let h = CGFloat(day.seconds) / CGFloat(maxSec) * chartHeight

                    VStack(spacing: 4) {
                        ZStack(alignment: .bottom) {
                            RoundedRectangle(cornerRadius: 2)
                                .fill(ThemeColors.barBackground)
                                .frame(height: chartHeight)

                            if state.deviceScope == .all, let devSecs = day.deviceSeconds, devSecs.count > 1 {
                                let devA = devSecs.values.first ?? 0
                                let devB = devSecs.values.dropFirst().first ?? 0
                                let hA = CGFloat(devA) / CGFloat(maxSec) * chartHeight
                                let hB = CGFloat(devB) / CGFloat(maxSec) * chartHeight

                                ZStack(alignment: .bottomLeading) {
                                    if hA >= hB {
                                        RoundedRectangle(cornerRadius: 2)
                                            .fill(ThemeColors.deviceA)
                                            .frame(width: 24, height: max(2, hA))

                                        RoundedRectangle(cornerRadius: 2)
                                            .fill(ThemeColors.deviceB)
                                            .frame(width: 24, height: max(2, hB))
                                            .offset(x: 10)
                                    } else {
                                        RoundedRectangle(cornerRadius: 2)
                                            .fill(ThemeColors.deviceB)
                                            .frame(width: 24, height: max(2, hB))
                                            .offset(x: 10)

                                        RoundedRectangle(cornerRadius: 2)
                                            .fill(ThemeColors.deviceA)
                                            .frame(width: 24, height: max(2, hA))
                                    }
                                }
                            } else {
                                if day.seconds > 0 {
                                    RoundedRectangle(cornerRadius: 2)
                                        .fill(day.isToday ? ThemeColors.accent : ThemeColors.accentDark)
                                        .frame(height: max(2, h))
                                }
                            }
                        }
                        .frame(maxWidth: .infinity)

                        Text(idx < dayLabels.count ? dayLabels[idx] : "")
                            .font(.system(size: 10, weight: day.isToday ? .bold : .regular))
                            .foregroundColor(day.isToday ? ThemeColors.accent : ThemeColors.textDim)
                    }
                }
            }
        }
    }

    private func weekAppRowView(row: WeekAppRow, maxSeconds: Int64) -> some View {
        Button(action: {
            state.openAppDetail(appId: row.appId)
        }) {
            HStack(spacing: 10) {
                // Icon tile
                ZStack {
                    RoundedRectangle(cornerRadius: 6)
                        .fill(ThemeColors.surfaceRaised)
                        .frame(width: 28, height: 28)

                    if let png = state.iconCache.getCachedIcon(forAppId: row.appId),
                       let nsImg = NSImage(data: png) {
                        Image(nsImage: nsImg)
                            .resizable()
                            .frame(width: 20, height: 20)
                            .cornerRadius(3)
                    } else {
                        Text(String(row.name.prefix(1)).uppercased())
                            .font(.system(size: 12, weight: .bold))
                            .foregroundColor(ThemeColors.accent)
                    }
                }

                Text(row.name)
                    .font(.system(size: 13, weight: .medium))
                    .foregroundColor(ThemeColors.textPrimary)

                Spacer()

                // Sparkline
                HStack(alignment: .bottom, spacing: 2) {
                    let sparkMax = max(1, row.sparkline.max() ?? 1)
                    ForEach(Array(row.sparkline.enumerated()), id: \.offset) { _, s in
                        RoundedRectangle(cornerRadius: 1)
                            .fill(s > 0 ? ThemeColors.accent : ThemeColors.barBackground)
                            .frame(width: 4, height: max(2, CGFloat(s) / CGFloat(sparkMax) * 16))
                    }
                }
                .frame(width: 40, height: 16)

                Text(Formatters.hoursMinutes(seconds: row.seconds))
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
