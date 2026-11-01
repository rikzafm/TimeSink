import SwiftUI
import TimeSinkCore

public struct WeekTabView: View {
    @ObservedObject var state: AppState
    @State private var selectedDate: String?
    @State private var dayDetail: DayDetail?
    @State private var focusAppId: Int64?
    @State private var hoveredDate: String?
    @State private var hoveredWeekIdx: Int?
    @State private var hoveredSparkKey: String? // "\(appId)-\(date)"

    public init(state: AppState) {
        self.state = state
    }

    public var body: some View {
        ScrollView(.vertical, showsIndicators: false) {
            VStack(alignment: .leading, spacing: 14) {
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
                    columnsView(result)

                    if let detail = dayDetail, selectedDate != nil {
                        dayDetailCard(detail)
                    }

                    historyView(result)

                    VStack(spacing: 2) {
                        ForEach(result.rows, id: \.appId) { appRow($0, result: result) }
                    }
                } else {
                    ProgressView().frame(maxWidth: .infinity, minHeight: 200)
                }
            }
            .padding(.horizontal, 14)
            .padding(.top, 14)
        }
    }

    private func isDayHighlighted(_ day: WeekDay) -> Bool {
        if let selectedDate {
            return day.date == selectedDate
        }
        return day.isToday
    }

    private func toggleDay(_ date: String, focusApp: Int64? = nil) {
        if selectedDate == date && focusAppId == focusApp && focusApp == nil {
            selectedDate = nil
            dayDetail = nil
            focusAppId = nil
        } else {
            selectedDate = date
            focusAppId = focusApp
            dayDetail = state.loadDayDetail(day: date)
        }
    }

    private func headline(_ result: WeekResult) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            HStack(spacing: 8) {
                PeriodArrow(left: true, enabled: result.canPrev(weekStart: WeekStart.parse(state.weekStart)), box: 14, iconSize: 8) {
                    clearSelection()
                    state.previousWeek()
                }
                Text(state.weeksBack == 0 ? "This week" : "Week of \(Formatters.dayText(result.shownWeekStart, format: "d MMM"))")
                    .font(.system(size: 11))
                    .foregroundColor(ThemeColors.textDim)
                PeriodArrow(left: false, enabled: state.weeksBack > 0, box: 14, iconSize: 8) {
                    clearSelection()
                    state.nextWeek()
                }
            }
            .padding(.bottom, 2)

            HStack(alignment: .center) {
                Text(Formatters.paddedHM(result.totalSeconds))
                    .font(.system(size: 34, weight: .bold))
                    .tracking(-0.8)
                    .foregroundColor(ThemeColors.accent)

                Spacer()

                VStack(alignment: .trailing, spacing: 2) {
                    Text("\(result.streakDays)-day streak")
                        .font(.system(size: 15, weight: .bold))
                        .foregroundColor(ThemeColors.textPrimary)
                    Text("apps open daily")
                        .font(.system(size: 11))
                        .foregroundColor(ThemeColors.textMuted)
                }
            }

            comparisonAndAverage(result)
        }
    }

    private func clearSelection() {
        selectedDate = nil
        dayDetail = nil
        focusAppId = nil
    }

    private func comparisonAndAverage(_ result: WeekResult) -> some View {
        let daysCounted = max(1, result.days.filter { !$0.isFuture }.count)
        let avgSeconds = result.totalSeconds / Int64(daysCounted)
        let avgText = "avg \(Formatters.paddedHM(avgSeconds)) / day"

        let vsText = state.weeksBack == 0 ? "vs last week so far" : "vs last week"
        let delta = result.totalSeconds - result.previousWeekTotalSeconds
        let isPositive = delta >= 0
        let arrow = isPositive ? "▲" : "▼"
        let diffText = "\(arrow) \(Formatters.paddedHM(abs(delta))) \(vsText)"

        let fullText: String
        if result.previousWeekTotalSeconds > 0 {
            fullText = "\(diffText) · \(avgText)"
        } else if result.totalSeconds > 0 {
            fullText = avgText
        } else {
            fullText = "avg 0m / day"
        }

        return Text(fullText)
            .font(.system(size: 11))
            .foregroundColor(ThemeColors.textMuted)
            .lineLimit(1)
    }

    private func columnsView(_ result: WeekResult) -> some View {
        let barMaxHeight: CGFloat = 66
        let top = max(3600, result.days.map { d -> Int64 in
            let p = state.deviceSplit(d.deviceSeconds)
            return state.showsDeviceSplit ? max(p.mac, p.pc) : d.seconds
        }.max() ?? 3600)
        let k = barMaxHeight / CGFloat(top)

        let daysCounted = max(1, result.days.filter { !$0.isFuture }.count)
        let avgSeconds = result.totalSeconds / Int64(daysCounted)
        let avgBarHeight = CGFloat(avgSeconds) * k

        return VStack(alignment: .leading, spacing: 0) {
            ZStack(alignment: .bottom) {
                // Dashed horizontal average reference line
                if avgSeconds > 0 {
                    GeometryReader { geo in
                        let lineY = geo.size.height - avgBarHeight
                        Path { path in
                            path.move(to: CGPoint(x: 0, y: lineY))
                            path.addLine(to: CGPoint(x: geo.size.width, y: lineY))
                        }
                        .stroke(ThemeColors.textMuted.opacity(0.35), style: StrokeStyle(lineWidth: 1, dash: [4, 4]))
                    }
                }

                // 7 Day columns
                HStack(alignment: .bottom, spacing: 8) {
                    ForEach(result.days, id: \.date) { day in
                        let highlighted = isDayHighlighted(day)
                        let isHovered = hoveredDate == day.date
                        let barH = CGFloat(day.seconds) * k
                        let tooltip = "\(Formatters.dayText(day.date, format: "EEEE, d MMMM yyyy"))\n\(day.seconds > 0 ? Formatters.paddedHM(day.seconds) : "No activity tracked")"

                        Button(action: {
                            if !day.isFuture { toggleDay(day.date) }
                        }) {
                            ZStack(alignment: .bottom) {
                                if isHovered && !day.isFuture {
                                    RoundedRectangle(cornerRadius: 5)
                                        .fill(ThemeColors.textMuted.opacity(0.12))
                                        .frame(height: barMaxHeight + 18)
                                }

                                VStack(spacing: 3) {
                                    Text(day.seconds > 0 ? Formatters.paddedHM(day.seconds) : "")
                                        .font(.system(size: 10.5, weight: highlighted ? .bold : .medium))
                                        .foregroundColor(highlighted ? ThemeColors.accent : ThemeColors.textMuted)
                                        .lineLimit(1)
                                        .minimumScaleFactor(0.7)

                                    if state.showsDeviceSplit {
                                        let p = state.deviceSplit(day.deviceSeconds)
                                        let macH = CGFloat(p.mac) * k
                                        let pcH = CGFloat(p.pc) * k
                                        OverlapBar(
                                            mac: macH,
                                            pc: pcH,
                                            macColor: highlighted ? ThemeColors.accent : Tokens.ramp[3],
                                            pcColor: highlighted ? ThemeColors.deviceB : ThemeColors.deviceBDark,
                                            radius: 4
                                        )
                                        .frame(height: max(macH, pcH))
                                    } else {
                                        RoundedRectangle(cornerRadius: 4)
                                            .fill(highlighted ? ThemeColors.accent : Tokens.ramp[2])
                                            .frame(height: max(day.seconds > 0 ? 3 : 0, barH))
                                    }
                                }

                                if selectedDate == day.date {
                                    RoundedRectangle(cornerRadius: 5)
                                        .strokeBorder(ThemeColors.accent, lineWidth: 1.5)
                                        .frame(height: barMaxHeight + 18)
                                }
                            }
                            .frame(maxWidth: .infinity, alignment: .bottom)
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                        .help(tooltip)
                        .onHover { isH in
                            if !day.isFuture {
                                hoveredDate = isH ? day.date : (hoveredDate == day.date ? nil : hoveredDate)
                            }
                        }
                    }
                }
            }
            .frame(height: barMaxHeight + 18)

            // Day labels row
            HStack(spacing: 8) {
                ForEach(result.days, id: \.date) { day in
                    let highlighted = isDayHighlighted(day)
                    Button(action: {
                        if !day.isFuture { toggleDay(day.date) }
                    }) {
                        Text(Formatters.dayText(day.date, format: "EEE"))
                            .font(.system(size: 11, weight: highlighted ? .bold : .regular))
                            .foregroundColor(highlighted ? ThemeColors.accent : (day.isFuture ? ThemeColors.textDisabled : ThemeColors.textMuted))
                            .frame(maxWidth: .infinity)
                            .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .help(Formatters.dayText(day.date, format: "EEEE, d MMMM yyyy"))
                    .onHover { isH in
                        if !day.isFuture {
                            hoveredDate = isH ? day.date : (hoveredDate == day.date ? nil : hoveredDate)
                        }
                    }
                }
            }
            .padding(.top, 5)
        }
    }

    @ViewBuilder
    private func dayDetailCard(_ detail: DayDetail) -> some View {
        let total = max(1, detail.totalSeconds)
        let sortedApps: [BarApp] = {
            if let focusId = focusAppId, let idx = detail.apps.firstIndex(where: { $0.appId == focusId }) {
                var list = detail.apps
                let focus = list.remove(at: idx)
                list.insert(focus, at: 0)
                return list
            }
            return detail.apps
        }()

        VStack(alignment: .leading, spacing: 8) {
            // Header: Title & Close
            HStack(alignment: .top) {
                VStack(alignment: .leading, spacing: 2) {
                    Text(Formatters.dayText(detail.date, format: "EEEE, d MMMM yyyy"))
                        .font(.system(size: 13, weight: .bold))
                        .foregroundColor(ThemeColors.textPrimary)

                    let spanText: String = {
                        if let first = detail.firstStartUtc, let last = detail.lastEndUtc {
                            return " · active \(Formatters.formatClock(utcSeconds: first)) – \(Formatters.formatClock(utcSeconds: last))"
                        }
                        return ""
                    }()

                    let subText: String = {
                        if let focusId = focusAppId, let f = detail.apps.first(where: { $0.appId == focusId }) {
                            let fTime = f.seconds > 0 ? Formatters.paddedHM(f.seconds) : "not used"
                            return "\(f.name): \(fTime) of \(Formatters.paddedHM(detail.totalSeconds))\(spanText)"
                        } else if detail.totalSeconds == 0 {
                            return "No activity tracked"
                        } else if let first = detail.apps.first {
                            return "\(Formatters.paddedHM(detail.totalSeconds)) · mostly \(first.name) (\(Formatters.paddedHM(first.seconds)))\(spanText)"
                        } else {
                            return "\(Formatters.paddedHM(detail.totalSeconds))\(spanText)"
                        }
                    }()

                    Text(subText)
                        .font(.system(size: 11))
                        .foregroundColor(ThemeColors.textMuted)
                }

                Spacer()

                Button(action: clearSelection) {
                    Text("✕")
                        .font(.system(size: 11, weight: .semibold))
                        .foregroundColor(ThemeColors.textMuted)
                        .padding(4)
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
            }

            // App breakdown bars
            if !sortedApps.isEmpty {
                VStack(spacing: 7) {
                    ForEach(sortedApps, id: \.appId) { app in
                        let sharePct = detail.totalSeconds > 0 ? Int((Double(app.seconds) / Double(detail.totalSeconds) * 100).rounded()) : 0
                        Button(action: { state.openAppDetail(appId: app.appId) }) {
                            VStack(alignment: .leading, spacing: 3) {
                                HStack {
                                    Text(app.name)
                                        .font(.system(size: 12, weight: .semibold))
                                        .foregroundColor(ThemeColors.textPrimary)
                                        .lineLimit(1)
                                    Spacer()
                                    Text("\(Formatters.paddedHM(app.seconds)) · \(sharePct)%")
                                        .font(.system(size: 10))
                                        .foregroundColor(ThemeColors.textMuted)
                                }
                                GeometryReader { geo in
                                    ZStack(alignment: .leading) {
                                        RoundedRectangle(cornerRadius: 3).fill(ThemeColors.barBackground)
                                        RoundedRectangle(cornerRadius: 3)
                                            .fill(Tokens.appColor(app.name))
                                            .frame(width: max(4, geo.size.width * CGFloat(app.seconds) / CGFloat(total)))
                                    }
                                }
                                .frame(height: 6)
                            }
                        }
                        .buttonStyle(.plain)
                        .help("Click to inspect \(app.name)")
                    }
                }
            }

            // Comparison metrics
            Divider().overlay(ThemeColors.borderSubtle).padding(.vertical, 2)

            let deltaAvg = detail.totalSeconds - Int64(detail.weekAverageSeconds.rounded())
            let signAvg = deltaAvg >= 0 ? "+" : "-"
            HStack {
                Text("vs week average")
                    .font(.system(size: 11))
                    .foregroundColor(ThemeColors.textMuted)
                Spacer()
                Text("\(signAvg)\(Formatters.paddedHM(abs(deltaAvg)))")
                    .font(.system(size: 11, weight: .semibold))
                    .foregroundColor(deltaAvg >= 0 ? ThemeColors.accent : ThemeColors.warning)
            }

            if detail.sameDayLastWeekSeconds > 0 || detail.totalSeconds > 0 {
                let deltaLast = detail.totalSeconds - detail.sameDayLastWeekSeconds
                let signLast = deltaLast >= 0 ? "+" : "-"
                HStack {
                    Text("vs same day last week")
                        .font(.system(size: 11))
                        .foregroundColor(ThemeColors.textMuted)
                    Spacer()
                    Text("\(signLast)\(Formatters.paddedHM(abs(deltaLast)))")
                        .font(.system(size: 11, weight: .semibold))
                        .foregroundColor(deltaLast >= 0 ? ThemeColors.accent : ThemeColors.warning)
                }
            }

            // Action: Open in Today
            Button(action: {
                state.openDayInToday(dateString: detail.date)
            }) {
                HStack(spacing: 4) {
                    Text("Open in Today")
                        .font(.system(size: 11, weight: .semibold))
                        .foregroundColor(ThemeColors.accent)
                    Image(systemName: "arrow.right")
                        .font(.system(size: 10, weight: .semibold))
                        .foregroundColor(ThemeColors.accent)
                    Spacer()
                }
                .padding(.top, 4)
            }
            .buttonStyle(.plain)
        }
        .padding(12)
        .background(RoundedRectangle(cornerRadius: 10).fill(ThemeColors.surfaceActive))
        .overlay(RoundedRectangle(cornerRadius: 10).strokeBorder(ThemeColors.accent, lineWidth: 1))
    }

    private func historyView(_ result: WeekResult) -> some View {
        let maxWeek = max(3600, result.weekTotals.max() ?? 3600)
        let activeIndex = (WeekQuery.gridWeeks - 1) - state.weeksBack

        return VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text("Last 20 weeks")
                    .foregroundColor(ThemeColors.textMuted)
                Spacer()
                Text("weekly total")
                    .foregroundColor(ThemeColors.textMuted)
            }
            .font(.system(size: 11))

            HStack(alignment: .bottom, spacing: 3.5) {
                ForEach(0..<result.weekTotals.count, id: \.self) { idx in
                    let total = result.weekTotals[idx]
                    let isActive = (idx == activeIndex)
                    let isHovered = (hoveredWeekIdx == idx)
                    let barH = max(2.5, CGFloat(total) / CGFloat(maxWeek) * 26)

                    let startIdx = idx * 7
                    let endIdx = min(result.heatGrid.count - 1, idx * 7 + 6)
                    let startDay = startIdx < result.heatGrid.count ? result.heatGrid[startIdx].date : ""
                    let endDay = endIdx < result.heatGrid.count ? result.heatGrid[endIdx].date : ""
                    let rangeStr = !startDay.isEmpty ? "\(Formatters.dayText(startDay, format: "d MMM")) – \(Formatters.dayText(endDay, format: "d MMM"))" : "Week \(idx + 1)"
                    let tip = "\(rangeStr)\n\(Formatters.paddedHM(total))"

                    Button(action: {
                        let back = (WeekQuery.gridWeeks - 1) - idx
                        if back >= 0 {
                            clearSelection()
                            state.weeksBack = back
                            state.refresh()
                        }
                    }) {
                        ZStack(alignment: .bottom) {
                            if isHovered {
                                RoundedRectangle(cornerRadius: 2)
                                    .fill(ThemeColors.textMuted.opacity(0.2))
                                    .frame(height: 28)
                            }
                            RoundedRectangle(cornerRadius: 1.5)
                                .fill(isActive ? ThemeColors.accent : (isHovered ? Tokens.ramp[3] : (total > 0 ? Tokens.ramp[2] : Tokens.ramp[1])))
                                .frame(height: barH)
                        }
                        .frame(maxWidth: .infinity, maxHeight: 28, alignment: .bottom)
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .help(tip)
                    .onHover { isH in
                        hoveredWeekIdx = isH ? idx : (hoveredWeekIdx == idx ? nil : hoveredWeekIdx)
                    }
                }
            }
            .frame(height: 28, alignment: .bottom)
        }
        .padding(.top, 4)
    }

    private func sparkDate(for index: Int, total: Int, todayStr: String) -> String {
        let df = DateFormatter()
        df.dateFormat = "yyyy-MM-dd"
        guard let today = df.date(from: todayStr) else { return todayStr }
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = .current
        if let d = calendar.date(byAdding: .day, value: index - (total - 1), to: today) {
            return df.string(from: d)
        }
        return todayStr
    }

    private func selectSparkDay(_ dateStr: String, appId: Int64, result: WeekResult) {
        if result.days.contains(where: { $0.date == dateStr }) {
            toggleDay(dateStr, focusApp: appId)
        } else {
            // Day belongs to another week: navigate to that week and select day
            let ws = WeekStart.parse(state.weekStart)
            let targetWeekStart = WeekStarts.containing(day: dateStr, weekStart: ws)
            let df = DateFormatter()
            df.dateFormat = "yyyy-MM-dd"
            if let tDate = df.date(from: targetWeekStart), let nowD = df.date(from: result.today) {
                var cal = Calendar(identifier: .gregorian)
                cal.timeZone = .current
                let diffDays = cal.dateComponents([.day], from: tDate, to: nowD).day ?? 0
                state.weeksBack = max(0, diffDays / 7)
                state.refresh()
                toggleDay(dateStr, focusApp: appId)
            }
        }
    }

    private func appRow(_ row: WeekAppRow, result: WeekResult) -> some View {
        HStack(spacing: 10) {
            Button(action: { state.openAppDetail(appId: row.appId) }) {
                HStack(spacing: 10) {
                    AppTile(name: row.name, png: state.iconCache.getCachedIcon(forAppId: row.appId))
                    Text(row.name)
                        .font(.system(size: 13, weight: .semibold))
                        .foregroundColor(ThemeColors.textPrimary)
                        .lineLimit(1)
                }
            }
            .buttonStyle(.plain)

            Spacer()

            // Sparkline bars (interactive)
            HStack(alignment: .bottom, spacing: 2) {
                let top = max(1, row.sparkline.max() ?? 1)
                ForEach(Array(row.sparkline.enumerated()), id: \.offset) { sIdx, s in
                    let sDate = sparkDate(for: sIdx, total: row.sparkline.count, todayStr: result.today)
                    let key = "\(row.appId)-\(sDate)"
                    let isHovered = hoveredSparkKey == key
                    let tip = "\(row.name)\n\(Formatters.dayText(sDate, format: "EEEE, d MMM")) · \(Formatters.paddedHM(s))"

                    Button(action: {
                        selectSparkDay(sDate, appId: row.appId, result: result)
                    }) {
                        ZStack(alignment: .bottom) {
                            if isHovered {
                                RoundedRectangle(cornerRadius: 1.5)
                                    .fill(ThemeColors.textMuted.opacity(0.2))
                                    .frame(width: 6, height: 22)
                            }
                            RoundedRectangle(cornerRadius: 1)
                                .fill(isHovered ? ThemeColors.accent : Tokens.appColor(row.name).opacity(0.8))
                                .frame(width: 4, height: max(2, CGFloat(s) / CGFloat(top) * 20))
                        }
                        .frame(width: 6, height: 22, alignment: .bottom)
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .help(tip)
                    .onHover { isH in
                        hoveredSparkKey = isH ? key : (hoveredSparkKey == key ? nil : hoveredSparkKey)
                    }
                }
            }
            .frame(height: 22, alignment: .bottom)

            Button(action: { state.openAppDetail(appId: row.appId) }) {
                Text(Formatters.paddedHM(row.seconds))
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundColor(ThemeColors.textPrimary)
                    .frame(width: 62, alignment: .trailing)
            }
            .buttonStyle(.plain)
        }
        .padding(.horizontal, 6)
        .frame(height: 46)
        .contentShape(Rectangle())
    }
}
