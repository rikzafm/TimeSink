import SwiftUI
import TimeSinkCore

public struct AllTabView: View {
    @ObservedObject var state: AppState

    public init(state: AppState) {
        self.state = state
    }

    public var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 12) {
                // Search box
                HStack(spacing: 8) {
                    Image(systemName: "magnifyingglass")
                        .font(.system(size: 12))
                        .foregroundColor(ThemeColors.textDim)

                    TextField("Search all apps...", text: $state.allSearchQuery)
                        .textFieldStyle(.plain)
                        .font(.system(size: 12))
                        .foregroundColor(ThemeColors.textPrimary)

                    if !state.allSearchQuery.isEmpty {
                        Button(action: {
                            state.allSearchQuery = ""
                        }) {
                            Image(systemName: "xmark.circle.fill")
                                .font(.system(size: 11))
                                .foregroundColor(ThemeColors.textDim)
                        }
                        .buttonStyle(.plain)
                    }
                }
                .padding(.horizontal, 10)
                .padding(.vertical, 7)
                .background(ThemeColors.surface)
                .cornerRadius(6)
                .overlay(
                    RoundedRectangle(cornerRadius: 6)
                        .stroke(ThemeColors.border, lineWidth: 1)
                )

                // Sort bar & count
                if let result = state.allResult {
                    let filtered = filteredRows(rows: result.apps)

                    HStack {
                        Text("\(filtered.count) APPS")
                            .font(.system(size: 10, weight: .semibold))
                            .foregroundColor(ThemeColors.textDim)

                        Spacer()

                        // Sort picker
                        HStack(spacing: 0) {
                            ForEach(AllSortOption.allCases) { opt in
                                Button(action: {
                                    state.allSort = opt
                                }) {
                                    Text(opt.rawValue)
                                        .font(.system(size: 10, weight: state.allSort == opt ? .semibold : .regular))
                                        .foregroundColor(state.allSort == opt ? ThemeColors.textPrimary : ThemeColors.textDim)
                                        .padding(.horizontal, 6)
                                        .padding(.vertical, 2)
                                        .background(state.allSort == opt ? ThemeColors.surfaceRaised : Color.clear)
                                        .cornerRadius(3)
                                }
                                .buttonStyle(.plain)
                            }
                        }
                    }

                    // App rows
                    if filtered.isEmpty {
                        Text("No matching apps found.")
                            .font(.system(size: 13))
                            .foregroundColor(ThemeColors.textMuted)
                            .padding(.vertical, 20)
                            .frame(maxWidth: .infinity, alignment: .center)
                    } else {
                        ForEach(filtered, id: \.appId) { row in
                            allAppRowView(row: row)
                        }
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

    private func filteredRows(rows: [AllAppRow]) -> [AllAppRow] {
        var list = rows
        if !state.allSearchQuery.isEmpty {
            let q = state.allSearchQuery.lowercased()
            list = list.filter { $0.name.lowercased().contains(q) }
        }

        switch state.allSort {
        case .time:
            return list.sorted(by: { $0.seconds > $1.seconds })
        case .name:
            return list.sorted(by: { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending })
        case .lastOpened:
            return list.sorted(by: { $0.lastUsedUtc > $1.lastUsedUtc })
        }
    }

    private func allAppRowView(row: AllAppRow) -> some View {
        Button(action: {
            state.openAppDetail(appId: row.appId)
        }) {
            HStack(spacing: 10) {
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

                // App name + device markers
                VStack(alignment: .leading, spacing: 3) {
                    HStack(spacing: 6) {
                        Text(row.name)
                            .font(.system(size: 13, weight: .medium))
                            .foregroundColor(ThemeColors.textPrimary)

                        if state.deviceScope == .all, let devs = row.devices {
                            ForEach(devs, id: \.self) { devId in
                                let isMac = devId.lowercased().contains("mac") || devId.isEmpty
                                DeviceMarker(isLocal: isMac, size: 6)
                            }
                        }
                    }

                    if row.lastUsedUtc > 0 {
                        Text("Last used \(Formatters.formatClock(utcSeconds: row.lastUsedUtc))")
                            .font(.system(size: 10))
                            .foregroundColor(ThemeColors.textDim)
                    }
                }

                Spacer()

                Text(Formatters.hoursMinutes(seconds: row.seconds))
                    .font(.system(size: 13, weight: .medium, design: .monospaced))
                    .foregroundColor(ThemeColors.textPrimary)

                Image(systemName: "chevron.right")
                    .font(.system(size: 10))
                    .foregroundColor(ThemeColors.textDim)
            }
            .padding(.horizontal, 10)
            .padding(.vertical, 8)
            .background(ThemeColors.surface)
            .cornerRadius(6)
        }
        .buttonStyle(.plain)
    }
}
