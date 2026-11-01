import SwiftUI
import TimeSinkCore

public struct AppDetailView: View {
    @ObservedObject var state: AppState
    @State private var showingSettingsSheet: Bool = false
    @State private var selectedRange: String = "Year"

    public init(state: AppState) {
        self.state = state
    }

    public var body: some View {
        VStack(spacing: 0) {
            // Header
            HStack {
                Button(action: {
                    state.closeAppDetail()
                }) {
                    HStack(spacing: 4) {
                        Image(systemName: "chevron.left")
                            .font(.system(size: 11, weight: .bold))
                        Text("All")
                            .font(.system(size: 13, weight: .medium))
                    }
                    .foregroundColor(ThemeColors.accent)
                }
                .buttonStyle(.plain)

                Spacer()

                Text("App detail")
                    .font(.system(size: 14, weight: .semibold))
                    .foregroundColor(ThemeColors.textPrimary)

                Spacer()

                Button(action: {
                    showingSettingsSheet = true
                }) {
                    Text("Settings")
                        .font(.system(size: 13, weight: .medium))
                        .foregroundColor(ThemeColors.textMuted)
                }
                .buttonStyle(.plain)
            }
            .padding(.horizontal, 16)
            .padding(.vertical, 10)
            .background(ThemeColors.surface)
            .overlay(
                Rectangle()
                    .frame(height: 1)
                    .foregroundColor(ThemeColors.borderSubtle),
                alignment: .bottom
            )

            ScrollView {
                VStack(alignment: .leading, spacing: 14) {
                    if let result = state.appDetailResult {
                        // App hero card
                        HStack(spacing: 12) {
                            ZStack {
                                RoundedRectangle(cornerRadius: 8)
                                    .fill(ThemeColors.accent)
                                    .frame(width: 44, height: 44)

                                Text(String(result.name.prefix(1)).uppercased())
                                    .font(.system(size: 22, weight: .bold))
                                    .foregroundColor(ThemeColors.background)
                            }

                            VStack(alignment: .leading, spacing: 3) {
                                Text(result.name)
                                    .font(.system(size: 17, weight: .bold))
                                    .foregroundColor(ThemeColors.textPrimary)

                                Text("Merged ×2 · tracked since 14 Mar 2026")
                                    .font(.system(size: 11))
                                    .foregroundColor(ThemeColors.textDim)
                            }

                            Spacer()

                            VStack(alignment: .trailing, spacing: 2) {
                                Text(Formatters.hoursOrMinutes(seconds: result.totalSeconds))
                                    .font(.system(size: 26, weight: .bold, design: .rounded))
                                    .foregroundColor(ThemeColors.textPrimary)

                                Text("lifetime · 35% share")
                                    .font(.system(size: 10))
                                    .foregroundColor(ThemeColors.textDim)
                            }
                        }
                        .padding(.vertical, 4)

                        // 3 Key stats cards
                        HStack(spacing: 8) {
                            statCard(label: "Last opened", value: "Running now", valueColor: ThemeColors.accent)
                            statCard(label: "Average session", value: "1h 24m", valueColor: ThemeColors.textPrimary)
                            statCard(label: "Times opened", value: "202", valueColor: ThemeColors.textPrimary)
                        }

                        // Multi-device Split Bar
                        VStack(spacing: 6) {
                            HStack {
                                HStack(spacing: 4) {
                                    Circle().fill(ThemeColors.deviceA).frame(width: 6, height: 6)
                                    Text("Mac 124 h")
                                        .font(.system(size: 11, weight: .medium))
                                        .foregroundColor(ThemeColors.textPrimary)
                                }
                                Spacer()
                                HStack(spacing: 4) {
                                    Text("PC 159 h")
                                        .font(.system(size: 11, weight: .medium))
                                        .foregroundColor(ThemeColors.textPrimary)
                                    RoundedRectangle(cornerRadius: 1).fill(ThemeColors.deviceB).frame(width: 6, height: 6)
                                }
                            }

                            GeometryReader { geo in
                                HStack(spacing: 2) {
                                    RoundedRectangle(cornerRadius: 2)
                                        .fill(ThemeColors.deviceA)
                                        .frame(width: geo.size.width * 0.44, height: 6)

                                    RoundedRectangle(cornerRadius: 2)
                                        .fill(ThemeColors.deviceB)
                                        .frame(width: geo.size.width * 0.56, height: 6)
                                }
                            }
                            .frame(height: 6)
                        }
                        .padding(10)
                        .background(ThemeColors.surface)
                        .cornerRadius(6)

                        // Calendar Selector Row
                        HStack {
                            HStack(spacing: 6) {
                                Button(action: {}) {
                                    Image(systemName: "chevron.left")
                                        .font(.system(size: 10, weight: .bold))
                                        .foregroundColor(ThemeColors.textDim)
                                }
                                .buttonStyle(.plain)

                                Text("2026")
                                    .font(.system(size: 13, weight: .bold))
                                    .foregroundColor(ThemeColors.textPrimary)

                                Button(action: {}) {
                                    Image(systemName: "chevron.right")
                                        .font(.system(size: 10, weight: .bold))
                                        .foregroundColor(ThemeColors.textDim)
                                }
                                .buttonStyle(.plain)
                            }

                            Spacer()

                            // [ All | Year | Month | Week ]
                            HStack(spacing: 0) {
                                ForEach(["All", "Year", "Month", "Week"], id: \.self) { r in
                                    Button(action: {
                                        selectedRange = r
                                    }) {
                                        Text(r)
                                            .font(.system(size: 11, weight: selectedRange == r ? .semibold : .regular))
                                            .foregroundColor(selectedRange == r ? ThemeColors.textPrimary : ThemeColors.textMuted)
                                            .padding(.horizontal, 8)
                                            .padding(.vertical, 3)
                                            .background(selectedRange == r ? ThemeColors.surfaceRaised : Color.clear)
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

                        // 12-Month Calendar Heatmap (4 columns x 3 rows)
                        twelveMonthsHeatmapGrid()

                        // Legend
                        HStack(spacing: 6) {
                            Text("Quarters of this app’s busiest day (8h 40m)")
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
                    } else {
                        ProgressView()
                            .frame(maxWidth: .infinity, minHeight: 200)
                    }
                }
                .padding(16)
            }

            // Bottom Footer
            HStack {
                Text("App time, not uptime")
                    .font(.system(size: 11))
                    .foregroundColor(ThemeColors.textMuted)

                Spacer()

                Button(action: {
                    if let appId = state.selectedAppDetailId {
                        state.exportAppCsv(appId: appId)
                    }
                }) {
                    Text("Export app (CSV)")
                        .font(.system(size: 11))
                        .foregroundColor(ThemeColors.textMuted)
                }
                .buttonStyle(.plain)
            }
            .padding(.horizontal, 16)
            .padding(.vertical, 8)
            .background(ThemeColors.surface)
            .overlay(
                Rectangle()
                    .frame(height: 1)
                    .foregroundColor(ThemeColors.borderSubtle),
                alignment: .top
            )
        }
        .frame(width: 400, height: 640)
        .background(ThemeColors.background)
        .sheet(isPresented: $showingSettingsSheet) {
            if let appId = state.selectedAppDetailId {
                AppSettingsSheet(state: state, appId: appId) {
                    showingSettingsSheet = false
                }
            }
        }
    }

    private func statCard(label: String, value: String, valueColor: Color) -> some View {
        VStack(alignment: .leading, spacing: 3) {
            Text(label)
                .font(.system(size: 9))
                .foregroundColor(ThemeColors.textDim)

            Text(value)
                .font(.system(size: 14, weight: .bold))
                .foregroundColor(valueColor)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(10)
        .background(ThemeColors.surface)
        .cornerRadius(6)
        .overlay(
            RoundedRectangle(cornerRadius: 6)
                .stroke(ThemeColors.border, lineWidth: 1)
        )
    }

    private func twelveMonthsHeatmapGrid() -> some View {
        let monthNames = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"]

        return VStack(spacing: 10) {
            ForEach(0..<3, id: \.self) { row in
                HStack(spacing: 8) {
                    ForEach(0..<4, id: \.self) { col in
                        let mIdx = row * 4 + col
                        monthBlock(name: monthNames[mIdx], monthIndex: mIdx)
                    }
                }
            }
        }
    }

    private func monthBlock(name: String, monthIndex: Int) -> some View {
        VStack(alignment: .leading, spacing: 3) {
            Text(name)
                .font(.system(size: 9, weight: .medium))
                .foregroundColor(ThemeColors.textDim)

            // Mini 4x5 grid of cells
            VStack(spacing: 2) {
                ForEach(0..<4, id: \.self) { r in
                    HStack(spacing: 2) {
                        ForEach(0..<5, id: \.self) { c in
                            let active = (monthIndex >= 2 && monthIndex <= 9) && ((r + c + monthIndex) % 3 != 0)
                            let heat = active ? (1 + (r + c) % 4) : 0
                            RoundedRectangle(cornerRadius: 1)
                                .fill(heatColor(heat))
                                .frame(width: 8, height: 8)
                        }
                    }
                }
            }
        }
        .frame(maxWidth: .infinity)
    }

    private func heatColor(_ level: Int) -> Color {
        switch level {
        case 0: return ThemeColors.barBackground
        case 1: return ThemeColors.greenLow
        case 2: return ThemeColors.greenMid
        case 3: return ThemeColors.greenDark
        default: return ThemeColors.accent
        }
    }
}
