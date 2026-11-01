import SwiftUI
import TimeSinkCore

public struct MonthTabView: View {
    @ObservedObject var state: AppState
    @State private var selectedDate: String?

    public init(state: AppState) {
        self.state = state
    }

    public var body: some View {
        ScrollView(.vertical, showsIndicators: false) {
            VStack(alignment: .leading, spacing: 12) {
                if let result = state.monthResult {
                    navigator(result)
                    stats(result)
                    if let text = comparison(result) {
                        Text(text.0)
                            .font(.system(size: 11))
                            .foregroundColor(text.1 ? ThemeColors.accent : ThemeColors.warning)
                    }
                    if state.showsDeviceSplit {
                        let t = state.deviceSplit(result.deviceTotals)
                        DeviceLegend(
                            mac: "Mac \(Formatters.paddedHM(t.mac))",
                            pc: "PC \(Formatters.paddedHM(t.pc))",
                            both: "\(Formatters.paddedHM(result.onBothSeconds)) on both"
                        )
                    }
                    calendar(result)
                    dayCard(result)
                } else {
                    ProgressView().frame(maxWidth: .infinity, minHeight: 200)
                }
            }
            .padding(.horizontal, 14)
            .padding(.top, 14)
        }
    }

    private func navigator(_ result: MonthResult) -> some View {
        HStack(spacing: 30) {
            PeriodArrow(left: true, enabled: true, box: 22, iconSize: 10) {
                state.monthsBack += 1
                selectedDate = nil
                state.refresh()
            }
            Text("\(Formatters.monthName(result.month)) \(String(result.year))")
                .font(.system(size: 17, weight: .bold))
                .foregroundColor(ThemeColors.textPrimary)
            PeriodArrow(left: false, enabled: state.monthsBack > 0, box: 22, iconSize: 10) {
                state.monthsBack -= 1
                selectedDate = nil
                state.refresh()
            }
        }
        .frame(maxWidth: .infinity)
        .frame(height: 30)
    }

    private func stats(_ result: MonthResult) -> some View {
        GeometryReader { geo in
            let unit = (geo.size.width - 16) / 3.4
            HStack(alignment: .top, spacing: 8) {
                VStack(alignment: .leading, spacing: 0) {
                    label("Month total · \(state.deviceScope == .all ? "all devices" : "this Mac")")
                    Text(Formatters.paddedHM(result.totalSeconds))
                        .font(.system(size: 26, weight: .semibold))
                        .tracking(-0.4)
                        .foregroundColor(ThemeColors.textPrimary)
                }
                .frame(width: unit * 1.4, alignment: .leading)

                small("Daily average", Formatters.paddedHM(Int64(result.dailyAverageSeconds))).frame(width: unit, alignment: .leading)

                small("Best day", result.best.map {
                    "\(Formatters.dayText($0.date, format: "MMM d")) · \(($0.seconds + 1800) / 3600)h"
                } ?? "—").frame(width: unit, alignment: .leading)
            }
        }
        .frame(height: 46)
    }

    private func label(_ text: String) -> some View {
        Text(text)
            .font(.system(size: 11))
            .foregroundColor(ThemeColors.textMuted)
            .lineLimit(1)
    }

    private func small(_ title: String, _ value: String) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            label(title)
            Text(value)
                .font(.system(size: 15, weight: .semibold))
                .foregroundColor(ThemeColors.textPrimary)
                .lineLimit(1)
        }
    }

    private func comparison(_ result: MonthResult) -> (String, Bool)? {
        guard result.previousSeconds > 0 else { return nil }
        let pct = Int((Double(result.deltaSeconds) / Double(result.previousSeconds) * 100).rounded())
        let prev = Formatters.monthName(result.month == 1 ? 12 : result.month - 1)
        return ("\(pct >= 0 ? "+" : "")\(pct)% vs \(prev) over the same \(result.daysCounted) day\(result.daysCounted == 1 ? "" : "s")", pct >= 0)
    }

    private func calendar(_ result: MonthResult) -> some View {
        let days = weekdayLabels(result)
        return VStack(spacing: 4) {
            HStack(spacing: 4) {
                ForEach(Array(days.enumerated()), id: \.offset) { _, d in
                    Text(d).frame(maxWidth: .infinity)
                }
                Text("Week").frame(width: 40)
            }
            .font(.system(size: 10))
            .foregroundColor(ThemeColors.textMuted)

            ForEach(Array(result.weeks.enumerated()), id: \.offset) { _, week in
                HStack(spacing: 4) {
                    ForEach(0..<7, id: \.self) { i in
                        cell(i < week.days.count ? week.days[i] : nil, result: result)
                    }
                    Text(week.totalSeconds > 0 ? "\((week.totalSeconds + 1800) / 3600)h" : "")
                        .font(.system(size: 11, weight: .semibold))
                        .foregroundColor(ThemeColors.textDim)
                        .frame(width: 40, height: 40, alignment: .bottomLeading)
                        .padding(.bottom, 8)
                }
            }
        }
    }

    private func weekdayLabels(_ result: MonthResult) -> [String] {
        let start = WeekStart.parse(state.weekStart)
        let names = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"]
        let shift = start == .mon ? 0 : start == .sun ? 6 : 5
        return (0..<7).map { names[($0 + shift) % 7] }
    }

    @ViewBuilder
    private func cell(_ day: MonthDay?, result: MonthResult) -> some View {
        if let day, !day.isFuture {
            let selected = (selectedDate ?? result.defaultSelected) == day.date
            let fg = day.heat == 0 ? ThemeColors.textMuted : day.heat >= 3 ? ThemeColors.background : ThemeColors.textPrimary
            Button(action: { selectedDate = day.date }) {
                VStack(alignment: .leading, spacing: 0) {
                    Text("\(Int(day.date.suffix(2)) ?? 0)")
                        .font(.system(size: 10))
                        .opacity(0.85)
                    Spacer(minLength: 0)
                    Text(day.seconds > 0 ? "\((day.seconds + 1800) / 3600)h" : "")
                        .font(.system(size: 11, weight: .semibold))
                }
                .foregroundColor(fg)
                .padding(.horizontal, 5)
                .padding(.vertical, 4)
                .frame(maxWidth: .infinity, alignment: .leading)
                .frame(height: 40)
                .background(RoundedRectangle(cornerRadius: 6).fill(Tokens.heat(day.heat)))
                .overlay(RoundedRectangle(cornerRadius: 6).strokeBorder(selected ? ThemeColors.accent : .clear, lineWidth: 1))
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
        } else {
            Color.clear
                .frame(maxWidth: .infinity)
                .frame(height: 40)
                .dashedCard(color: ThemeColors.borderSubtle, radius: 6)
        }
    }

    @ViewBuilder
    private func dayCard(_ result: MonthResult) -> some View {
        let date = selectedDate ?? result.defaultSelected
        if let day = result.days.first(where: { $0.date == date }) {
            VStack(alignment: .leading, spacing: 0) {
                HStack {
                    Text(Formatters.dayText(day.date, format: "EEE, MMM d"))
                    Spacer()
                    Text(Formatters.paddedHM(day.seconds))
                }
                .font(.system(size: 12, weight: .semibold))
                .foregroundColor(ThemeColors.textPrimary)

                GeometryReader { geo in
                    let total = max(1, day.apps.reduce(0) { $0 + $1.seconds })
                    let shown = Array(day.apps.prefix(6))
                    let room = geo.size.width - CGFloat(max(0, shown.count - 1)) * 2
                    HStack(spacing: 2) {
                        ForEach(shown, id: \.appId) { app in
                            Rectangle()
                                .fill(Tokens.appColor(app.name))
                                .frame(width: room * CGFloat(app.seconds) / CGFloat(total))
                        }
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                }
                .frame(height: 8)
                .background(ThemeColors.barBackground)
                .clipShape(RoundedRectangle(cornerRadius: 4))
                .padding(.top, 8)
                .padding(.bottom, 6)

                Text(day.apps.first.map { "Mostly \($0.name) · \(Formatters.paddedHM($0.seconds))" } ?? "No apps tracked")
                    .font(.system(size: 11))
                    .foregroundColor(ThemeColors.textMuted)
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 10)
            .card()
        }
    }
}
