import SwiftUI
import TimeSinkCore

public struct YearTabView: View {
    @ObservedObject var state: AppState
    @State private var selectedMonth: Int?
    @State private var selectedRow: Int64?

    public init(state: AppState) {
        self.state = state
    }

    private var isWrapped: Bool { state.yearSubView == .wrapped }

    public var body: some View {
        VStack(spacing: isWrapped ? 10 : 12) {
            Segmented(
                options: YearSubView.allCases.map { ($0, $0.rawValue) },
                selection: $state.yearSubView,
                hPad: 14,
                radius: isWrapped ? 8 : 7,
                fill: isWrapped ? .clear : Tokens.segmentBg,
                border: isWrapped ? ThemeColors.border : ThemeColors.borderSubtle
            )

            switch state.yearSubView {
            case .pace:
                ScrollView(.vertical, showsIndicators: false) { paceView }
            case .wrapped:
                wrappedView
            case .matrix:
                ScrollView(.vertical, showsIndicators: false) { matrixView }
            }
        }
        .padding(.horizontal, isWrapped ? 16 : 14)
        .padding(.top, isWrapped ? 10 : 12)
    }

    // MARK: - Pace

    private func h(_ seconds: Int64) -> String { Formatters.hours(seconds) }

    @ViewBuilder
    private var paceView: some View {
        if let result = state.yearResult {
            let scope = state.deviceScope == .all ? "uptime, all devices (overlap once)" : "uptime, this Mac"
            VStack(alignment: .leading, spacing: 12) {
                HStack(alignment: .bottom) {
                    VStack(alignment: .leading, spacing: 0) {
                        Text("\(String(result.year)) so far · \(scope)")
                            .font(.system(size: 11))
                            .foregroundColor(ThemeColors.textMuted)
                        Text(h(result.totalSeconds))
                            .font(.system(size: 30, weight: .semibold))
                            .tracking(-0.5)
                            .foregroundColor(ThemeColors.textPrimary)
                            .frame(height: 36)
                    }
                    Spacer()
                    VStack(alignment: .trailing, spacing: 0) {
                        if let avg = result.averageMonthSeconds { Text("Avg \(h(avg)) / month").frame(height: 16) }
                        if let best = result.bestMonth {
                            Text("Best \(Formatters.monthName(best, short: true)) \(h(result.bestMonthSeconds))").frame(height: 16)
                        }
                    }
                    .font(.system(size: 11))
                    .foregroundColor(ThemeColors.textMuted)
                }

                if state.showsDeviceSplit {
                    let t = state.deviceSplit(result.deviceTotals)
                    DeviceLegend(mac: "Mac \(h(t.mac))", pc: "PC \(h(t.pc))", both: "\(h(result.onBothSeconds)) on both")
                }

                paceChart(result)

                if let pace = result.paceYearSeconds {
                    (Text("On pace for ") + Text(h(pace)).bold() + Text(" by December, based on your last \(result.recentMonthCount == 1 ? "month" : "\(result.recentMonthCount) months")"))
                        .font(.system(size: 12))
                        .lineLimit(1)
                        .minimumScaleFactor(0.85)
                        .foregroundColor(ThemeColors.textPrimary)
                        .padding(.horizontal, 12)
                        .padding(.vertical, 10)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .card(border: ThemeColors.accent)
                }

                monthCard(result)
            }
        }
    }

    private func paceChart(_ result: YearResult) -> some View {
        let height: CGFloat = 160
        let top = max(3600, result.months.map { max($0.seconds, $0.projectedSeconds) }.max() ?? 3600)
        let k = (height - 6) / CGFloat(top)
        let bestOrCurrent = result.months.first { $0.kind == .current }?.month

        return VStack(alignment: .leading, spacing: 0) {
            ZStack(alignment: .bottom) {
                if let avg = result.averageMonthSeconds {
                    Rectangle()
                        .stroke(ThemeColors.textMuted.opacity(0.6), style: StrokeStyle(lineWidth: 1, dash: [3, 3]))
                        .frame(height: 0.5)
                        .offset(y: -CGFloat(avg) * k)
                }
                HStack(alignment: .bottom, spacing: 6) {
                    ForEach(result.months, id: \.month) { m in
                        Button(action: { selectedMonth = m.month }) {
                            paceColumn(m, k: k).frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .bottom).contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                    }
                }
            }
            .frame(height: height)

            HStack(spacing: 6) {
                ForEach(result.months, id: \.month) { m in
                    let cur = m.month == bestOrCurrent
                    Text(String(Formatters.monthName(m.month).prefix(1)))
                        .font(.system(size: 10, weight: cur ? .semibold : .regular))
                        .foregroundColor(cur ? ThemeColors.accent : ThemeColors.textMuted)
                        .frame(maxWidth: .infinity)
                }
            }
            .padding(.top, 6)

            HStack(spacing: 12) {
                HStack(spacing: 4) { DeviceMarker(isLocal: true, size: 7); Text("Mac") }
                HStack(spacing: 4) { DeviceMarker(isLocal: false, size: 7); Text("PC") }
                HStack(spacing: 4) {
                    Hatch(color: Tokens.ramp[1], line: 2, gap: 2)
                        .frame(width: 10, height: 7)
                        .dashedCard(color: Tokens.ramp[3], radius: 0)
                    Text("Projected")
                }
                HStack(spacing: 4) {
                    Rectangle().stroke(ThemeColors.textMuted, style: StrokeStyle(lineWidth: 1, dash: [3, 2])).frame(width: 12, height: 0.5)
                    Text("Monthly avg")
                }
            }
            .font(.system(size: 10))
            .foregroundColor(ThemeColors.textDim)
            .padding(.top, 10)
        }
        .padding(.top, 10)
        .padding(.horizontal, 12)
        .padding(.bottom, 8)
        .card()
    }

    @ViewBuilder
    private func paceColumn(_ m: YearMonth, k: CGFloat) -> some View {
        let parts = state.deviceSplit(m.deviceSeconds)
        let mac = CGFloat(state.showsDeviceSplit ? parts.mac : m.seconds) * k
        let pc = CGFloat(state.showsDeviceSplit ? parts.pc : 0) * k
        switch m.kind {
        case .beforeTracking:
            Rectangle().stroke(ThemeColors.border, style: StrokeStyle(lineWidth: 2, dash: [2, 2])).frame(height: 1)
        case .future:
            RoundedRectangle(cornerRadius: 3)
                .fill(.clear)
                .dashedCard(color: ThemeColors.border, radius: 3)
                .frame(height: CGFloat(m.projectedSeconds) * k)
        case .tracked, .current:
            VStack(spacing: 0) {
                if m.kind == .current, m.projectedSeconds > m.seconds {
                    Hatch(color: Tokens.ramp[1], line: 3, gap: 3)
                        .frame(height: CGFloat(m.projectedSeconds - m.seconds) * k)
                        .clipShape(UnevenRoundedRectangle(topLeadingRadius: 3, topTrailingRadius: 3))
                        .dashedCard(color: Tokens.ramp[3], radius: 3)
                }
                OverlapBar(mac: mac, pc: pc, macColor: ThemeColors.deviceA, pcColor: ThemeColors.deviceB)
                    .frame(height: max(mac, pc))
            }
        }
    }

    @ViewBuilder
    private func monthCard(_ result: YearResult) -> some View {
        if let month = result.months.first(where: { $0.month == (selectedMonth ?? result.bestMonth) }), month.seconds > 0 {
            let isBest = month.month == result.bestMonth
            VStack(alignment: .leading, spacing: 4) {
                HStack {
                    Text(Formatters.monthName(month.month)).foregroundColor(ThemeColors.textPrimary)
                    Spacer()
                    Text(isBest ? "\(h(month.seconds)) · your best month" : h(month.seconds)).foregroundColor(ThemeColors.accent)
                }
                .font(.system(size: 12, weight: .semibold))

                Text(monthLine(month))
                    .font(.system(size: 11))
                    .foregroundColor(ThemeColors.textMuted)
                    .lineLimit(1)
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 10)
            .card()
        }
    }

    private func monthLine(_ m: YearMonth) -> String {
        var parts: [String] = []
        if let prev = m.previousSeconds, prev > 0 {
            let pct = Int((Double(m.seconds - prev) / Double(prev) * 100).rounded())
            parts.append("\(pct >= 0 ? "+" : "")\(pct)% vs \(Formatters.monthName(m.month == 1 ? 12 : m.month - 1))")
        }
        parts += m.topApps.prefix(3).map { "\($0.name) \(h($0.seconds))" }
        return parts.joined(separator: " · ")
    }

    // MARK: - Wrapped

    @ViewBuilder
    private var wrappedView: some View {
        if let result = state.wrappedResult {
            VStack(spacing: 10) {
                WrappedCardView(state: state, result: result, index: state.wrappedCardIndex)
                    .frame(maxHeight: .infinity)

                HStack {
                    deckButton("‹", enabled: state.wrappedCardIndex > 0) { state.wrappedCardIndex -= 1 }
                    Spacer()
                    HStack(spacing: 6) {
                        ForEach(0..<WrappedCardView.cardCount, id: \.self) { i in
                            let on = i == state.wrappedCardIndex
                            Capsule().fill(on ? ThemeColors.accent : ThemeColors.surfaceRaised).frame(width: on ? 22 : 6, height: 6)
                        }
                    }
                    Spacer()
                    deckButton("›", enabled: state.wrappedCardIndex < WrappedCardView.cardCount - 1) { state.wrappedCardIndex += 1 }
                }
                .frame(height: 34)
            }
        }
    }

    private func deckButton(_ glyph: String, enabled: Bool, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Text(glyph)
                .font(.system(size: 15))
                .foregroundColor(enabled ? ThemeColors.textPrimary : ThemeColors.textDisabled)
                .frame(width: 32, height: 30)
                .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(ThemeColors.border, lineWidth: 1))
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .disabled(!enabled)
    }

    // MARK: - Matrix

    @ViewBuilder
    private var matrixView: some View {
        if let result = state.matrixResult {
            let row = result.rows.first { $0.appId == selectedRow } ?? result.rows.first
            VStack(alignment: .leading, spacing: 12) {
                Text("\(String(result.year)) · hours per app per month · since \(Formatters.monthName(result.firstMonth, short: true)) · \(state.deviceScope == .all ? "all devices" : "this Mac")")
                    .font(.system(size: 11))
                    .foregroundColor(ThemeColors.textMuted)
                    .lineLimit(1)

                matrixTable(result, selected: row?.appId)

                HStack(spacing: 8) {
                    Text("Colour is relative to each app's own best month.")
                    Spacer()
                    HStack(spacing: 2) {
                        ForEach(0..<5, id: \.self) { RoundedRectangle(cornerRadius: 2).fill(Tokens.heat($0)).frame(width: 12, height: 8) }
                    }
                }
                .font(.system(size: 10))
                .foregroundColor(ThemeColors.textMuted)

                if let row { matrixCard(row, result: result) }
            }
        }
    }

    private func matrixTable(_ result: MatrixResult, selected: Int64?) -> some View {
        let months = result.months
        return VStack(spacing: 3) {
            HStack(spacing: 3) {
                Color.clear.frame(width: 78, height: 1)
                ForEach(months, id: \.month) { m in
                    Text(String(Formatters.monthName(m.month).prefix(1))).frame(maxWidth: .infinity)
                }
                Text("Total").frame(width: 40, alignment: .trailing)
            }
            .font(.system(size: 10))
            .foregroundColor(ThemeColors.textMuted)

            ForEach(result.rows, id: \.appId) { row in
                let on = row.appId == selected
                Button(action: { selectedRow = row.appId }) {
                    HStack(spacing: 3) {
                        Text(row.name)
                            .font(.system(size: 11, weight: on ? .bold : .medium))
                            .foregroundColor(on ? ThemeColors.accent : ThemeColors.textPrimary)
                            .lineLimit(1)
                            .frame(width: 78, alignment: .leading)

                        ForEach(Array(row.cells.enumerated()), id: \.offset) { _, cell in
                            let hours = (cell.seconds + 1800) / 3600
                            Text(hours > 0 ? "\(hours)" : "")
                                .font(.system(size: 10, weight: .semibold))
                                .foregroundColor(cell.level >= 3 ? ThemeColors.background : ThemeColors.textDim)
                                .frame(maxWidth: .infinity)
                                .frame(height: 26)
                                .background(RoundedRectangle(cornerRadius: 4).fill(Tokens.heat(cell.level)))
                        }

                        Text(Formatters.grouped((row.totalSeconds + 1800) / 3600))
                            .font(.system(size: 11, weight: .semibold))
                            .foregroundColor(ThemeColors.textPrimary)
                            .frame(width: 40, alignment: .trailing)
                    }
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
            }

            HStack(spacing: 3) {
                Text("Total")
                    .font(.system(size: 11, weight: .semibold))
                    .foregroundColor(ThemeColors.textPrimary)
                    .frame(width: 78, alignment: .leading)
                ForEach(months, id: \.month) { m in
                    Text("\((m.totalSeconds + 1800) / 3600)")
                        .font(.system(size: 10, weight: .semibold))
                        .foregroundColor(ThemeColors.textDim)
                        .frame(maxWidth: .infinity)
                }
                Text(Formatters.grouped((result.grandSeconds + 1800) / 3600))
                    .font(.system(size: 11, weight: .bold))
                    .foregroundColor(ThemeColors.accent)
                    .frame(width: 40, alignment: .trailing)
            }
            .padding(.top, 6)
            .topRule()
        }
        .padding(.top, 10)
        .padding(.horizontal, 8)
        .padding(.bottom, 8)
        .card()
    }

    private func matrixCard(_ row: MatrixRow, result: MatrixResult) -> some View {
        let secs = row.cells.map(\.seconds)
        let last3 = secs.suffix(3).reduce(0, +)
        let prev3 = secs.dropLast(3).suffix(3).reduce(0, +)
        var line = "Peak \(Formatters.monthName(row.peakMonth, short: true)) (\(h(row.peakSeconds)))."
        if prev3 > 0 {
            let pct = Int((Double(last3 - prev3) / Double(prev3) * 100).rounded())
            line += " Last 3 months \(pct >= 0 ? "up" : "down") \(abs(pct))% on the 3 before."
        }
        return VStack(alignment: .leading, spacing: 4) {
            HStack {
                Text(row.name)
                Spacer()
                Text("\(h(row.totalSeconds)) · \(row.sharePercent)%")
            }
            .font(.system(size: 12, weight: .semibold))
            .foregroundColor(ThemeColors.textPrimary)

            Text(line)
                .font(.system(size: 11))
                .foregroundColor(ThemeColors.textMuted)
                .lineLimit(1)
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 10)
        .card()
    }
}

/// One card of the Wrapped deck. Also what Save as image renders.
public struct WrappedCardView: View {
    @ObservedObject var state: AppState
    let result: WrappedResult
    let index: Int

    public static let cardCount = 5

    private func h(_ seconds: Int64) -> String { "\((seconds + 1800) / 3600)h" }
    private var yearText: String { String(result.year) }

    public var body: some View {
        ZStack(alignment: .topTrailing) {
            RoundedRectangle(cornerRadius: 16).fill(ThemeColors.surface)
            dots
            content
                .padding(.top, 20)
                .padding(.horizontal, 20)
                .padding(.bottom, index == 0 ? 18 : 16)
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        }
        .clipShape(RoundedRectangle(cornerRadius: 16))
        .overlay(RoundedRectangle(cornerRadius: 16).strokeBorder(ThemeColors.borderSubtle, lineWidth: 1))
    }

    private var dots: some View {
        Canvas { ctx, size in
            for x in stride(from: CGFloat(3.5), to: size.width, by: 7) {
                for y in stride(from: CGFloat(3.5), to: size.height, by: 7) {
                    ctx.fill(Path(ellipseIn: CGRect(x: x - 1.2, y: y - 1.2, width: 2.4, height: 2.4)), with: .color(Tokens.ramp[2].opacity(0.55)))
                }
            }
        }
        .frame(width: 200, height: 170)
        .overlay(LinearGradient(stops: [.init(color: .clear, location: 0.2), .init(color: ThemeColors.surface, location: 0.85)], startPoint: .topTrailing, endPoint: .bottomLeading))
    }

    @ViewBuilder
    private var content: some View {
        switch index {
        case 0: totalCard
        case 1: podiumCard(eyebrow: "YOUR TOP APPS", title: result.podium.first.map { "\($0.name) kept you company most." } ?? "No apps yet.", podium: result.podium, others: result.others)
        case 2: podiumCard(eyebrow: "YOUR TOP GAMES", title: result.gamesTop?.podium.first.map { "\($0.name) was your game." } ?? "No games yet.", podium: result.gamesTop?.podium ?? [], others: result.gamesTop?.others ?? [])
        case 3: podiumCard(eyebrow: "YOUR TOP GACHA", title: result.gachaTop?.podium.first.map { "\($0.name) got the daily logins." } ?? "No gacha games yet.", podium: result.gachaTop?.podium ?? [], others: result.gachaTop?.others ?? [])
        default: habitsCard
        }
    }

    private func eyebrow(_ text: String) -> some View {
        Text(text).caps(10, tracking: 2, weight: .bold)
    }

    private var totalCard: some View {
        let split = state.deviceSplit(result.deviceTotals)
        let total = CGFloat(max(1, result.totalSeconds))
        return VStack(alignment: .leading, spacing: 0) {
            eyebrow("YOUR \(yearText) SO FAR")

            HStack(alignment: .lastTextBaseline, spacing: 8) {
                Text(Formatters.grouped(result.totalHours))
                    .font(.system(size: 92, weight: .heavy))
                    .tracking(-4)
                    .foregroundColor(ThemeColors.accent)
                    .lineLimit(1)
                    .minimumScaleFactor(0.5)
                Text("hours")
                    .font(.system(size: 22, weight: .bold))
                    .foregroundColor(ThemeColors.textPrimary)
            }
            .frame(height: 84)
            .padding(.top, 22)

            (Text("That's ") + Text("\(result.equivalentDays) days and \(result.equivalentHours) hours").foregroundColor(ThemeColors.accent) + Text(" of your apps running, back to back."))
                .font(.system(size: 17, weight: .semibold))
                .lineSpacing(4)
                .foregroundColor(ThemeColors.textPrimary)
                .frame(maxWidth: 300, alignment: .leading)
                .padding(.top, 22)

            if state.showsDeviceSplit {
                GeometryReader { geo in
                    ZStack(alignment: .leading) {
                        Rectangle().fill(ThemeColors.barBackground)
                        Rectangle().fill(ThemeColors.deviceB.opacity(0.8))
                            .frame(width: geo.size.width * CGFloat(split.pc) / total)
                            .frame(maxWidth: .infinity, alignment: .trailing)
                        Rectangle().fill(ThemeColors.deviceA.opacity(0.9))
                            .frame(width: geo.size.width * CGFloat(split.mac) / total)
                            .blendMode(.normal)
                    }
                }
                .frame(width: 300, height: 8)
                .clipShape(Capsule())
                .padding(.top, 18)

                HStack {
                    HStack(spacing: 5) { DeviceMarker(isLocal: true, size: 7); Text("Mac \(Formatters.hours(split.mac))") }
                    Spacer()
                    Text("\(Formatters.hours(result.onBothSeconds)) on both")
                    Spacer()
                    HStack(spacing: 5) {
                        RoundedRectangle(cornerRadius: 2).fill(ThemeColors.deviceB).frame(width: 7, height: 7)
                        Text("PC \(Formatters.hours(split.pc))")
                    }
                }
                .font(.system(size: 10))
                .foregroundColor(ThemeColors.textMuted)
                .frame(width: 300)
                .padding(.top, 5)
            }

            Spacer(minLength: 8)

            HStack(spacing: 8) {
                statTile("\(result.daysTracked)", result.trackingStarted.map { "days tracked since \(Formatters.dayText($0, format: "d MMM"))" } ?? "days tracked")
                statTile("\(result.daysWithActivity)", "days with an app open")
                statTile("\(result.appsTracked)", "apps tracked")
            }
        }
    }

    private func statTile(_ value: String, _ label: String) -> some View {
        VStack(alignment: .leading, spacing: 0) {
            Text(value)
                .font(.system(size: 18, weight: .bold))
                .foregroundColor(ThemeColors.textPrimary)
            Text(label)
                .font(.system(size: 10))
                .lineSpacing(1)
                .foregroundColor(ThemeColors.textMuted)
                .fixedSize(horizontal: false, vertical: true)
                .frame(height: 26, alignment: .top)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.horizontal, 10)
        .padding(.vertical, 8)
        .overlay(RoundedRectangle(cornerRadius: 10).strokeBorder(ThemeColors.border, lineWidth: 1))
    }

    private func headline(_ text: String) -> some View {
        Text(text)
            .font(.system(size: 20, weight: .bold))
            .lineSpacing(2)
            .foregroundColor(ThemeColors.textPrimary)
            .frame(maxWidth: 310, alignment: .leading)
            .padding(.top, 8)
    }

    private func podiumCard(eyebrow label: String, title: String, podium: [WrappedApp], others: [WrappedApp]) -> some View {
        let top = CGFloat(max(1, podium.first?.seconds ?? 1))
        return VStack(alignment: .leading, spacing: 0) {
            eyebrow(label)
            headline(title)
            Spacer(minLength: 8)
            if !podium.isEmpty {
                HStack(alignment: .bottom, spacing: 8) {
                    slot(podium, rank: 2, top: top, tall: 170, fontSize: 26, inset: 10)
                    slot(podium, rank: 1, top: top, tall: 170, fontSize: 30, inset: 12)
                    slot(podium, rank: 3, top: top, tall: 170, fontSize: 24, inset: 8)
                }
                if !others.isEmpty {
                    (Text("Then ") + others.prefix(3).enumerated().reduce(Text("")) { acc, item in
                        acc + Text(item.offset > 0 ? " · " : "") + Text("\(item.element.name) \(h(item.element.seconds))").foregroundColor(ThemeColors.textPrimary)
                    })
                    .font(.system(size: 10))
                    .foregroundColor(ThemeColors.textMuted)
                    .lineLimit(1)
                    .padding(.top, 12)
                }
            }
        }
    }

    @ViewBuilder
    private func slot(_ podium: [WrappedApp], rank: Int, top: CGFloat, tall: CGFloat, fontSize: CGFloat, inset: CGFloat) -> some View {
        if rank <= podium.count {
            let app = podium[rank - 1]
            VStack(spacing: 0) {
                AppTile(name: app.name, png: state.iconCache.getCachedIcon(forAppId: app.appId), size: 34, radius: 9, fontSize: 15, border: ThemeColors.border)
                Text(app.name)
                    .font(.system(size: 12, weight: .bold))
                    .foregroundColor(ThemeColors.textPrimary)
                    .lineLimit(1)
                    .padding(.top, 6)
                Text(h(app.seconds))
                    .font(.system(size: 10))
                    .foregroundColor(ThemeColors.textDim)
                    .padding(.bottom, 6)
                Text("\(rank)")
                    .font(.system(size: fontSize, weight: .heavy))
                    .foregroundColor(ThemeColors.background)
                    .padding(.top, inset)
                    .frame(maxWidth: .infinity, alignment: .top)
                    .frame(height: max(56, tall * CGFloat(app.seconds) / top), alignment: .top)
                    .background(UnevenRoundedRectangle(topLeadingRadius: 8, topTrailingRadius: 8).fill(Tokens.appColor(app.name)))
            }
            .frame(maxWidth: .infinity)
        } else {
            Color.clear.frame(maxWidth: .infinity, maxHeight: 1)
        }
    }

    private var habitsTitle: String {
        let summer = Formatters.dayText(result.streakStart, format: "M")
        return ["6", "7", "8"].contains(summer) && result.longestStreakDays >= 7 ? "Consistent, with a long summer run." : "Consistent, with a long run."
    }

    private var busiestDelta: String {
        guard let m = result.busiestMonth,
              let month = state.yearResult?.months.first(where: { $0.month == m }),
              let prev = month.previousSeconds, prev > 0 else { return "your biggest month" }
        let pct = Int((Double(month.seconds - prev) / Double(prev) * 100).rounded())
        return "\(pct >= 0 ? "+" : "")\(pct)% vs \(Formatters.monthName(m == 1 ? 12 : m - 1, short: true))"
    }

    private var peakText: String {
        guard let hour = result.peakHour else { return "—" }
        func label(_ h: Int) -> (String, String) {
            let v = h % 12 == 0 ? 12 : h % 12
            return ("\(v)", h % 24 < 12 ? "am" : "pm")
        }
        let a = label(hour), b = label(hour + 1)
        return a.1 == b.1 ? "\(a.0)–\(b.0) \(b.1)" : "\(a.0) \(a.1)–\(b.0) \(b.1)"
    }

    private var habitsCard: some View {
        VStack(alignment: .leading, spacing: 0) {
            eyebrow("YOUR HABITS")
            headline(habitsTitle)
            VStack(spacing: 10) {
                HStack(spacing: 10) {
                    habit("LONGEST STREAK", "\(result.longestStreakDays) day\(result.longestStreakDays == 1 ? "" : "s")", "\(Formatters.dayText(result.streakStart, format: "d MMM")) to \(Formatters.dayText(result.streakEnd, format: "d MMM"))")
                    habit("BUSIEST MONTH", result.busiestMonth.map { "\(Formatters.monthName($0, short: true)) · \(h(result.busiestMonthSeconds))" } ?? "—", busiestDelta)
                }
                HStack(spacing: 10) {
                    habit("PEAK HOUR", peakText, "most apps open together")
                    habit("BEFORE NOON", "\(result.beforeNoonPercent)%", result.beforeNoonPercent < 50 ? "of your time, a night owl" : "of your time, an early bird")
                }
            }
            .padding(.top, 16)
        }
    }

    private func habit(_ title: String, _ value: String, _ note: String) -> some View {
        VStack(alignment: .leading, spacing: 0) {
            Text(title).caps(9, tracking: 1.4, weight: .bold)
            Spacer(minLength: 4)
            Text(value)
                .font(.system(size: 26, weight: .heavy))
                .foregroundColor(ThemeColors.accent)
                .minimumScaleFactor(0.7)
            Text(note)
                .font(.system(size: 10))
                .foregroundColor(ThemeColors.textDim)
                .lineLimit(1)
                .padding(.top, 4)
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 12)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
        .overlay(RoundedRectangle(cornerRadius: 12).strokeBorder(ThemeColors.border, lineWidth: 1))
    }
}
