import SwiftUI
import TimeSinkCore

public struct WeekTabView: View {
    @ObservedObject var state: AppState

    public init(state: AppState) {
        self.state = state
    }

    public var body: some View {
        ScrollView(.vertical, showsIndicators: false) {
            VStack(alignment: .leading, spacing: 12) {
                if let result = state.weekResult {
                    headline(result)
                    if state.showsDeviceSplit {
                        let t = state.deviceSplit(result.deviceTotals)
                        DeviceLegend(
                            mac: "Mac \(Formatters.paddedHM(t.mac))",
                            pc: "PC \(Formatters.paddedHM(t.pc))",
                            both: "\(Formatters.paddedHM(result.onBothSeconds)) on both"
                        )
                    }
                    columnsCard(result)
                    heatCard(result)
                    VStack(spacing: 2) {
                        ForEach(result.rows, id: \.appId) { appRow($0) }
                    }
                } else {
                    ProgressView().frame(maxWidth: .infinity, minHeight: 200)
                }
            }
            .padding(.horizontal, 14)
            .padding(.top, 14)
        }
    }

    private func headline(_ result: WeekResult) -> some View {
        HStack(alignment: .bottom) {
            VStack(alignment: .leading, spacing: 0) {
                HStack(spacing: 10) {
                    PeriodArrow(left: true, enabled: true, iconSize: 8) {
                        state.weeksBack += 1
                        state.refresh()
                    }
                    Text(state.weeksBack == 0 ? "This week" : "Week of \(Formatters.dayText(result.shownWeekStart, format: "d MMM"))")
                        .font(.system(size: 11))
                        .foregroundColor(ThemeColors.textMuted)
                    PeriodArrow(left: false, enabled: state.weeksBack > 0, iconSize: 8) {
                        state.weeksBack -= 1
                        state.refresh()
                    }
                }
                Text(Formatters.paddedHM(result.totalSeconds))
                    .font(.system(size: 32, weight: .semibold))
                    .tracking(-0.5)
                    .foregroundColor(ThemeColors.textPrimary)
                    .frame(height: 38)
            }
            Spacer()
            if result.previousWeekTotalSeconds > 0 {
                let delta = result.totalSeconds - result.previousWeekTotalSeconds
                Text("\(delta >= 0 ? "+" : "-")\(Formatters.paddedHM(abs(delta))) vs last week")
                    .font(.system(size: 11))
                    .foregroundColor(delta >= 0 ? ThemeColors.accent : ThemeColors.warning)
                    .padding(.bottom, 6)
            }
        }
    }

    private func columnsCard(_ result: WeekResult) -> some View {
        let top = max(3600, result.days.map { d -> Int64 in
            let p = state.deviceSplit(d.deviceSeconds)
            return state.showsDeviceSplit ? max(p.mac, p.pc) : d.seconds
        }.max() ?? 3600)
        let k = 62 / CGFloat(top)

        return VStack(alignment: .leading, spacing: 0) {
            HStack(alignment: .bottom, spacing: 10) {
                ForEach(result.days, id: \.date) { day in
                    let p = state.deviceSplit(day.deviceSeconds)
                    let mac = state.showsDeviceSplit ? p.mac : day.seconds
                    let pc = state.showsDeviceSplit ? p.pc : 0
                    VStack(spacing: 3) {
                        Text(day.seconds > 0 ? "\((day.seconds + 1800) / 3600)h" : "")
                            .font(.system(size: 10))
                            .foregroundColor(ThemeColors.textMuted)
                        OverlapBar(
                            mac: CGFloat(mac) * k,
                            pc: CGFloat(pc) * k,
                            macColor: day.isToday ? ThemeColors.accent : Tokens.ramp[3],
                            pcColor: day.isToday ? ThemeColors.deviceB : ThemeColors.deviceBDark
                        )
                        .frame(height: CGFloat(max(mac, pc)) * k)
                    }
                    .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .bottom)
                }
            }
            .frame(height: 80)

            HStack(spacing: 10) {
                ForEach(result.days, id: \.date) { day in
                    Text(Formatters.dayText(day.date, format: "EEE"))
                        .font(.system(size: 10, weight: day.isToday ? .semibold : .regular))
                        .foregroundColor(day.isToday ? ThemeColors.accent : ThemeColors.textMuted)
                        .frame(maxWidth: .infinity)
                }
            }
            .padding(.top, 5)

            HStack(spacing: 12) {
                HStack(spacing: 4) {
                    RoundedRectangle(cornerRadius: 2).fill(Tokens.ramp[3]).frame(width: 7, height: 7)
                    Text("Mac")
                }
                HStack(spacing: 4) {
                    RoundedRectangle(cornerRadius: 2).fill(ThemeColors.deviceBDark).frame(width: 7, height: 7)
                    Text("PC")
                }
                Text("Overlapping bars = both on · label is uptime, overlap once")
            }
            .font(.system(size: 9))
            .foregroundColor(ThemeColors.textMuted)
            .lineLimit(1)
            .padding(.top, 6)
        }
        .padding(.top, 10)
        .padding(.horizontal, 12)
        .padding(.bottom, 8)
        .frame(height: 134, alignment: .top)
        .card()
    }

    private func heatCard(_ result: WeekResult) -> some View {
        VStack(spacing: 8) {
            HStack {
                Text("Last 20 weeks").foregroundColor(ThemeColors.textMuted)
                Spacer()
                Text("\(result.streakDays)-day streak")
                    .fontWeight(.semibold)
                    .foregroundColor(ThemeColors.accent)
            }
            .font(.system(size: 11))

            HStack(spacing: 3) {
                ForEach(0..<20, id: \.self) { col in
                    VStack(spacing: 3) {
                        ForEach(0..<7, id: \.self) { row in
                            let i = col * 7 + row
                            RoundedRectangle(cornerRadius: 2)
                                .fill(i < result.heatGrid.count ? Tokens.heat(result.heatGrid[i].level) : Tokens.ramp[0])
                                .frame(height: 10)
                                .opacity(i < result.heatGrid.count && result.heatGrid[i].isFuture ? 0.4 : 1)
                        }
                    }
                }
            }
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 10)
        .card()
    }

    private func appRow(_ row: WeekAppRow) -> some View {
        Button(action: { state.openAppDetail(appId: row.appId) }) {
            HStack(spacing: 10) {
                AppTile(name: row.name, png: state.iconCache.getCachedIcon(forAppId: row.appId))
                Text(row.name)
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundColor(ThemeColors.textPrimary)
                    .lineLimit(1)
                Spacer()
                HStack(alignment: .bottom, spacing: 2) {
                    let top = max(1, row.sparkline.max() ?? 1)
                    ForEach(Array(row.sparkline.enumerated()), id: \.offset) { _, s in
                        RoundedRectangle(cornerRadius: 1)
                            .fill(Tokens.appColor(row.name).opacity(0.8))
                            .frame(width: 4, height: max(2, CGFloat(s) / CGFloat(top) * 20))
                    }
                }
                .frame(height: 20, alignment: .bottom)
                Text(Formatters.paddedHM(row.seconds))
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundColor(ThemeColors.textPrimary)
                    .frame(width: 62, alignment: .trailing)
            }
            .padding(.horizontal, 6)
            .frame(height: 46)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }
}
