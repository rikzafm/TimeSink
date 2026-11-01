import SwiftUI
import TimeSinkCore

public struct MergeView: View {
    @ObservedObject var state: AppState
    public let mainAppId: Int64
    public let onDismiss: () -> Void

    @State private var plan: MergePlan?
    @State private var selectedDevice: String = ""
    @State private var searchQuery: String = ""

    public init(state: AppState, mainAppId: Int64, onDismiss: @escaping () -> Void) {
        self.state = state
        self.mainAppId = mainAppId
        self.onDismiss = onDismiss
    }

    public var body: some View {
        VStack(spacing: 0) {
            // Header
            HStack {
                Button(action: onDismiss) {
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

                Text("Merge into “\(plan?.mainName ?? "App")”")
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundColor(ThemeColors.textPrimary)

                Spacer()
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

            if let p = plan {
                // Device tabs bar
                let devs = p.devices
                if devs.count > 1 {
                    HStack(spacing: 12) {
                        ForEach(devs, id: \.self) { devId in
                            let isLocal = devId.caseInsensitiveCompare(state.localDeviceId) == .orderedSame
                            let devName = isLocal ? state.localDeviceName : (state.remoteDeviceName ?? "PC")
                            let tickedCount = p.tickedCountForDevice(devId)
                            let isSelected = selectedDevice == devId

                            Button(action: {
                                selectedDevice = devId
                                p.deviceFilter = devId
                            }) {
                                HStack(spacing: 6) {
                                    DeviceMarker(isLocal: isLocal, size: 7)
                                    Text(devName)
                                        .font(.system(size: 12, weight: isSelected ? .semibold : .medium))
                                        .foregroundColor(isSelected ? ThemeColors.textPrimary : ThemeColors.textMuted)

                                    if tickedCount > 0 {
                                        Text("\(tickedCount)")
                                            .font(.system(size: 9, weight: .bold))
                                            .foregroundColor(ThemeColors.background)
                                            .padding(.horizontal, 5)
                                            .padding(.vertical, 1)
                                            .background(ThemeColors.accent)
                                            .cornerRadius(8)
                                    }
                                }
                                .padding(.vertical, 6)
                                .overlay(
                                    Rectangle()
                                        .fill(isSelected ? ThemeColors.accent : Color.clear)
                                        .frame(height: 2),
                                    alignment: .bottom
                                )
                            }
                            .buttonStyle(.plain)
                        }
                        Spacer()
                    }
                    .padding(.horizontal, 16)
                    .background(ThemeColors.background)
                }

                // Suggested banner
                if let suggestion = p.suggestedMatches.first {
                    HStack {
                        Image(systemName: "sparkles")
                            .font(.system(size: 12))
                            .foregroundColor(ThemeColors.accent)

                        Text("Suggested: same name on \(state.remoteDeviceName ?? "other device")")
                            .font(.system(size: 11))
                            .foregroundColor(ThemeColors.textPrimary)

                        Spacer()

                        Button(action: {
                            p.toggle(suggestion.key)
                            // Trigger view reload
                            searchQuery = searchQuery
                        }) {
                            Text(p.isTicked(suggestion.key) ? "Ticked" : "Tick match")
                                .font(.system(size: 11, weight: .semibold))
                                .foregroundColor(ThemeColors.background)
                                .padding(.horizontal, 8)
                                .padding(.vertical, 3)
                                .background(ThemeColors.accent)
                                .cornerRadius(4)
                        }
                        .buttonStyle(.plain)
                    }
                    .padding(.horizontal, 12)
                    .padding(.vertical, 8)
                    .background(ThemeColors.surfaceRaised)
                    .cornerRadius(6)
                    .padding(.horizontal, 16)
                    .padding(.top, 8)
                }

                // Search Box
                HStack(spacing: 6) {
                    Image(systemName: "magnifyingglass")
                        .font(.system(size: 11))
                        .foregroundColor(ThemeColors.textDim)

                    let activeDevName = selectedDevice.caseInsensitiveCompare(state.localDeviceId) == .orderedSame ? state.localDeviceName : (state.remoteDeviceName ?? "PC")
                    TextField("Search apps on \(activeDevName)...", text: $searchQuery)
                        .textFieldStyle(.plain)
                        .font(.system(size: 12))
                        .foregroundColor(ThemeColors.textPrimary)
                        .onChange(of: searchQuery) { _, val in
                            p.query = val
                        }
                }
                .padding(.horizontal, 10)
                .padding(.vertical, 6)
                .background(ThemeColors.surface)
                .cornerRadius(6)
                .overlay(
                    RoundedRectangle(cornerRadius: 6)
                        .stroke(ThemeColors.border, lineWidth: 1)
                )
                .padding(.horizontal, 16)
                .padding(.top, 8)

                // Candidate rows
                ScrollView {
                    VStack(spacing: 4) {
                        let candidates = p.shown()
                        if candidates.isEmpty {
                            Text("No candidate apps found on this device.")
                                .font(.system(size: 12))
                                .foregroundColor(ThemeColors.textDim)
                                .padding(.vertical, 24)
                        } else {
                            ForEach(candidates, id: \.key) { cand in
                                let isTicked = p.isTicked(cand.key)
                                Button(action: {
                                    p.toggle(cand.key)
                                    searchQuery = searchQuery // refresh
                                }) {
                                    HStack(spacing: 10) {
                                        Image(systemName: isTicked ? "checkmark.square.fill" : "square")
                                            .font(.system(size: 14))
                                            .foregroundColor(isTicked ? ThemeColors.accent : ThemeColors.textDim)

                                        VStack(alignment: .leading, spacing: 2) {
                                            Text(cand.name)
                                                .font(.system(size: 13, weight: .medium))
                                                .foregroundColor(ThemeColors.textPrimary)

                                            Text(cand.exePath)
                                                .font(.system(size: 10))
                                                .foregroundColor(ThemeColors.textDim)
                                                .lineLimit(1)
                                        }

                                        Spacer()

                                        if cand.seconds > 0 {
                                            Text(Formatters.hoursMinutes(seconds: cand.seconds))
                                                .font(.system(size: 11, design: .monospaced))
                                                .foregroundColor(ThemeColors.textMuted)
                                        }
                                    }
                                    .padding(.horizontal, 10)
                                    .padding(.vertical, 6)
                                    .background(isTicked ? ThemeColors.surfaceRaised : ThemeColors.surface)
                                    .cornerRadius(6)
                                }
                                .buttonStyle(.plain)
                            }
                        }
                    }
                    .padding(.horizontal, 16)
                    .padding(.vertical, 8)
                }

                // Bottom apply bar
                VStack(spacing: 8) {
                    let tickedCands = p.candidates.filter { p.isTicked($0.key) }
                    let totalHours = tickedCands.reduce(0) { $0 + $1.seconds }

                    HStack {
                        Text("\(p.mainName) + \(tickedCands.count) apps (\(Formatters.hoursMinutes(seconds: totalHours)))")
                            .font(.system(size: 11))
                            .foregroundColor(ThemeColors.textMuted)
                            .lineLimit(1)

                        Spacer()

                        Button(action: {
                            applyMerge(plan: p)
                        }) {
                            Text("Apply merge")
                                .font(.system(size: 12, weight: .semibold))
                                .foregroundColor(ThemeColors.background)
                                .padding(.horizontal, 14)
                                .padding(.vertical, 6)
                                .background(ThemeColors.accent)
                                .cornerRadius(6)
                        }
                        .buttonStyle(.plain)
                    }
                }
                .padding(14)
                .background(ThemeColors.surface)
                .overlay(
                    Rectangle()
                        .frame(height: 1)
                        .foregroundColor(ThemeColors.borderSubtle),
                    alignment: .top
                )
            } else {
                ProgressView()
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
        .frame(width: 400, height: 640)
        .background(ThemeColors.background)
        .onAppear {
            loadPlan()
        }
    }

    private func loadPlan() {
        let lib = MacAppLibrary(db: state.db)
        if let p = try? lib.openMergePlan(mainId: mainAppId) {
            self.plan = p
            if let firstDev = p.devices.first {
                self.selectedDevice = firstDev
                p.deviceFilter = firstDev
            }
        }
    }

    private func applyMerge(plan: MergePlan) {
        let lib = MacAppLibrary(db: state.db)
        let tickedAppIds = plan.candidates.filter { plan.isTicked($0.key) }.map(\.appId)
        try? lib.merge(mainId: mainAppId, candidateIds: tickedAppIds)
        state.refresh()
        onDismiss()
    }
}
