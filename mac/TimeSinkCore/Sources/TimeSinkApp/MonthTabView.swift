import SwiftUI
import TimeSinkCore

public struct MonthTabView: View {
    @ObservedObject var state: AppState
    @State private var selectedDate: String?
    @State private var hoveredDayDate: String?
    @State private var hoveredAppId: Int64?
    @State private var pinnedAppId: Int64?

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
                    legend
                    dayCard(result)
                    RankedAppList(state: state, rows: result.topApps.map { ($0.appId, $0.name, $0.seconds) })
                        .padding(.bottom, 8)
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
            PeriodArrow(left: true, enabled: result.canPrev, box: 22, iconSize: 10) {
                selectedDate = nil
                hoveredAppId = nil
                pinnedAppId = nil
                state.previousMonth()
            }
            Text("\(Formatters.monthName(result.month)) \(String(result.year))")
                .font(.system(size: 17, weight: .bold))
                .foregroundColor(ThemeColors.textPrimary)
            PeriodArrow(left: false, enabled: state.monthsBack > 0, box: 22, iconSize: 10) {
                selectedDate = nil
                hoveredAppId = nil
                pinnedAppId = nil
                state.nextMonth()
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
                        .foregroundColor(ThemeColors.accent)
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
                        .foregroundColor(ThemeColors.textPrimary)
                        .frame(width: 40, height: 40, alignment: .leading)
                        .help(week.totalSeconds > 0 ? "Week total: \(Formatters.paddedHM(week.totalSeconds))" : "")
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
        if let day {
            let dayNum = "\(Int(day.date.suffix(2)) ?? 0)"
            let selected = (selectedDate ?? result.defaultSelected) == day.date
            let isHovered = hoveredDayDate == day.date
            let tooltip = "\(Formatters.dayText(day.date, format: "EEEE, d MMMM yyyy"))\n\(day.seconds > 0 ? Formatters.paddedHM(day.seconds) : (day.isFuture ? "Future day" : "No tracked activity"))"

            if day.isFuture {
                Text(dayNum)
                    .font(.system(size: 11, weight: .bold))
                    .foregroundColor(ThemeColors.textMuted)
                    .padding(.horizontal, 6)
                    .padding(.vertical, 5)
                    .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
                    .frame(height: 40)
                    .dashedCard(color: ThemeColors.borderSubtle, radius: 6)
                    .help(tooltip)
            } else {
                let fg = day.heat >= 3 ? ThemeColors.background : ThemeColors.textPrimary
                Button(action: {
                    selectedDate = day.date
                    hoveredAppId = nil
                    pinnedAppId = nil
                }) {
                    ZStack(alignment: .bottom) {
                        VStack(alignment: .leading, spacing: 0) {
                            Text(dayNum)
                                .font(.system(size: 11, weight: .bold))
                            Spacer(minLength: 0)
                            if day.seconds > 0 {
                                Text("\((day.seconds + 1800) / 3600)h")
                                    .font(.system(size: 11))
                                    .frame(maxWidth: .infinity, alignment: .trailing)
                            }
                        }
                        .foregroundColor(fg)
                        .padding(.horizontal, 6)
                        .padding(.vertical, 4)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .frame(height: 40)
                        .background(
                            RoundedRectangle(cornerRadius: 6)
                                .fill(day.heat == 0 ? (isHovered ? ThemeColors.surfaceRaised : ThemeColors.surfaceRaised.opacity(0.4)) : Tokens.heat(day.heat))
                        )

                        // Today underline indicator
                        if day.isToday {
                            RoundedRectangle(cornerRadius: 1)
                                .fill(day.heat >= 3 ? ThemeColors.background : ThemeColors.accent)
                                .frame(height: 3)
                                .padding(.horizontal, 4)
                                .padding(.bottom, 2)
                        }

                        // Selection outline
                        if selected {
                            RoundedRectangle(cornerRadius: 6)
                                .strokeBorder(Color.white, lineWidth: 2)
                        } else if isHovered {
                            RoundedRectangle(cornerRadius: 6)
                                .strokeBorder(ThemeColors.borderSubtle, lineWidth: 1)
                        }
                    }
                    .frame(height: 40)
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .help(tooltip)
                .onHover { isH in
                    hoveredDayDate = isH ? day.date : (hoveredDayDate == day.date ? nil : hoveredDayDate)
                }
            }
        } else {
            Color.clear
                .frame(maxWidth: .infinity)
                .frame(height: 40)
        }
    }

    private var legend: some View {
        HStack(spacing: 4) {
            Spacer()
            Text("Less").font(.system(size: 10)).foregroundColor(ThemeColors.textMuted)
            ForEach(0..<5, id: \.self) { i in
                RoundedRectangle(cornerRadius: 2).fill(Tokens.heat(i)).frame(width: 10, height: 10)
            }
            Text("More").font(.system(size: 10)).foregroundColor(ThemeColors.textMuted)
        }
    }

    @ViewBuilder
    private func dayCard(_ result: MonthResult) -> some View {
        let date = selectedDate ?? result.defaultSelected
        if let day = result.days.first(where: { $0.date == date }) {
            let total = max(1, day.apps.reduce(0) { $0 + $1.seconds })
            let activeFocusId = hoveredAppId ?? pinnedAppId
            let shownApps = Array(day.apps.prefix(8))

            VStack(alignment: .leading, spacing: 0) {
                HStack {
                    Text(Formatters.dayText(day.date, format: "EEEE, MMM d"))
                    Spacer()
                    Text(Formatters.paddedHM(day.seconds))
                }
                .font(.system(size: 12, weight: .semibold))
                .foregroundColor(ThemeColors.textPrimary)

                // Interactive Stacked Bar
                GeometryReader { geo in
                    let gap: CGFloat = 2
                    let count = shownApps.count
                    let totalGaps = CGFloat(max(0, count - 1)) * gap
                    let usableWidth = max(0, geo.size.width - totalGaps)

                    HStack(spacing: gap) {
                        ForEach(shownApps, id: \.appId) { app in
                            let sharePct = total > 0 ? Int((Double(app.seconds) / Double(total) * 100).rounded()) : 0
                            let isFocused = activeFocusId == app.appId
                            let hasFocus = activeFocusId != nil
                            let segWidth = max(3, usableWidth * CGFloat(app.seconds) / CGFloat(total))
                            let barHeight: CGFloat = isFocused ? 12 : 8
                            let opacity: Double = (hasFocus && !isFocused) ? 0.35 : 1.0

                            Button(action: {
                                if pinnedAppId == app.appId {
                                    pinnedAppId = nil
                                } else {
                                    pinnedAppId = app.appId
                                }
                            }) {
                                RoundedRectangle(cornerRadius: 2)
                                    .fill(Tokens.appColor(app.name))
                                    .opacity(opacity)
                                    .frame(width: segWidth, height: barHeight)
                                    .contentShape(Rectangle())
                            }
                            .buttonStyle(.plain)
                            .help("\(app.name): \(Formatters.paddedHM(app.seconds)) (\(sharePct)%)")
                            .onHover { isH in
                                hoveredAppId = isH ? app.appId : (hoveredAppId == app.appId ? nil : hoveredAppId)
                            }
                        }
                    }
                    .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .center)
                }
                .frame(height: 14)
                .padding(.top, 8)
                .padding(.bottom, 6)

                // Subtitle note
                HStack(spacing: 4) {
                    if let focusApp = day.apps.first(where: { $0.appId == activeFocusId }) {
                        let sharePct = total > 0 ? Int((Double(focusApp.seconds) / Double(total) * 100).rounded()) : 0
                        Circle()
                            .fill(Tokens.appColor(focusApp.name))
                            .frame(width: 7, height: 7)
                        Text("\(focusApp.name) · \(Formatters.paddedHM(focusApp.seconds)) (\(sharePct)%)")
                            .font(.system(size: 11, weight: .semibold))
                            .foregroundColor(ThemeColors.textPrimary)
                        if pinnedAppId == focusApp.appId {
                            Text("(pinned)")
                                .font(.system(size: 10))
                                .foregroundColor(ThemeColors.accent)
                        }
                    } else if day.apps.isEmpty {
                        Text("No apps tracked")
                            .font(.system(size: 11))
                            .foregroundColor(ThemeColors.textMuted)
                    } else if let leader = day.apps.first, Double(leader.seconds) / Double(total) >= 0.5 {
                        Text("Mostly \(leader.name) · \(Formatters.paddedHM(leader.seconds))")
                            .font(.system(size: 11))
                            .foregroundColor(ThemeColors.textMuted)
                    } else {
                        Text("\(day.apps.count) apps tracked")
                            .font(.system(size: 11))
                            .foregroundColor(ThemeColors.textMuted)
                    }
                    Spacer()
                }
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 10)
            .card()
        }
    }
}
