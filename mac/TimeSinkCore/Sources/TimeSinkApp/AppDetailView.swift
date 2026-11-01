import SwiftUI
import TimeSinkCore

public struct AppDetailView: View {
    @ObservedObject var state: AppState
    @State private var showingSettings = false
    @State private var range: AppDetailRange = .year
    @State private var mergedCount = 0

    public init(state: AppState) {
        self.state = state
    }

    public var body: some View {
        ZStack {
            VStack(spacing: 0) {
                header
                ScrollView(.vertical, showsIndicators: false) {
                    if let result = state.appDetailResult {
                        content(result)
                    } else {
                        ProgressView().frame(maxWidth: .infinity, minHeight: 200)
                    }
                }
                footer
            }
            .background(ThemeColors.background)

            if showingSettings, let appId = state.selectedAppDetailId {
                AppSettingsSheet(state: state, appId: appId) {
                    showingSettings = false
                    loadMerged()
                    state.refresh()
                }
            }
        }
        .frame(width: 400, height: 640)
        .onAppear(perform: loadMerged)
    }

    private func loadMerged() {
        guard let id = state.selectedAppDetailId else { return }
        mergedCount = ((try? MacAppLibrary(db: state.db).getMergedChildren(mainId: id)) ?? []).count
    }

    private var header: some View {
        HStack(spacing: 6) {
            Button(action: { state.closeAppDetail() }) {
                HStack(spacing: 4) {
                    Text("‹").font(.system(size: 16))
                    Text("All").font(.system(size: 13))
                }
                .foregroundColor(ThemeColors.accent)
                .padding(.horizontal, 8)
                .frame(height: 28)
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)

            Text("App detail")
                .font(.system(size: 14, weight: .semibold))
                .foregroundColor(ThemeColors.textPrimary)
                .frame(maxWidth: .infinity)

            Button(action: { showingSettings = true }) {
                Text("Settings")
                    .font(.system(size: 12))
                    .foregroundColor(ThemeColors.textDim)
                    .padding(.horizontal, 8)
                    .frame(height: 28)
                    .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
        }
        .padding(.horizontal, 10)
        .frame(height: 44)
    }

    private var footer: some View {
        HStack {
            Text("App time, not uptime").foregroundColor(ThemeColors.textMuted)
            Spacer()
            Button(action: {
                if let id = state.selectedAppDetailId { state.exportAppCsv(appId: id) }
            }) {
                Text("Export app (CSV)").foregroundColor(ThemeColors.textDim)
            }
            .buttonStyle(.plain)
        }
        .font(.system(size: 12))
        .padding(.horizontal, 14)
        .frame(height: 44)
        .topRule()
    }

    private func content(_ result: AppDetailResult) -> some View {
        VStack(alignment: .leading, spacing: 12) {
            hero(result)
            HStack(spacing: 8) {
                stat("Last opened", lastOpened(result), result.last?.running == true ? ThemeColors.accent : ThemeColors.textPrimary)
                stat("Average session", Formatters.paddedHM(result.averageSessionSeconds), ThemeColors.textPrimary)
                stat("Times opened", Formatters.grouped(Int64(result.sessionCount)), ThemeColors.textPrimary)
            }
            if state.showsDeviceSplit { deviceCard(result) }
            rangeRow(result)
            if range == .year { yearGrid(result) } else { rangeBars(result) }
        }
        .padding(.horizontal, 14)
        .padding(.top, 4)
    }

    private func lastOpened(_ result: AppDetailResult) -> String {
        guard let last = result.last else { return "Never" }
        return last.running ? "Running now" : Formatters.timeAgo(last.startUtc)
    }

    private func hero(_ result: AppDetailResult) -> some View {
        let lifetime = max(1, state.allResult?.totalSeconds ?? result.totalSeconds)
        let share = Int((Double(result.totalSeconds) / Double(lifetime) * 100).rounded())
        var sub = "tracked since \(Formatters.dayText(result.firstTracked, format: "d MMM yyyy"))"
        if mergedCount > 0 { sub = "Merged ×\(mergedCount) · " + sub }

        return HStack(spacing: 12) {
            ZStack {
                RoundedRectangle(cornerRadius: 11).fill(ThemeColors.accent)
                if let png = state.iconCache.getCachedIcon(forAppId: result.appId), let img = NSImage(data: png) {
                    Image(nsImage: img).resizable().padding(5)
                } else {
                    Text(String(result.name.prefix(1)).uppercased())
                        .font(.system(size: 20, weight: .bold))
                        .foregroundColor(ThemeColors.background)
                }
            }
            .frame(width: 44, height: 44)

            VStack(alignment: .leading, spacing: 2) {
                Text(result.name)
                    .font(.system(size: 18, weight: .bold))
                    .foregroundColor(ThemeColors.textPrimary)
                    .lineLimit(1)
                Text(sub)
                    .font(.system(size: 11))
                    .foregroundColor(ThemeColors.textMuted)
                    .lineLimit(1)
            }

            Spacer(minLength: 4)

            VStack(alignment: .trailing, spacing: 0) {
                Text(Formatters.hours(result.totalSeconds))
                    .font(.system(size: 22, weight: .semibold))
                    .tracking(-0.4)
                    .foregroundColor(ThemeColors.textPrimary)
                Text("lifetime · \(share)% share")
                    .font(.system(size: 10))
                    .foregroundColor(ThemeColors.textMuted)
            }
        }
    }

    private func stat(_ label: String, _ value: String, _ color: Color) -> some View {
        VStack(alignment: .leading, spacing: 3) {
            Text(label)
                .font(.system(size: 10))
                .foregroundColor(ThemeColors.textMuted)
            Text(value)
                .font(.system(size: 13, weight: .semibold))
                .foregroundColor(color)
                .lineLimit(1)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.horizontal, 10)
        .padding(.vertical, 8)
        .card()
    }

    private func deviceCard(_ result: AppDetailResult) -> some View {
        let split = state.deviceSplit(result.deviceSeconds)
        return VStack(spacing: 6) {
            HStack {
                HStack(spacing: 6) { DeviceMarker(isLocal: true, size: 7); Text("Mac \(Formatters.hours(split.mac))") }
                Spacer()
                HStack(spacing: 6) { Text("PC \(Formatters.hours(split.pc))"); DeviceMarker(isLocal: false, size: 7) }
            }
            .font(.system(size: 11))
            .foregroundColor(ThemeColors.textDim)

            GeometryReader { geo in
                let total = CGFloat(max(1, split.mac + split.pc))
                let room = geo.size.width - 2
                HStack(spacing: 2) {
                    Rectangle().fill(ThemeColors.deviceA).frame(width: room * CGFloat(split.mac) / total)
                    Rectangle().fill(ThemeColors.deviceB).frame(width: room * CGFloat(split.pc) / total)
                }
            }
            .frame(height: 8)
            .clipShape(Capsule())
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 10)
        .card()
    }

    private func yearButton(_ glyph: String, enabled: Bool, delta: Int) -> some View {
        Button(action: {
            state.detailYear += delta
            state.refresh()
        }) {
            Text(glyph)
                .font(.system(size: 13))
                .foregroundColor(enabled ? ThemeColors.textPrimary : ThemeColors.textMuted)
                .frame(width: 24, height: 24)
                .card(fill: Tokens.segmentBg, radius: 6)
        }
        .buttonStyle(.plain)
        .disabled(!enabled)
    }

    private func rangeRow(_ result: AppDetailResult) -> some View {
        HStack {
            HStack(spacing: 6) {
                yearButton("‹", enabled: result.year > result.firstYear, delta: -1)
                Text(String(result.year))
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundColor(ThemeColors.textPrimary)
                    .frame(width: 40)
                yearButton("›", enabled: result.year < result.lastYear, delta: 1)
            }
            Spacer()
            Segmented(
                options: [(AppDetailRange.all, "All"), (.year, "Year"), (.month, "Month"), (.week, "Week")],
                selection: $range,
                vPad: 2,
                radius: 7,
                fill: Tokens.segmentBg,
                border: ThemeColors.borderSubtle
            )
        }
    }

    private func yearGrid(_ result: AppDetailResult) -> some View {
        let byDate = Dictionary(result.days.map { ($0.date, $0) }, uniquingKeysWith: { a, _ in a })
        let columns = Array(repeating: GridItem(.flexible(), spacing: 12), count: 4)
        return VStack(alignment: .leading, spacing: 12) {
            LazyVGrid(columns: columns, alignment: .leading, spacing: 10) {
                ForEach(1...12, id: \.self) { month in
                    monthBlock(month, year: result.year, byDate: byDate)
                }
            }
            HStack(spacing: 6) {
                Text("Quarters of this app's busiest day (\(Formatters.paddedHM(result.peakDaySeconds)))")
                Spacer()
                HStack(spacing: 2) {
                    ForEach(0..<5, id: \.self) { RoundedRectangle(cornerRadius: 2).fill(Tokens.heat($0)).frame(width: 10, height: 8) }
                }
            }
            .font(.system(size: 10))
            .foregroundColor(ThemeColors.textMuted)
        }
    }

    private func monthBlock(_ month: Int, year: Int, byDate: [String: AppDetailDay]) -> some View {
        var cal = Calendar(identifier: .gregorian)
        cal.firstWeekday = 2
        let first = cal.date(from: DateComponents(year: year, month: month, day: 1)) ?? Date()
        let lead = (cal.component(.weekday, from: first) + 5) % 7
        let count = cal.range(of: .day, in: .month, for: first)?.count ?? 30
        let columns = Array(repeating: GridItem(.flexible(), spacing: 2), count: 7)

        return VStack(alignment: .leading, spacing: 3) {
            Text(Formatters.monthName(month, short: true))
                .font(.system(size: 10))
                .foregroundColor(ThemeColors.textMuted)
            LazyVGrid(columns: columns, spacing: 2) {
                ForEach(0..<(lead + count), id: \.self) { i in
                    if i < lead {
                        Color.clear.frame(height: 8)
                    } else {
                        dayCell(byDate[String(format: "%04d-%02d-%02d", year, month, i - lead + 1)])
                    }
                }
            }
        }
    }

    @ViewBuilder
    private func dayCell(_ day: AppDetailDay?) -> some View {
        if let day, day.kind == .tracked {
            RoundedRectangle(cornerRadius: 2).fill(Tokens.heat(day.level)).frame(height: 8)
        } else {
            RoundedRectangle(cornerRadius: 2).strokeBorder(ThemeColors.borderSubtle, lineWidth: 1).frame(height: 8)
        }
    }

    @ViewBuilder
    private func rangeBars(_ result: AppDetailResult) -> some View {
        let today = LocalDays.dayString(for: Int64(Date().timeIntervalSince1970), timeZone: .current)
        let chart = AppDetailChart.build(
            range: range,
            anchor: today,
            today: today,
            firstTracked: result.firstTracked,
            weekStart: WeekStart.parse(state.weekStart),
            daySeconds: result.daySeconds ?? [:],
            opens: result.opens ?? []
        )
        let top = CGFloat(max(1, chart.bars.map(\.seconds).max() ?? 1))
        VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .bottom, spacing: 3) {
                ForEach(Array(chart.bars.enumerated()), id: \.offset) { _, bar in
                    RoundedRectangle(cornerRadius: 2)
                        .fill(ThemeColors.accent)
                        .frame(height: max(2, CGFloat(bar.seconds) / top * 120))
                        .frame(maxWidth: .infinity)
                        .help("\(bar.label) · \(Formatters.paddedHM(bar.seconds))")
                }
            }
            .frame(height: 120, alignment: .bottom)
            Text("\(Formatters.paddedHM(chart.totalSeconds)) in this period")
                .font(.system(size: 10))
                .foregroundColor(ThemeColors.textMuted)
        }
    }
}
