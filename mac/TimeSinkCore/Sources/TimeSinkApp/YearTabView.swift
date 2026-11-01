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
                // Year navigator & segmented switch
                HStack {
                    Button(action: {
                        state.yearOffset += 1
                        state.refresh()
                    }) {
                        Image(systemName: "chevron.left")
                            .font(.system(size: 11, weight: .bold))
                            .foregroundColor(ThemeColors.textDim)
                            .frame(width: 20, height: 20)
                    }
                    .buttonStyle(.plain)

                    let currentYear = Calendar.current.component(.year, from: Date()) - state.yearOffset
                    Text(String(currentYear))
                        .font(.system(size: 13, weight: .bold))
                        .foregroundColor(ThemeColors.textPrimary)

                    Button(action: {
                        if state.yearOffset > 0 {
                            state.yearOffset -= 1
                            state.refresh()
                        }
                    }) {
                        Image(systemName: "chevron.right")
                            .font(.system(size: 11, weight: .bold))
                            .foregroundColor(state.yearOffset > 0 ? ThemeColors.textDim : ThemeColors.border)
                            .frame(width: 20, height: 20)
                    }
                    .buttonStyle(.plain)
                    .disabled(state.yearOffset == 0)

                    Spacer()

                    // Sub-view picker (Pace | Wrapped | Matrix)
                    HStack(spacing: 0) {
                        ForEach(YearSubView.allCases) { sub in
                            Button(action: {
                                state.yearSubView = sub
                            }) {
                                Text(sub.rawValue)
                                    .font(.system(size: 11, weight: state.yearSubView == sub ? .semibold : .regular))
                                    .foregroundColor(state.yearSubView == sub ? ThemeColors.textPrimary : ThemeColors.textMuted)
                                    .padding(.horizontal, 8)
                                    .padding(.vertical, 3)
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

    // MARK: - Pace View

    @ViewBuilder
    private func yearPaceView() -> some View {
        if let result = state.yearResult {
            VStack(alignment: .leading, spacing: 12) {
                // Headline total
                VStack(alignment: .leading, spacing: 4) {
                    Text(state.deviceScope == .all ? "\(result.year) so far · all devices" : "\(result.year) so far · \(state.localDeviceName)")
                        .font(.system(size: 12, weight: .medium))
                        .foregroundColor(ThemeColors.textDim)

                    HStack(alignment: .firstTextBaseline, spacing: 10) {
                        Text(Formatters.hoursMinutes(seconds: result.totalSeconds))
                            .font(.system(size: 28, weight: .bold, design: .rounded))
                            .foregroundColor(ThemeColors.textPrimary)

                        if let pace = result.paceYearSeconds {
                            Text("Pace: \(Formatters.hoursOrMinutes(seconds: pace))")
                                .font(.system(size: 12, weight: .medium))
                                .foregroundColor(ThemeColors.accent)
                                .padding(.horizontal, 6)
                                .padding(.vertical, 2)
                                .background(ThemeColors.surfaceRaised)
                                .cornerRadius(5)
                        }
                    }

                    // Device summary row in All scope
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

                // 12-Month Pace Chart Card
                VStack(alignment: .leading, spacing: 8) {
                    HStack {
                        Text("MONTHLY UPTIME")
                            .font(.system(size: 10, weight: .semibold))
                            .foregroundColor(ThemeColors.textDim)

                        Spacer()

                        if let best = result.bestMonth {
                            let mName = DateFormatter().shortMonthSymbols[best - 1]
                            Text("Best: \(mName) (\(Formatters.hoursOrMinutes(seconds: result.bestMonthSeconds)))")
                                .font(.system(size: 10))
                                .foregroundColor(ThemeColors.textDim)
                        }
                    }

                    paceColumnsView(months: result.months)
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
        let monthNames = DateFormatter().veryShortMonthSymbols ?? []

        return VStack(spacing: 4) {
            HStack(alignment: .bottom, spacing: 4) {
                ForEach(months, id: \.month) { m in
                    let h = CGFloat(m.seconds) / CGFloat(maxSec) * chartHeight
                    VStack(spacing: 4) {
                        ZStack(alignment: .bottom) {
                            RoundedRectangle(cornerRadius: 2)
                                .fill(ThemeColors.barBackground)
                                .frame(height: chartHeight)

                            if state.deviceScope == .all, let devSecs = m.deviceSeconds, devSecs.count > 1 {
                                let devA = devSecs.values.first ?? 0
                                let devB = devSecs.values.dropFirst().first ?? 0
                                let hA = CGFloat(devA) / CGFloat(maxSec) * chartHeight
                                let hB = CGFloat(devB) / CGFloat(maxSec) * chartHeight

                                ZStack(alignment: .bottomLeading) {
                                    if hA >= hB {
                                        RoundedRectangle(cornerRadius: 2)
                                            .fill(ThemeColors.deviceA)
                                            .frame(width: 14, height: max(2, hA))

                                        RoundedRectangle(cornerRadius: 2)
                                            .fill(ThemeColors.deviceB)
                                            .frame(width: 14, height: max(2, hB))
                                            .offset(x: 5)
                                    } else {
                                        RoundedRectangle(cornerRadius: 2)
                                            .fill(ThemeColors.deviceB)
                                            .frame(width: 14, height: max(2, hB))
                                            .offset(x: 5)

                                        RoundedRectangle(cornerRadius: 2)
                                            .fill(ThemeColors.deviceA)
                                            .frame(width: 14, height: max(2, hA))
                                    }
                                }
                            } else {
                                if m.seconds > 0 {
                                    RoundedRectangle(cornerRadius: 2)
                                        .fill(m.kind == .current ? ThemeColors.accent : ThemeColors.accentDark)
                                        .frame(height: max(2, h))
                                }
                            }
                        }
                        .frame(maxWidth: .infinity)

                        Text(m.month <= monthNames.count ? monthNames[m.month - 1] : "\(m.month)")
                            .font(.system(size: 9, weight: m.kind == .current ? .bold : .regular))
                            .foregroundColor(m.kind == .current ? ThemeColors.accent : ThemeColors.textDim)
                    }
                }
            }
        }
    }

    // MARK: - Wrapped View

    @ViewBuilder
    private func yearWrappedView() -> some View {
        if let result = state.wrappedResult {
            VStack(alignment: .leading, spacing: 12) {
                // Card Switcher Tabs (Total, Top Apps, Habits)
                HStack(spacing: 8) {
                    wrappedTabButton(title: "1 · Total", index: 0)
                    wrappedTabButton(title: "2 · Top Apps", index: 1)
                    wrappedTabButton(title: "3 · Habits", index: 2)
                    Spacer()
                }

                // Selected Card
                switch state.wrappedCardIndex {
                case 0:
                    wrappedTotalCard(result: result)
                case 1:
                    wrappedTopAppsCard(result: result)
                default:
                    wrappedHabitsCard(result: result)
                }
            }
        } else {
            ProgressView()
        }
    }

    private func wrappedTabButton(title: String, index: Int) -> some View {
        Button(action: {
            state.wrappedCardIndex = index
        }) {
            Text(title)
                .font(.system(size: 11, weight: state.wrappedCardIndex == index ? .semibold : .regular))
                .foregroundColor(state.wrappedCardIndex == index ? ThemeColors.textPrimary : ThemeColors.textMuted)
                .padding(.horizontal, 10)
                .padding(.vertical, 4)
                .background(state.wrappedCardIndex == index ? ThemeColors.surfaceRaised : ThemeColors.surface)
                .cornerRadius(5)
        }
        .buttonStyle(.plain)
    }

    private func wrappedTotalCard(result: WrappedResult) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("YEAR IN REVIEW")
                .font(.system(size: 11, weight: .bold))
                .foregroundColor(ThemeColors.accent)

            VStack(alignment: .leading, spacing: 4) {
                Text("\(result.equivalentDays) days, \(result.equivalentHours) hours")
                    .font(.system(size: 26, weight: .bold, design: .rounded))
                    .foregroundColor(ThemeColors.textPrimary)

                Text("\(Formatters.hoursMinutes(seconds: result.totalSeconds)) active uptime in \(result.year)")
                    .font(.system(size: 13))
                    .foregroundColor(ThemeColors.textMuted)
            }

            // Overlapping dual-device bar in All Scope
            if state.deviceScope == .all, let totals = result.deviceTotals, totals.count > 1 {
                let devA = totals.values.first ?? 0
                let devB = totals.values.dropFirst().first ?? 0
                VStack(alignment: .leading, spacing: 6) {
                    HStack {
                        HStack(spacing: 4) {
                            DeviceMarker(isLocal: true, size: 7)
                            Text("\(state.localDeviceName): \(Formatters.hoursMinutes(seconds: devA))")
                                .font(.system(size: 11, weight: .medium))
                                .foregroundColor(ThemeColors.textMuted)
                        }
                        Spacer()
                        HStack(spacing: 4) {
                            DeviceMarker(isLocal: false, size: 7)
                            Text("\(state.remoteDeviceName ?? "PC"): \(Formatters.hoursMinutes(seconds: devB))")
                                .font(.system(size: 11, weight: .medium))
                                .foregroundColor(ThemeColors.textMuted)
                        }
                    }

                    // Dual colored bar
                    GeometryReader { geo in
                        let totalSum = max(1, devA + devB)
                        let fracA = CGFloat(devA) / CGFloat(totalSum)
                        let fracB = CGFloat(devB) / CGFloat(totalSum)

                        HStack(spacing: 2) {
                            RoundedRectangle(cornerRadius: 2)
                                .fill(ThemeColors.deviceA)
                                .frame(width: geo.size.width * fracA, height: 8)

                            RoundedRectangle(cornerRadius: 2)
                                .fill(ThemeColors.deviceB)
                                .frame(width: geo.size.width * fracB, height: 8)
                        }
                    }
                    .frame(height: 8)

                    if result.onBothSeconds > 0 {
                        Text("\(Formatters.hoursMinutes(seconds: result.onBothSeconds)) on both devices simultaneously")
                            .font(.system(size: 11))
                            .foregroundColor(ThemeColors.textDim)
                    }
                }
                .padding(10)
                .background(ThemeColors.surfaceRaised)
                .cornerRadius(6)
            }

            HStack(spacing: 20) {
                VStack(alignment: .leading, spacing: 2) {
                    Text("\(result.daysWithActivity)")
                        .font(.system(size: 16, weight: .bold))
                        .foregroundColor(ThemeColors.textPrimary)
                    Text("Days active")
                        .font(.system(size: 11))
                        .foregroundColor(ThemeColors.textDim)
                }

                VStack(alignment: .leading, spacing: 2) {
                    Text("\(result.appsTracked)")
                        .font(.system(size: 16, weight: .bold))
                        .foregroundColor(ThemeColors.textPrimary)
                    Text("Apps tracked")
                        .font(.system(size: 11))
                        .foregroundColor(ThemeColors.textDim)
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

    private func wrappedTopAppsCard(result: WrappedResult) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("MOST PLAYED APPS")
                .font(.system(size: 11, weight: .bold))
                .foregroundColor(ThemeColors.accent)

            // Podium (1st, 2nd, 3rd)
            HStack(alignment: .bottom, spacing: 8) {
                if result.podium.count > 1 {
                    podiumPillar(app: result.podium[1], rank: 2, height: 60)
                }
                if let first = result.podium.first {
                    podiumPillar(app: first, rank: 1, height: 80)
                }
                if result.podium.count > 2 {
                    podiumPillar(app: result.podium[2], rank: 3, height: 45)
                }
            }
            .frame(maxWidth: .infinity)
            .padding(.vertical, 8)

            // Other apps
            ForEach(result.others.prefix(4), id: \.appId) { app in
                HStack {
                    Text(app.name)
                        .font(.system(size: 12, weight: .medium))
                        .foregroundColor(ThemeColors.textPrimary)
                    Spacer()
                    Text(Formatters.hoursMinutes(seconds: app.seconds))
                        .font(.system(size: 12, weight: .medium, design: .monospaced))
                        .foregroundColor(ThemeColors.textMuted)
                }
                .padding(.vertical, 2)
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

    private func podiumPillar(app: WrappedApp, rank: Int, height: CGFloat) -> some View {
        VStack(spacing: 4) {
            Text(app.name)
                .font(.system(size: 11, weight: .semibold))
                .foregroundColor(ThemeColors.textPrimary)
                .lineLimit(1)

            Text(Formatters.hoursOrMinutes(seconds: app.seconds))
                .font(.system(size: 10))
                .foregroundColor(ThemeColors.textMuted)

            ZStack {
                RoundedRectangle(cornerRadius: 4)
                    .fill(rank == 1 ? ThemeColors.accent : ThemeColors.surfaceRaised)
                    .frame(height: height)

                Text("#\(rank)")
                    .font(.system(size: 13, weight: .bold))
                    .foregroundColor(rank == 1 ? ThemeColors.background : ThemeColors.textDim)
            }
        }
        .frame(maxWidth: .infinity)
    }

    private func wrappedHabitsCard(result: WrappedResult) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("YOUR HABITS")
                .font(.system(size: 11, weight: .bold))
                .foregroundColor(ThemeColors.accent)

            VStack(spacing: 10) {
                habitRow(title: "Longest streak", value: "\(result.longestStreakDays) days", icon: "flame.fill")

                if let bMonth = result.busiestMonth {
                    let mName = DateFormatter().monthSymbols[bMonth - 1]
                    habitRow(title: "Busiest month", value: "\(mName) (\(Formatters.hoursOrMinutes(seconds: result.busiestMonthSeconds)))", icon: "calendar")
                }

                if let pHour = result.peakHour {
                    habitRow(title: "Peak hour", value: String(format: "%02d:00 – %02d:00", pHour, (pHour + 1) % 24), icon: "clock.fill")
                }

                habitRow(title: "Time before noon", value: "\(result.beforeNoonPercent)%", icon: "sun.max.fill")
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

    private func habitRow(title: String, value: String, icon: String) -> some View {
        HStack {
            Image(systemName: icon)
                .font(.system(size: 13))
                .foregroundColor(ThemeColors.accent)
                .frame(width: 20)

            Text(title)
                .font(.system(size: 12))
                .foregroundColor(ThemeColors.textDim)

            Spacer()

            Text(value)
                .font(.system(size: 12, weight: .semibold))
                .foregroundColor(ThemeColors.textPrimary)
        }
        .padding(.vertical, 3)
    }

    // MARK: - Matrix View

    @ViewBuilder
    private func yearMatrixView() -> some View {
        if let result = state.matrixResult {
            VStack(alignment: .leading, spacing: 12) {
                Text("APPS ACTIVITY MATRIX")
                    .font(.system(size: 10, weight: .semibold))
                    .foregroundColor(ThemeColors.textDim)

                ForEach(result.rows, id: \.appId) { row in
                    VStack(alignment: .leading, spacing: 3) {
                        HStack {
                            Text(row.name)
                                .font(.system(size: 12, weight: .medium))
                                .foregroundColor(ThemeColors.textPrimary)
                            Spacer()
                            Text(Formatters.hoursOrMinutes(seconds: row.totalSeconds))
                                .font(.system(size: 11, design: .monospaced))
                                .foregroundColor(ThemeColors.textMuted)
                        }

                        // 12 month heat cells
                        HStack(spacing: 3) {
                            ForEach(Array(row.cells.enumerated()), id: \.offset) { _, cell in
                                RoundedRectangle(cornerRadius: 1.5)
                                    .fill(matrixHeatColor(level: cell.level))
                                    .frame(height: 12)
                            }
                        }
                    }
                    .padding(8)
                    .background(ThemeColors.surface)
                    .cornerRadius(6)
                }
            }
        } else {
            ProgressView()
        }
    }

    private func matrixHeatColor(level: Int) -> Color {
        switch level {
        case 0: return ThemeColors.barBackground
        case 1: return ThemeColors.accentDark.opacity(0.4)
        case 2: return ThemeColors.accentDark
        case 3: return ThemeColors.accent.opacity(0.8)
        default: return ThemeColors.accent
        }
    }
}
