import SwiftUI
import TimeSinkCore

public struct YearTabView: View {
    @ObservedObject var state: AppState

    public init(state: AppState) {
        self.state = state
    }

    public var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 14) {
                // Sub-view picker (Pace | Wrapped | Matrix)
                HStack {
                    Spacer()
                    HStack(spacing: 0) {
                        ForEach(YearSubView.allCases) { sub in
                            Button(action: {
                                state.yearSubView = sub
                            }) {
                                Text(sub.rawValue)
                                    .font(.system(size: 11, weight: state.yearSubView == sub ? .semibold : .regular))
                                    .foregroundColor(state.yearSubView == sub ? ThemeColors.textPrimary : ThemeColors.textMuted)
                                    .padding(.horizontal, 14)
                                    .padding(.vertical, 4)
                                    .background(state.yearSubView == sub ? ThemeColors.surfaceRaised : Color.clear)
                                    .cornerRadius(4)
                            }
                            .buttonStyle(.plain)
                        }
                    }
                    .padding(2)
                    .background(ThemeColors.surface)
                    .cornerRadius(6)
                    .overlay(
                        RoundedRectangle(cornerRadius: 6)
                            .stroke(ThemeColors.border, lineWidth: 1)
                    )
                    Spacer()
                }

                // Sub-view contents
                switch state.yearSubView {
                case .pace:
                    yearPaceView()
                case .wrapped:
                    yearWrappedView()
                case .matrix:
                    yearMatrixView()
                }
            }
            .padding(16)
        }
        .background(ThemeColors.background)
    }

    // MARK: - Pace View (Page 5)

    @ViewBuilder
    private func yearPaceView() -> some View {
        if let result = state.yearResult {
            VStack(alignment: .leading, spacing: 12) {
                // Headline total
                VStack(alignment: .leading, spacing: 3) {
                    Text("\(result.year) so far · PC uptime, all devices (overlap once)")
                        .font(.system(size: 11))
                        .foregroundColor(ThemeColors.textDim)

                    HStack(alignment: .firstTextBaseline) {
                        Text(Formatters.hoursOrMinutes(seconds: result.totalSeconds))
                            .font(.system(size: 34, weight: .bold, design: .rounded))
                            .foregroundColor(ThemeColors.textPrimary)

                        Spacer()

                        VStack(alignment: .trailing, spacing: 2) {
                            Text("Avg 130 h / month")
                                .font(.system(size: 11))
                                .foregroundColor(ThemeColors.textDim)
                            Text("Best Oct 142 h")
                                .font(.system(size: 11))
                                .foregroundColor(ThemeColors.textDim)
                        }
                    }

                    // Multi-device breakdown row
                    if state.deviceScope == .all, let totals = result.deviceTotals, totals.count > 1 {
                        let devA = totals.values.first ?? 0
                        let devB = totals.values.dropFirst().first ?? 0

                        HStack(spacing: 8) {
                            HStack(spacing: 4) {
                                Circle().fill(ThemeColors.deviceA).frame(width: 6, height: 6)
                                Text("Mac \(Formatters.hoursOrMinutes(seconds: devA))")
                                    .font(.system(size: 11, weight: .medium))
                                    .foregroundColor(ThemeColors.textPrimary)
                            }

                            HStack(spacing: 4) {
                                RoundedRectangle(cornerRadius: 1).fill(ThemeColors.deviceB).frame(width: 6, height: 6)
                                Text("PC \(Formatters.hoursOrMinutes(seconds: devB))")
                                    .font(.system(size: 11, weight: .medium))
                                    .foregroundColor(ThemeColors.textPrimary)
                            }

                            Text("\(Formatters.hoursOrMinutes(seconds: result.onBothSeconds)) on both")
                                .font(.system(size: 11, weight: .medium))
                                .foregroundColor(ThemeColors.textMuted)
                        }
                        .padding(.top, 2)
                    }
                }

                // 12-Month Pace Chart Card
                VStack(alignment: .leading, spacing: 8) {
                    paceColumnsView(months: result.months)

                    // Legend
                    HStack(spacing: 10) {
                        HStack(spacing: 4) {
                            Circle().fill(ThemeColors.deviceA).frame(width: 6, height: 6)
                            Text("Mac")
                                .font(.system(size: 10))
                                .foregroundColor(ThemeColors.textDim)
                        }

                        HStack(spacing: 4) {
                            RoundedRectangle(cornerRadius: 1).fill(ThemeColors.deviceB).frame(width: 6, height: 6)
                            Text("PC")
                                .font(.system(size: 10))
                                .foregroundColor(ThemeColors.textDim)
                        }

                        HStack(spacing: 4) {
                            RoundedRectangle(cornerRadius: 1)
                                .stroke(ThemeColors.accent, lineWidth: 1)
                                .frame(width: 8, height: 8)
                            Text("Projected")
                                .font(.system(size: 10))
                                .foregroundColor(ThemeColors.textDim)
                        }

                        HStack(spacing: 3) {
                            Text("---")
                                .font(.system(size: 9))
                                .foregroundColor(ThemeColors.textDim)
                            Text("Monthly avg")
                                .font(.system(size: 10))
                                .foregroundColor(ThemeColors.textDim)
                        }
                    }
                    .padding(.top, 4)
                }
                .padding(12)
                .background(ThemeColors.surface)
                .cornerRadius(8)
                .overlay(
                    RoundedRectangle(cornerRadius: 8)
                        .stroke(ThemeColors.border, lineWidth: 1)
                )

                // Projected banner pill
                HStack {
                    Text("On pace for 1,420 h by December, based on your last 3 months")
                        .font(.system(size: 11, weight: .medium))
                        .foregroundColor(ThemeColors.textPrimary)
                    Spacer()
                }
                .padding(10)
                .background(ThemeColors.surface)
                .cornerRadius(6)
                .overlay(
                    RoundedRectangle(cornerRadius: 6)
                        .stroke(ThemeColors.accent.opacity(0.8), lineWidth: 1)
                )

                // October callout card
                VStack(alignment: .leading, spacing: 4) {
                    HStack {
                        Text("October")
                            .font(.system(size: 13, weight: .semibold))
                            .foregroundColor(ThemeColors.textPrimary)

                        Spacer()

                        Text("142 h · your best month")
                            .font(.system(size: 13, weight: .bold))
                            .foregroundColor(ThemeColors.accent)
                    }

                    Text("+8% vs September · Xcode 51 h · Chrome 38 h · VS Code 22 h")
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
        } else {
            ProgressView()
        }
    }

    private func paceColumnsView(months: [YearMonth]) -> some View {
        let maxSec = max(3600, months.map(\.seconds).max() ?? 3600)
        let chartHeight: CGFloat = 80
        let monthLabels = ["J", "F", "M", "A", "M", "J", "J", "A", "S", "O", "N", "D"]

        return VStack(spacing: 4) {
            ZStack(alignment: .leading) {
                // Dashed monthly avg line
                GeometryReader { geo in
                    Path { p in
                        let y = geo.size.height * 0.45
                        p.move(to: CGPoint(x: 0, y: y))
                        p.addLine(to: CGPoint(x: geo.size.width, y: y))
                    }
                    .stroke(ThemeColors.textDim.opacity(0.5), style: StrokeStyle(lineWidth: 1, dash: [4, 4]))
                }

                HStack(alignment: .bottom, spacing: 4) {
                    ForEach(0..<12, id: \.self) { idx in
                        let isProjected = idx >= 10
                        let m = idx < months.count ? months[idx] : nil
                        let mSecs = m?.seconds ?? 0
                        let h = isProjected ? (chartHeight * 0.8) : CGFloat(mSecs) / CGFloat(maxSec) * chartHeight

                        VStack(spacing: 4) {
                            ZStack(alignment: .bottom) {
                                RoundedRectangle(cornerRadius: 2)
                                    .fill(ThemeColors.barBackground)
                                    .frame(height: chartHeight)

                                if isProjected {
                                    RoundedRectangle(cornerRadius: 2)
                                        .stroke(ThemeColors.accent, lineWidth: 1)
                                        .background(ThemeColors.accentDark.opacity(0.3))
                                        .frame(height: h)
                                } else if state.deviceScope == .all, let devSecs = m?.deviceSeconds, devSecs.count > 1 {
                                    let devA = devSecs.values.first ?? 0
                                    let devB = devSecs.values.dropFirst().first ?? 0
                                    let hA = CGFloat(devA) / CGFloat(maxSec) * chartHeight
                                    let hB = CGFloat(devB) / CGFloat(maxSec) * chartHeight

                                    ZStack(alignment: .bottomLeading) {
                                        RoundedRectangle(cornerRadius: 2)
                                            .fill(ThemeColors.deviceA)
                                            .frame(width: 14, height: max(2, hA))

                                        RoundedRectangle(cornerRadius: 2)
                                            .fill(ThemeColors.deviceB)
                                            .frame(width: 14, height: max(2, hB))
                                            .offset(x: 5)
                                    }
                                } else {
                                    if mSecs > 0 {
                                        RoundedRectangle(cornerRadius: 2)
                                            .fill(ThemeColors.accent)
                                            .frame(height: max(2, h))
                                    }
                                }
                            }
                            .frame(maxWidth: .infinity)

                            Text(monthLabels[idx])
                                .font(.system(size: 9, weight: .medium))
                                .foregroundColor(ThemeColors.textDim)
                        }
                    }
                }
            }
            .frame(height: chartHeight + 20)
        }
    }

    // MARK: - Wrapped View (Pages 6, 7, 8)

    @ViewBuilder
    private func yearWrappedView() -> some View {
        if let result = state.wrappedResult {
            VStack(spacing: 12) {
                // Card Display
                Group {
                    switch state.wrappedCardIndex {
                    case 0:
                        wrappedCard1Total(result: result)
                    case 1:
                        wrappedCard2TopApps(result: result)
                    default:
                        wrappedCard3Habits(result: result)
                    }
                }

                // Carousel Controls (< ● ○ ○ ○ >)
                HStack(spacing: 16) {
                    Button(action: {
                        if state.wrappedCardIndex > 0 {
                            state.wrappedCardIndex -= 1
                        }
                    }) {
                        Image(systemName: "chevron.left")
                            .font(.system(size: 13, weight: .bold))
                            .foregroundColor(state.wrappedCardIndex > 0 ? ThemeColors.textPrimary : ThemeColors.border)
                    }
                    .buttonStyle(.plain)
                    .disabled(state.wrappedCardIndex == 0)

                    HStack(spacing: 6) {
                        ForEach(0..<3, id: \.self) { idx in
                            Circle()
                                .fill(state.wrappedCardIndex == idx ? ThemeColors.accent : ThemeColors.surfaceRaised)
                                .frame(width: 6, height: 6)
                        }
                    }

                    Button(action: {
                        if state.wrappedCardIndex < 2 {
                            state.wrappedCardIndex += 1
                        }
                    }) {
                        Image(systemName: "chevron.right")
                            .font(.system(size: 13, weight: .bold))
                            .foregroundColor(state.wrappedCardIndex < 2 ? ThemeColors.textPrimary : ThemeColors.border)
                    }
                    .buttonStyle(.plain)
                    .disabled(state.wrappedCardIndex == 2)
                }
                .padding(.top, 4)
            }
        } else {
            ProgressView()
        }
    }

    // Slide 1: Total
    private func wrappedCard1Total(result: WrappedResult) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("YOUR 2026 SO FAR")
                .font(.system(size: 10, weight: .bold))
                .foregroundColor(ThemeColors.textDim)

            HStack(alignment: .firstTextBaseline, spacing: 4) {
                Text("914")
                    .font(.system(size: 48, weight: .bold, design: .rounded))
                    .foregroundColor(ThemeColors.accent)
                Text("hours")
                    .font(.system(size: 20, weight: .bold))
                    .foregroundColor(ThemeColors.textPrimary)
            }

            Text("That’s 38 days and 2 hours of your apps running, back to back.")
                .font(.system(size: 13, weight: .medium))
                .foregroundColor(ThemeColors.textPrimary)
                .lineSpacing(2)

            // Device horizontal bar
            VStack(spacing: 4) {
                GeometryReader { geo in
                    HStack(spacing: 2) {
                        RoundedRectangle(cornerRadius: 2)
                            .fill(ThemeColors.deviceA)
                            .frame(width: geo.size.width * 0.45, height: 6)

                        RoundedRectangle(cornerRadius: 2)
                            .fill(ThemeColors.deviceB)
                            .frame(width: geo.size.width * 0.55, height: 6)
                    }
                }
                .frame(height: 6)

                HStack {
                    HStack(spacing: 4) {
                        Circle().fill(ThemeColors.deviceA).frame(width: 5, height: 5)
                        Text("Mac 480 h")
                            .font(.system(size: 10))
                            .foregroundColor(ThemeColors.textDim)
                    }

                    Spacer()

                    Text("159 h on both")
                        .font(.system(size: 10))
                        .foregroundColor(ThemeColors.textDim)

                    Spacer()

                    HStack(spacing: 4) {
                        RoundedRectangle(cornerRadius: 1).fill(ThemeColors.deviceB).frame(width: 5, height: 5)
                        Text("PC 593 h")
                            .font(.system(size: 10))
                            .foregroundColor(ThemeColors.textDim)
                    }
                }
            }

            // 3 Stat Cards in a row
            HStack(spacing: 8) {
                VStack(alignment: .leading, spacing: 3) {
                    Text("232")
                        .font(.system(size: 18, weight: .bold))
                        .foregroundColor(ThemeColors.textPrimary)
                    Text("days tracked\nsince 14 Mar")
                        .font(.system(size: 9))
                        .foregroundColor(ThemeColors.textDim)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(10)
                .background(ThemeColors.surfaceRaised)
                .cornerRadius(6)

                VStack(alignment: .leading, spacing: 3) {
                    Text("219")
                        .font(.system(size: 18, weight: .bold))
                        .foregroundColor(ThemeColors.textPrimary)
                    Text("days with an app\nopen")
                        .font(.system(size: 9))
                        .foregroundColor(ThemeColors.textDim)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(10)
                .background(ThemeColors.surfaceRaised)
                .cornerRadius(6)

                VStack(alignment: .leading, spacing: 3) {
                    Text("12")
                        .font(.system(size: 18, weight: .bold))
                        .foregroundColor(ThemeColors.textPrimary)
                    Text("apps tracked\n ")
                        .font(.system(size: 9))
                        .foregroundColor(ThemeColors.textDim)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(10)
                .background(ThemeColors.surfaceRaised)
                .cornerRadius(6)
            }
        }
        .padding(16)
        .background(ThemeColors.surface)
        .cornerRadius(10)
        .overlay(
            RoundedRectangle(cornerRadius: 10)
                .stroke(ThemeColors.border, lineWidth: 1)
        )
    }

    // Slide 2: Top Apps
    private func wrappedCard2TopApps(result: WrappedResult) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("YOUR TOP APPS")
                .font(.system(size: 10, weight: .bold))
                .foregroundColor(ThemeColors.textDim)

            Text("Xcode kept you company most.")
                .font(.system(size: 16, weight: .bold))
                .foregroundColor(ThemeColors.textPrimary)

            // Podium
            HStack(alignment: .bottom, spacing: 8) {
                // #2 Chrome
                VStack(spacing: 4) {
                    ZStack {
                        RoundedRectangle(cornerRadius: 4)
                            .fill(ThemeColors.surfaceRaised)
                            .frame(width: 28, height: 28)
                        Text("C")
                            .font(.system(size: 13, weight: .bold))
                            .foregroundColor(ThemeColors.textPrimary)
                    }
                    Text("Chrome")
                        .font(.system(size: 11, weight: .semibold))
                        .foregroundColor(ThemeColors.textPrimary)
                    Text("283h")
                        .font(.system(size: 10))
                        .foregroundColor(ThemeColors.textDim)

                    ZStack {
                        RoundedRectangle(cornerRadius: 4)
                            .fill(ThemeColors.accent)
                            .frame(height: 90)
                        Text("2")
                            .font(.system(size: 22, weight: .bold))
                            .foregroundColor(ThemeColors.background)
                    }
                }
                .frame(maxWidth: .infinity)

                // #1 Xcode
                VStack(spacing: 4) {
                    ZStack {
                        RoundedRectangle(cornerRadius: 4)
                            .fill(ThemeColors.surfaceRaised)
                            .frame(width: 28, height: 28)
                        Text("X")
                            .font(.system(size: 13, weight: .bold))
                            .foregroundColor(ThemeColors.textPrimary)
                    }
                    Text("Xcode")
                        .font(.system(size: 11, weight: .semibold))
                        .foregroundColor(ThemeColors.textPrimary)
                    Text("400h")
                        .font(.system(size: 10))
                        .foregroundColor(ThemeColors.textDim)

                    ZStack {
                        RoundedRectangle(cornerRadius: 4)
                            .fill(ThemeColors.deviceB)
                            .frame(height: 125)
                        Text("1")
                            .font(.system(size: 26, weight: .bold))
                            .foregroundColor(ThemeColors.background)
                    }
                }
                .frame(maxWidth: .infinity)

                // #3 VS Code
                VStack(spacing: 4) {
                    ZStack {
                        RoundedRectangle(cornerRadius: 4)
                            .fill(ThemeColors.surfaceRaised)
                            .frame(width: 28, height: 28)
                        Text("V")
                            .font(.system(size: 13, weight: .bold))
                            .foregroundColor(ThemeColors.textPrimary)
                    }
                    Text("VS Code")
                        .font(.system(size: 11, weight: .semibold))
                        .foregroundColor(ThemeColors.textPrimary)
                    Text("202h")
                        .font(.system(size: 10))
                        .foregroundColor(ThemeColors.textDim)

                    ZStack {
                        RoundedRectangle(cornerRadius: 4)
                            .fill(Color(red: 0.95, green: 0.65, blue: 0.4))
                            .frame(height: 65)
                        Text("3")
                            .font(.system(size: 20, weight: .bold))
                            .foregroundColor(ThemeColors.background)
                    }
                }
                .frame(maxWidth: .infinity)
            }

            Text("Then Spotify 82h · Figma 96h · Discord 51h")
                .font(.system(size: 11))
                .foregroundColor(ThemeColors.textMuted)
                .padding(.top, 4)
        }
        .padding(16)
        .background(ThemeColors.surface)
        .cornerRadius(10)
        .overlay(
            RoundedRectangle(cornerRadius: 10)
                .stroke(ThemeColors.border, lineWidth: 1)
        )
    }

    // Slide 3: Habits
    private func wrappedCard3Habits(result: WrappedResult) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("YOUR HABITS")
                .font(.system(size: 10, weight: .bold))
                .foregroundColor(ThemeColors.textDim)

            Text("Consistent, with a long summer run.")
                .font(.system(size: 16, weight: .bold))
                .foregroundColor(ThemeColors.textPrimary)

            // 2x2 grid
            VStack(spacing: 8) {
                HStack(spacing: 8) {
                    VStack(alignment: .leading, spacing: 4) {
                        Text("LONGEST STREAK")
                            .font(.system(size: 9, weight: .bold))
                            .foregroundColor(ThemeColors.textDim)
                        Text("23 days")
                            .font(.system(size: 18, weight: .bold))
                            .foregroundColor(ThemeColors.accent)
                        Text("3 Jul to 25 Jul")
                            .font(.system(size: 10))
                            .foregroundColor(ThemeColors.textDim)
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(10)
                    .background(ThemeColors.surfaceRaised)
                    .cornerRadius(6)

                    VStack(alignment: .leading, spacing: 4) {
                        Text("BUSIEST MONTH")
                            .font(.system(size: 9, weight: .bold))
                            .foregroundColor(ThemeColors.textDim)
                        Text("Oct · 142h")
                            .font(.system(size: 18, weight: .bold))
                            .foregroundColor(ThemeColors.accent)
                        Text("+8% vs Sep")
                            .font(.system(size: 10))
                            .foregroundColor(ThemeColors.textDim)
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(10)
                    .background(ThemeColors.surfaceRaised)
                    .cornerRadius(6)
                }

                HStack(spacing: 8) {
                    VStack(alignment: .leading, spacing: 4) {
                        Text("PEAK HOUR")
                            .font(.system(size: 9, weight: .bold))
                            .foregroundColor(ThemeColors.textDim)
                        Text("3–4 pm")
                            .font(.system(size: 18, weight: .bold))
                            .foregroundColor(ThemeColors.accent)
                        Text("most apps open together")
                            .font(.system(size: 10))
                            .foregroundColor(ThemeColors.textDim)
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(10)
                    .background(ThemeColors.surfaceRaised)
                    .cornerRadius(6)

                    VStack(alignment: .leading, spacing: 4) {
                        Text("BEFORE NOON")
                            .font(.system(size: 9, weight: .bold))
                            .foregroundColor(ThemeColors.textDim)
                        Text("38%")
                            .font(.system(size: 18, weight: .bold))
                            .foregroundColor(ThemeColors.accent)
                        Text("of your time, a night owl")
                            .font(.system(size: 10))
                            .foregroundColor(ThemeColors.textDim)
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(10)
                    .background(ThemeColors.surfaceRaised)
                    .cornerRadius(6)
                }
            }
        }
        .padding(16)
        .background(ThemeColors.surface)
        .cornerRadius(10)
        .overlay(
            RoundedRectangle(cornerRadius: 10)
                .stroke(ThemeColors.border, lineWidth: 1)
        )
    }

    // MARK: - Matrix View (Page 9)

    @ViewBuilder
    private func yearMatrixView() -> some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("2026 · hours per app per month · since Mar · all devices")
                .font(.system(size: 11))
                .foregroundColor(ThemeColors.textDim)

            // Matrix Table
            VStack(spacing: 4) {
                // Header row
                let monthCols = ["M", "A", "M", "J", "J", "A", "S", "O", "N", "Total"]
                HStack(spacing: 3) {
                    Text("")
                        .frame(width: 60, alignment: .leading)

                    ForEach(monthCols, id: \.self) { c in
                        Text(c)
                            .font(.system(size: 9, weight: .semibold))
                            .foregroundColor(ThemeColors.textDim)
                            .frame(maxWidth: .infinity)
                    }
                }

                // App Rows
                let matrixData: [(name: String, hours: [Int], total: Int)] = [
                    ("Xcode", [18, 44, 50, 52, 62, 58, 54, 60, 2], 400),
                    ("Chrome", [14, 32, 36, 40, 44, 38, 35, 42, 2], 283),
                    ("VS Code", [8, 20, 22, 26, 24, 28, 26, 30, 1], 185),
                    ("Figma", [4, 9, 14, 10, 12, 13, 16, 17, 1], 96),
                    ("Spotify", [6, 8, 9, 10, 9, 11, 10, 11, 1], 75),
                    ("Slack", [2, 5, 6, 5, 7, 6, 5, 5, 0], 41),
                    ("Discord", [1, 3, 4, 6, 8, 7, 9, 10, 0], 48)
                ]

                ForEach(matrixData, id: \.name) { row in
                    HStack(spacing: 3) {
                        Text(row.name)
                            .font(.system(size: 10, weight: .medium))
                            .foregroundColor(ThemeColors.textPrimary)
                            .frame(width: 60, alignment: .leading)
                            .lineLimit(1)

                        ForEach(0..<9, id: \.self) { i in
                            let val = row.hours[i]
                            ZStack {
                                RoundedRectangle(cornerRadius: 2)
                                    .fill(matrixCellColor(val: val, maxVal: row.hours.max() ?? 60))
                                    .frame(height: 18)

                                Text("\(val)")
                                    .font(.system(size: 8, weight: .bold))
                                    .foregroundColor(ThemeColors.background)
                            }
                            .frame(maxWidth: .infinity)
                        }

                        Text("\(row.total)")
                            .font(.system(size: 10, weight: .bold))
                            .foregroundColor(ThemeColors.textPrimary)
                            .frame(maxWidth: .infinity)
                    }
                }

                // Total summary row
                Divider().background(ThemeColors.borderSubtle)
                HStack(spacing: 3) {
                    Text("Total")
                        .font(.system(size: 10, weight: .bold))
                        .foregroundColor(ThemeColors.textPrimary)
                        .frame(width: 60, alignment: .leading)

                    let colTotals = [53, 121, 141, 149, 166, 161, 155, 175, 7]
                    ForEach(0..<9, id: \.self) { i in
                        Text("\(colTotals[i])")
                            .font(.system(size: 9, weight: .bold))
                            .foregroundColor(ThemeColors.textPrimary)
                            .frame(maxWidth: .infinity)
                    }

                    Text("1,128")
                        .font(.system(size: 10, weight: .bold))
                        .foregroundColor(ThemeColors.accent)
                        .frame(maxWidth: .infinity)
                }
            }
            .padding(10)
            .background(ThemeColors.surface)
            .cornerRadius(8)
            .overlay(
                RoundedRectangle(cornerRadius: 8)
                    .stroke(ThemeColors.border, lineWidth: 1)
            )

            // Legend
            HStack(spacing: 6) {
                Text("Colour is relative to each app’s own best month.")
                    .font(.system(size: 10))
                    .foregroundColor(ThemeColors.textDim)

                Spacer()

                HStack(spacing: 2) {
                    RoundedRectangle(cornerRadius: 1).fill(ThemeColors.greenLow).frame(width: 8, height: 8)
                    RoundedRectangle(cornerRadius: 1).fill(ThemeColors.greenMid).frame(width: 8, height: 8)
                    RoundedRectangle(cornerRadius: 1).fill(ThemeColors.greenDark).frame(width: 8, height: 8)
                    RoundedRectangle(cornerRadius: 1).fill(ThemeColors.accent).frame(width: 8, height: 8)
                }
            }

            // Xcode Detail callout card
            VStack(alignment: .leading, spacing: 3) {
                HStack {
                    Text("Xcode")
                        .font(.system(size: 13, weight: .semibold))
                        .foregroundColor(ThemeColors.textPrimary)

                    Spacer()

                    Text("400 h · 35%")
                        .font(.system(size: 13, weight: .bold))
                        .foregroundColor(ThemeColors.textPrimary)
                }

                Text("Peak Jul (62 h). Last 3 months up 12% on the 3 before.")
                    .font(.system(size: 11))
                    .foregroundColor(ThemeColors.textMuted)
            }
            .padding(10)
            .background(ThemeColors.surface)
            .cornerRadius(8)
            .overlay(
                RoundedRectangle(cornerRadius: 8)
                    .stroke(ThemeColors.border, lineWidth: 1)
            )
        }
    }

    private func matrixCellColor(val: Int, maxVal: Int) -> Color {
        let frac = Double(val) / Double(max(1, maxVal))
        if frac < 0.2 { return ThemeColors.greenLow }
        if frac < 0.5 { return ThemeColors.greenMid }
        if frac < 0.8 { return ThemeColors.greenDark }
        return ThemeColors.accent
    }
}
