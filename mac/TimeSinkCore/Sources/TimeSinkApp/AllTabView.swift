import SwiftUI
import TimeSinkCore

public struct AllTabView: View {
    @ObservedObject var state: AppState

    public init(state: AppState) {
        self.state = state
    }

    public var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 14) {
                // Header description + Grid button
                HStack {
                    Text("Lifetime · every app, one table")
                        .font(.system(size: 11))
                        .foregroundColor(ThemeColors.textDim)

                    Spacer()

                    Button(action: {}) {
                        Image(systemName: "square.grid.2x2")
                            .font(.system(size: 12))
                            .foregroundColor(ThemeColors.textMuted)
                    }
                    .buttonStyle(.plain)
                }

                if let result = state.allResult {
                    // Headline total + Subtext
                    HStack(alignment: .firstTextBaseline) {
                        Text(Formatters.hoursOrMinutes(seconds: result.totalSeconds))
                            .font(.system(size: 34, weight: .bold, design: .rounded))
                            .foregroundColor(ThemeColors.textPrimary)

                        Spacer()

                        VStack(alignment: .trailing, spacing: 2) {
                            Text("\(result.apps.count) tracked · 1 retired")
                                .font(.system(size: 11))
                                .foregroundColor(ThemeColors.textDim)

                            Text("sorted by hours ↓")
                                .font(.system(size: 11))
                                .foregroundColor(ThemeColors.textDim)
                        }
                    }

                    // Column headers
                    HStack {
                        Text("APP")
                            .font(.system(size: 10, weight: .bold))
                            .foregroundColor(ThemeColors.textDim)

                        Spacer()

                        Text("HOURS ↓")
                            .font(.system(size: 10, weight: .bold))
                            .foregroundColor(ThemeColors.textDim)
                    }
                    .padding(.top, 4)

                    // App rows
                    VStack(spacing: 8) {
                        ForEach(result.apps, id: \.appId) { row in
                            allAppRowView(row: row)
                        }

                        // Total uptime row
                        Divider().background(ThemeColors.borderSubtle)

                        HStack {
                            Text("Total uptime")
                                .font(.system(size: 13, weight: .bold))
                                .foregroundColor(ThemeColors.textPrimary)

                            Spacer()

                            Text(Formatters.hoursOrMinutes(seconds: result.totalSeconds))
                                .font(.system(size: 14, weight: .bold))
                                .foregroundColor(ThemeColors.textPrimary)
                        }
                        .padding(.vertical, 6)
                    }
                } else {
                    ProgressView()
                        .frame(maxWidth: .infinity, minHeight: 200)
                }
            }
            .padding(16)
        }
        .background(ThemeColors.background)
    }

    private func allAppRowView(row: AllAppRow) -> some View {
        Button(action: {
            state.openAppDetail(appId: row.appId)
        }) {
            HStack(spacing: 12) {
                // App letter tile or icon
                ZStack {
                    RoundedRectangle(cornerRadius: 6)
                        .fill(ThemeColors.surfaceRaised)
                        .frame(width: 32, height: 32)

                    if let png = state.iconCache.getCachedIcon(forAppId: row.appId),
                       let nsImg = NSImage(data: png) {
                        Image(nsImage: nsImg)
                            .resizable()
                            .frame(width: 24, height: 24)
                            .cornerRadius(4)
                    } else {
                        Text(String(row.name.prefix(1)).uppercased())
                            .font(.system(size: 13, weight: .bold))
                            .foregroundColor(ThemeColors.accent)
                    }
                }

                // App name + device markers + recent stats
                VStack(alignment: .leading, spacing: 3) {
                    HStack(spacing: 5) {
                        Text(row.name)
                            .font(.system(size: 13, weight: .semibold))
                            .foregroundColor(ThemeColors.textPrimary)

                        if state.deviceScope == .all, let devs = row.devices {
                            ForEach(devs, id: \.self) { devId in
                                let isMac = state.isLocalDevice(devId)
                                DeviceMarker(isLocal: isMac, size: 6)
                            }
                        } else {
                            Circle().fill(ThemeColors.deviceA).frame(width: 5, height: 5)
                        }
                    }

                    HStack(spacing: 8) {
                        HStack(spacing: 3) {
                            Image(systemName: "clock")
                                .font(.system(size: 9))
                                .foregroundColor(ThemeColors.textDim)
                            Text("1h 24m")
                                .font(.system(size: 10))
                                .foregroundColor(ThemeColors.textDim)
                        }

                        HStack(spacing: 3) {
                            Image(systemName: "arrow.clockwise")
                                .font(.system(size: 9))
                                .foregroundColor(ThemeColors.textDim)
                            Text("Just now")
                                .font(.system(size: 10))
                                .foregroundColor(ThemeColors.textDim)
                        }
                    }
                }

                Spacer()

                Text(Formatters.hoursOrMinutes(seconds: row.seconds))
                    .font(.system(size: 15, weight: .bold))
                    .foregroundColor(ThemeColors.accent)
            }
            .padding(.vertical, 4)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }
}
