import SwiftUI
import TimeSinkCore

public struct AppDetailView: View {
    @ObservedObject var state: AppState

    public init(state: AppState) {
        self.state = state
    }

    public var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 14) {
                // Back button header
                HStack {
                    Button(action: {
                        state.closeAppDetail()
                    }) {
                        HStack(spacing: 4) {
                            Image(systemName: "chevron.left")
                                .font(.system(size: 11, weight: .bold))
                            Text("Back")
                                .font(.system(size: 13, weight: .medium))
                        }
                        .foregroundColor(ThemeColors.accent)
                    }
                    .buttonStyle(.plain)

                    Spacer()
                }

                if let result = state.appDetailResult {
                    // App title & icon card
                    HStack(spacing: 12) {
                        ZStack {
                            RoundedRectangle(cornerRadius: 8)
                                .fill(ThemeColors.surfaceRaised)
                                .frame(width: 44, height: 44)

                            if let png = state.iconCache.getCachedIcon(forAppId: result.appId),
                               let nsImg = NSImage(data: png) {
                                Image(nsImage: nsImg)
                                    .resizable()
                                    .frame(width: 34, height: 34)
                                    .cornerRadius(6)
                            } else {
                                Text(String(result.name.prefix(1)).uppercased())
                                    .font(.system(size: 20, weight: .bold))
                                    .foregroundColor(ThemeColors.accent)
                            }
                        }

                        VStack(alignment: .leading, spacing: 3) {
                            Text(result.name)
                                .font(.system(size: 16, weight: .bold))
                                .foregroundColor(ThemeColors.textPrimary)

                            if let first = result.firstTracked {
                                Text("Tracked since \(first)")
                                    .font(.system(size: 11))
                                    .foregroundColor(ThemeColors.textDim)
                            }
                        }

                        Spacer()

                        Text(Formatters.hoursMinutes(seconds: result.totalSeconds))
                            .font(.system(size: 22, weight: .bold, design: .rounded))
                            .foregroundColor(ThemeColors.textPrimary)
                    }
                    .padding(12)
                    .background(ThemeColors.surface)
                    .cornerRadius(8)
                    .overlay(
                        RoundedRectangle(cornerRadius: 8)
                            .stroke(ThemeColors.border, lineWidth: 1)
                    )

                    // Multi-device Split Bar in All Scope
                    if state.deviceScope == .all, let devSecs = result.deviceSeconds, devSecs.count > 1 {
                        let devA = devSecs.values.first ?? 0
                        let devB = devSecs.values.dropFirst().first ?? 0
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

                            GeometryReader { geo in
                                let total = max(1, devA + devB)
                                let fracA = CGFloat(devA) / CGFloat(total)
                                let fracB = CGFloat(devB) / CGFloat(total)

                                HStack(spacing: 2) {
                                    RoundedRectangle(cornerRadius: 2)
                                        .fill(ThemeColors.deviceA)
                                        .frame(width: geo.size.width * fracA, height: 6)

                                    RoundedRectangle(cornerRadius: 2)
                                        .fill(ThemeColors.deviceB)
                                        .frame(width: geo.size.width * fracB, height: 6)
                                }
                            }
                            .frame(height: 6)
                        }
                        .padding(10)
                        .background(ThemeColors.surface)
                        .cornerRadius(6)
                    }

                    // Key Stats Grid
                    HStack(spacing: 8) {
                        statCard(label: "Sessions", value: "\(result.sessionCount)")
                        statCard(label: "Average", value: Formatters.hoursOrMinutes(seconds: result.averageSessionSeconds))
                        statCard(label: "Longest", value: Formatters.hoursOrMinutes(seconds: result.longestSessionSeconds))
                    }

                    // 52-Week Year Calendar Heat Grid
                    VStack(alignment: .leading, spacing: 8) {
                        HStack {
                            Text("\(result.year) ACTIVITY HEATMAP")
                                .font(.system(size: 10, weight: .semibold))
                                .foregroundColor(ThemeColors.textDim)

                            Spacer()

                            if result.peakDaySeconds > 0 {
                                Text("Peak day: \(Formatters.hoursMinutes(seconds: result.peakDaySeconds))")
                                    .font(.system(size: 10))
                                    .foregroundColor(ThemeColors.textDim)
                            }
                        }

                        // Grid of days
                        yearHeatmapGrid(days: result.days)
                    }
                    .padding(12)
                    .background(ThemeColors.surface)
                    .cornerRadius(8)
                    .overlay(
                        RoundedRectangle(cornerRadius: 8)
                            .stroke(ThemeColors.border, lineWidth: 1)
                    )
                } else {
                    ProgressView()
                        .frame(maxWidth: .infinity, minHeight: 200)
                }
            }
            .padding(16)
        }
        .background(ThemeColors.background)
    }

    private func statCard(label: String, value: String) -> some View {
        VStack(spacing: 3) {
            Text(value)
                .font(.system(size: 15, weight: .bold, design: .rounded))
                .foregroundColor(ThemeColors.textPrimary)

            Text(label)
                .font(.system(size: 10))
                .foregroundColor(ThemeColors.textDim)
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 8)
        .background(ThemeColors.surface)
        .cornerRadius(6)
    }

    private func yearHeatmapGrid(days: [AppDetailDay]) -> some View {
        // Chunk into 7-day columns (weeks)
        let weeks = stride(from: 0, to: days.count, by: 7).map {
            Array(days[$0..<min($0 + 7, days.count)])
        }

        return ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 3) {
                ForEach(Array(weeks.enumerated()), id: \.offset) { _, week in
                    VStack(spacing: 3) {
                        ForEach(Array(week.enumerated()), id: \.offset) { _, day in
                            RoundedRectangle(cornerRadius: 1.5)
                                .fill(heatColor(level: day.level))
                                .frame(width: 8, height: 8)
                        }
                    }
                }
            }
            .padding(.vertical, 4)
        }
    }

    private func heatColor(level: Int) -> Color {
        switch level {
        case 0: return ThemeColors.barBackground
        case 1: return ThemeColors.accentDark.opacity(0.4)
        case 2: return ThemeColors.accentDark
        case 3: return ThemeColors.accent.opacity(0.8)
        default: return ThemeColors.accent
        }
    }
}

