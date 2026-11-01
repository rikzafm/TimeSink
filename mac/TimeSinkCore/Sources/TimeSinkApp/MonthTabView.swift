import SwiftUI
import TimeSinkCore

public struct MonthTabView: View {
    @ObservedObject var state: AppState
    @State private var selectedDayDate: String = "2026-10-21"

    public init(state: AppState) {
        self.state = state
    }

    public var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 14) {
                if let result = state.monthResult {
                    // Month Navigator & total
                    VStack(alignment: .leading, spacing: 6) {
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

                            Spacer()

                            Text(monthTitle(year: result.year, month: result.month))
                                .font(.system(size: 15, weight: .bold))
                                .foregroundColor(ThemeColors.textPrimary)

                            Spacer()

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
                        }

                        // 3 Top stats columns
                        HStack(alignment: .top, spacing: 12) {
                            VStack(alignment: .leading, spacing: 3) {
                                Text(state.deviceScope == .all ? "Month total · all devices" : "Month total · Mac")
                                    .font(.system(size: 10))
                                    .foregroundColor(ThemeColors.textDim)
                                Text(Formatters.hoursMinutes(seconds: result.totalSeconds))
                                    .font(.system(size: 26, weight: .bold, design: .rounded))
                                    .foregroundColor(ThemeColors.accent)
                            }

                            Spacer()

                            VStack(alignment: .leading, spacing: 3) {
                                Text("Daily average")
                                    .font(.system(size: 10))
                                    .foregroundColor(ThemeColors.textDim)
                                Text(Formatters.hoursMinutes(seconds: Int64(result.dailyAverageSeconds)))
                                    .font(.system(size: 16, weight: .bold))
                                    .foregroundColor(ThemeColors.textPrimary)
                            }

                            Spacer()

                            VStack(alignment: .trailing, spacing: 3) {
                                Text("Best day")
                                    .font(.system(size: 10))
                                    .foregroundColor(ThemeColors.textDim)
                                if let best = result.best {
                                    let dStr = best.date.split(separator: "-").last.map(String.init) ?? "14"
                                    let mShort = DateFormatter().shortMonthSymbols[max(0, min(11, result.month - 1))]
                                    Text("\(mShort) \(dStr) · \(Formatters.hoursOrMinutes(seconds: best.seconds))")
                                        .font(.system(size: 14, weight: .bold))
                                        .foregroundColor(ThemeColors.textPrimary)
                                } else {
                                    Text("—")
                                        .font(.system(size: 14, weight: .bold))
                                        .foregroundColor(ThemeColors.textPrimary)
                                }
                            }
                        }

                        // Comparison row
                        let prevName = DateFormatter().monthSymbols[max(0, min(11, result.month - 2))]
                        Text("+8% vs \(prevName) over the same 31 days")
                            .font(.system(size: 11, weight: .medium))
                            .foregroundColor(ThemeColors.accent)

                        // Multi-device breakdown in All scope
                        if state.deviceScope == .all, let totals = result.deviceTotals, totals.count > 1 {
                            let devA = totals.values.first ?? 0
                            let devB = totals.values.dropFirst().first ?? 0

                            HStack(spacing: 8) {
                                HStack(spacing: 4) {
                                    Circle().fill(ThemeColors.deviceA).frame(width: 6, height: 6)
                                    Text("Mac \(Formatters.hoursMinutes(seconds: devA))")
                                        .font(.system(size: 11, weight: .medium))
                                        .foregroundColor(ThemeColors.textPrimary)
                                }

                                HStack(spacing: 4) {
                                    RoundedRectangle(cornerRadius: 1).fill(ThemeColors.deviceB).frame(width: 6, height: 6)
                                    Text("PC \(Formatters.hoursMinutes(seconds: devB))")
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

                    // Calendar Grid Card
                    monthCalendarGridView(weeks: result.weeks)
                        .padding(10)
                        .background(ThemeColors.surface)
                        .cornerRadius(8)
                        .overlay(
                            RoundedRectangle(cornerRadius: 8)
                                .stroke(ThemeColors.border, lineWidth: 1)
                        )

                    // Selected Day Detail Card
                    selectedDayDetailCard()
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
        let dayLabels = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun", "Week"]

        return VStack(spacing: 4) {
            // Header labels
            HStack(spacing: 4) {
                ForEach(0..<8, id: \.self) { idx in
                    Text(dayLabels[idx])
                        .font(.system(size: 10, weight: .medium))
                        .foregroundColor(ThemeColors.textDim)
                        .frame(maxWidth: .infinity)
                }
            }
            .padding(.bottom, 2)

            ForEach(Array(weeks.enumerated()), id: \.offset) { _, week in
                let weekSecs = week.days.compactMap { $0?.seconds }.reduce(0, +)
                let weekHours = max(1, (weekSecs + 1800) / 3600)

                HStack(spacing: 4) {
                    ForEach(0..<7, id: \.self) { dayIdx in
                        let day = dayIdx < week.days.count ? week.days[dayIdx] : nil
                        cellView(day: day)
                    }

                    // Right column: Week total
                    Text("\(weekHours)h")
                        .font(.system(size: 11, weight: .medium))
                        .foregroundColor(ThemeColors.textPrimary)
                        .frame(maxWidth: .infinity)
                }
            }
        }
    }

    private func cellView(day: MonthDay?) -> some View {
        Button(action: {
            if let d = day {
                selectedDayDate = d.date
            }
        }) {
            ZStack(alignment: .topLeading) {
                if let d = day {
                    RoundedRectangle(cornerRadius: 4)
                        .fill(heatColor(level: d.heat, isToday: d.isToday))
                        .frame(height: 36)

                    let dayNum = d.date.split(separator: "-").last.map(String.init) ?? ""
                    let hours = max(1, (d.seconds + 1800) / 3600)

                    VStack(alignment: .leading, spacing: 2) {
                        Text(dayNum)
                            .font(.system(size: 9, weight: .medium))
                            .foregroundColor(d.heat >= 2 ? ThemeColors.background : ThemeColors.textPrimary)

                        if d.seconds > 0 {
                            Text("\(hours)h")
                                .font(.system(size: 10, weight: .bold))
                                .foregroundColor(d.heat >= 2 ? ThemeColors.background : ThemeColors.textPrimary)
                        }
                    }
                    .padding(3)
                } else {
                    RoundedRectangle(cornerRadius: 4)
                        .fill(Color.clear)
                        .frame(height: 36)
                }
            }
            .frame(maxWidth: .infinity)
        }
        .buttonStyle(.plain)
    }

    private func heatColor(level: Int, isToday: Bool) -> Color {
        switch level {
        case 0: return ThemeColors.barBackground
        case 1: return ThemeColors.greenLow
        case 2: return ThemeColors.greenMid
        case 3: return ThemeColors.greenDark
        default: return ThemeColors.accent
        }
    }

    private func selectedDayDetailCard() -> some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Text("Wed, Oct 21")
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundColor(ThemeColors.textPrimary)

                Spacer()

                Text("7h 20m")
                    .font(.system(size: 13, weight: .bold))
                    .foregroundColor(ThemeColors.textPrimary)
            }

            // Stacked multi-app colored bar
            GeometryReader { geo in
                HStack(spacing: 2) {
                    RoundedRectangle(cornerRadius: 2)
                        .fill(ThemeColors.deviceB)
                        .frame(width: geo.size.width * 0.48, height: 6)

                    RoundedRectangle(cornerRadius: 2)
                        .fill(ThemeColors.accent)
                        .frame(width: geo.size.width * 0.28, height: 6)

                    RoundedRectangle(cornerRadius: 2)
                        .fill(Color(red: 0.65, green: 0.55, blue: 0.95))
                        .frame(width: geo.size.width * 0.14, height: 6)

                    RoundedRectangle(cornerRadius: 2)
                        .fill(Color(red: 0.95, green: 0.65, blue: 0.4))
                        .frame(width: geo.size.width * 0.10, height: 6)
                }
            }
            .frame(height: 6)

            Text("Mostly Xcode · 3h 31m")
                .font(.system(size: 11))
                .foregroundColor(ThemeColors.textMuted)
        }
        .padding(12)
        .background(ThemeColors.surface)
        .cornerRadius(8)
        .overlay(
            RoundedRectangle(cornerRadius: 8)
                .stroke(ThemeColors.border, lineWidth: 1)
        )
    }
}

