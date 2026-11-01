import SwiftUI
import TimeSinkCore

public struct TodayTabView: View {
    @ObservedObject var state: AppState
    @State private var expandedAppId: Int64?

    public init(state: AppState) {
        self.state = state
    }

    public var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 14) {
                if let result = state.todayResult {
                    // Header info
                    VStack(alignment: .leading, spacing: 4) {
                        HStack {
                            Text(state.deviceScope == .all ? "Today · All devices" : "Today · \(state.localDeviceName)")
                                .font(.system(size: 12, weight: .medium))
                                .foregroundColor(ThemeColors.textDim)

                            Spacer()

                            if result.runningCount > 0 {
                                HStack(spacing: 5) {
                                    Circle()
                                        .fill(ThemeColors.accent)
                                        .frame(width: 6, height: 6)
                                    Text("\(result.runningCount) running · \(state.localDeviceName)")
                                        .font(.system(size: 11, weight: .medium))
                                        .foregroundColor(ThemeColors.accent)
                                }
                                .padding(.horizontal, 7)
                                .padding(.vertical, 2)
                                .background(ThemeColors.surfaceRaised)
                                .cornerRadius(10)
                            }
                        }

                        // Headline uptime
                        HStack(alignment: .firstTextBaseline, spacing: 10) {
                            Text(Formatters.hoursMinutes(seconds: result.uptimeSeconds))
                                .font(.system(size: 28, weight: .bold, design: .rounded))
                                .foregroundColor(ThemeColors.textPrimary)

                            if result.uptimeVsYesterdaySeconds != 0 {
                                let delta = result.uptimeVsYesterdaySeconds
                                HStack(spacing: 3) {
                                    Image(systemName: delta >= 0 ? "arrow.up.right" : "arrow.down.right")
                                        .font(.system(size: 10, weight: .bold))
                                    Text("\(delta >= 0 ? "+" : "-")\(Formatters.hoursOrMinutes(seconds: abs(delta)))")
                                        .font(.system(size: 11, weight: .medium))
                                }
                                .foregroundColor(delta >= 0 ? ThemeColors.accent : ThemeColors.warning)
                                .padding(.horizontal, 6)
                                .padding(.vertical, 2)
                                .background(ThemeColors.surfaceRaised)
                                .cornerRadius(5)
                            }
                        }

                        // Device Summary Row in All Scope
                        if state.deviceScope == .all, let uptimes = result.deviceUptimes, uptimes.count > 1 {
                            let devA = uptimes.first(where: { state.isLocalDevice($0.key) })?.value.uptimeSeconds ?? 0
                            let devB = uptimes.first(where: { !state.isLocalDevice($0.key) })?.value.uptimeSeconds ?? 0
                            DeviceSummaryRowView(
                                localName: state.localDeviceName,
                                remoteName: state.remoteDeviceName,
                                localSeconds: devA,
                                remoteSeconds: devB,
                                onBothSeconds: result.onBothSeconds
                            )
                        }
                    }

                    // Hourly chart card
                    VStack(alignment: .leading, spacing: 8) {
                        HStack {
                            Text("HOURLY ACTIVITY")
                                .font(.system(size: 10, weight: .semibold))
                                .foregroundColor(ThemeColors.textDim)

                            Spacer()

                            if state.deviceScope == .all {
                                HStack(spacing: 6) {
                                    CompoundDeviceSwatch(width: 14, height: 8)
                                    Text("Overlap")
                                        .font(.system(size: 10))
                                        .foregroundColor(ThemeColors.textDim)
                                }
                            }
                        }

                        HourlyChartView(hours: result.hours, isAllScope: state.deviceScope == .all)
                    }
                    .padding(12)
                    .background(ThemeColors.surface)
                    .cornerRadius(8)
                    .overlay(
                        RoundedRectangle(cornerRadius: 8)
                            .stroke(ThemeColors.border, lineWidth: 1)
                    )

                    // Tracked apps list
                    VStack(alignment: .leading, spacing: 6) {
                        Text("APPS USED TODAY")
                            .font(.system(size: 10, weight: .semibold))
                            .foregroundColor(ThemeColors.textDim)
                            .padding(.top, 4)

                        if result.rows.isEmpty {
                            Text("No apps tracked today yet.")
                                .font(.system(size: 13))
                                .foregroundColor(ThemeColors.textMuted)
                                .padding(.vertical, 16)
                        } else {
                            ForEach(result.rows, id: \.appId) { row in
                                appRowView(row: row, result: result)
                            }
                        }
                    }
                } else {
                    VStack(spacing: 8) {
                        ProgressView()
                        Text("Loading Today...")
                            .font(.system(size: 12))
                            .foregroundColor(ThemeColors.textDim)
                    }
                    .frame(maxWidth: .infinity, minHeight: 200)
                }
            }
            .padding(16)
        }
        .background(ThemeColors.background)
    }

    private func appRowView(row: TodayAppRow, result: TodayResult) -> some View {
        let isExpanded = expandedAppId == row.appId
        let maxSeconds = max(1, result.rows.map(\.seconds).max() ?? 1)
        let shareFrac = CGFloat(row.seconds) / CGFloat(maxSeconds)

        return VStack(spacing: 0) {
            Button(action: {
                withAnimation(.easeInOut(duration: 0.18)) {
                    if expandedAppId == row.appId {
                        expandedAppId = nil
                    } else {
                        expandedAppId = row.appId
                    }
                }
            }) {
                HStack(spacing: 10) {
                    // App letter tile or cached icon
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
                                .font(.system(size: 14, weight: .bold))
                                .foregroundColor(ThemeColors.accent)
                        }
                    }

                    // App details
                    VStack(alignment: .leading, spacing: 3) {
                        HStack(spacing: 6) {
                            Text(row.name)
                                .font(.system(size: 13, weight: .medium))
                                .foregroundColor(ThemeColors.textPrimary)

                            if row.isRunning {
                                Circle()
                                    .fill(ThemeColors.accent)
                                    .frame(width: 5, height: 5)
                            }

                            if state.deviceScope == .all, let devSecs = row.deviceSeconds {
                                ForEach(Array(devSecs.keys), id: \.self) { devId in
                                    let isMac = state.isLocalDevice(devId)
                                    DeviceMarker(isLocal: isMac, size: 6)
                                }
                            }
                        }

                        // Progress share bar
                        GeometryReader { barGeo in
                            ZStack(alignment: .leading) {
                                RoundedRectangle(cornerRadius: 1.5)
                                    .fill(ThemeColors.barBackground)
                                    .frame(height: 3)

                                RoundedRectangle(cornerRadius: 1.5)
                                    .fill(ThemeColors.accent)
                                    .frame(width: barGeo.size.width * shareFrac, height: 3)
                            }
                        }
                        .frame(height: 3)
                    }

                    Spacer()

                    Text(Formatters.hoursMinutes(seconds: row.seconds))
                        .font(.system(size: 13, weight: .medium, design: .monospaced))
                        .foregroundColor(ThemeColors.textPrimary)

                    Image(systemName: isExpanded ? "chevron.up" : "chevron.down")
                        .font(.system(size: 10))
                        .foregroundColor(ThemeColors.textDim)
                }
                .padding(.horizontal, 10)
                .padding(.vertical, 8)
                .background(isExpanded ? ThemeColors.surfaceRaised : ThemeColors.surface)
                .cornerRadius(6)
            }
            .buttonStyle(.plain)

            // Expanded panel: SessionDial + Stats
            if isExpanded {
                HStack(spacing: 16) {
                    SessionDialView(
                        appRow: row,
                        dayStartUtc: result.dayStartUtc,
                        dayEndUtc: result.dayEndUtc,
                        isAllScope: state.deviceScope == .all,
                        localDeviceId: state.localDeviceId
                    )

                    VStack(alignment: .leading, spacing: 6) {
                        HStack {
                            Text("Sessions:")
                                .font(.system(size: 11))
                                .foregroundColor(ThemeColors.textDim)
                            Text("\(row.sessions.count)")
                                .font(.system(size: 11, weight: .semibold))
                                .foregroundColor(ThemeColors.textPrimary)
                        }

                        if let avg = row.sevenDayAverageSeconds, avg > 0 {
                            HStack {
                                Text("7-day avg:")
                                    .font(.system(size: 11))
                                    .foregroundColor(ThemeColors.textDim)
                                Text(Formatters.hoursMinutes(seconds: Int64(avg)))
                                    .font(.system(size: 11, weight: .semibold))
                                    .foregroundColor(ThemeColors.textMuted)
                            }
                        }

                        if state.deviceScope == .all, row.onBothSeconds > 0 {
                            HStack {
                                Text("On both:")
                                    .font(.system(size: 11))
                                    .foregroundColor(ThemeColors.textDim)
                                Text(Formatters.hoursMinutes(seconds: row.onBothSeconds))
                                    .font(.system(size: 11, weight: .semibold))
                                    .foregroundColor(ThemeColors.accent)
                            }
                        }

                        Button(action: {
                            state.openAppDetail(appId: row.appId)
                        }) {
                            Text("View app details →")
                                .font(.system(size: 11, weight: .medium))
                                .foregroundColor(ThemeColors.accent)
                        }
                        .buttonStyle(.plain)
                        .padding(.top, 4)
                    }

                    Spacer()
                }
                .padding(12)
                .background(ThemeColors.surface)
                .cornerRadius(6)
                .padding(.top, 2)
            }
        }
    }
}

