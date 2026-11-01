import SwiftUI
import TimeSinkCore

public struct AllTabView: View {
    @ObservedObject var state: AppState

    public init(state: AppState) {
        self.state = state
    }

    private var sortLabel: String {
        switch state.allSort {
        case .time: return "hours ↓"
        case .name: return "name ↓"
        case .lastOpened: return "last opened ↓"
        }
    }

    private func sorted(_ rows: [AllAppRow]) -> [AllAppRow] {
        switch state.allSort {
        case .time: return rows
        case .name: return rows.sorted { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }
        case .lastOpened: return rows.sorted { $0.lastUsedUtc > $1.lastUsedUtc }
        }
    }

    public var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 8) {
                Text("Lifetime · every app, one table")
                    .font(.system(size: 11))
                    .foregroundColor(ThemeColors.textMuted)
                Spacer()
                Menu {
                    ForEach(AllSortOption.allCases) { option in
                        Button("Sort by \(option.rawValue.lowercased())") { state.allSort = option }
                    }
                } label: {
                    IconView(.tableOptions, size: 16, color: ThemeColors.textDim)
                        .frame(width: 32, height: 30)
                        .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(ThemeColors.border, lineWidth: 1))
                }
                .menuStyle(.button)
                .buttonStyle(.plain)
                .menuIndicator(.hidden)
                .fixedSize()
                .help("Table options")
            }

            if let result = state.allResult {
                headline(result)
                table(result)
            } else {
                ProgressView().frame(maxWidth: .infinity, minHeight: 200)
            }
        }
        .padding(.horizontal, 16)
        .padding(.top, 12)
    }

    private func headline(_ result: AllResult) -> some View {
        HStack(alignment: .bottom) {
            HStack(alignment: .firstTextBaseline, spacing: 0) {
                Text(Formatters.grouped((result.totalSeconds + 1800) / 3600))
                    .font(.system(size: 46, weight: .bold))
                    .tracking(-1)
                Text(" h").font(.system(size: 28, weight: .bold))
            }
            .foregroundColor(ThemeColors.accent)
            .frame(height: 52)

            Spacer()

            VStack(alignment: .trailing, spacing: 0) {
                (Text("\(result.apps.count)").bold().foregroundColor(ThemeColors.textPrimary) + Text(" tracked · ")
                    + Text("\(result.retired.count)").bold().foregroundColor(ThemeColors.textPrimary) + Text(" retired"))
                    .frame(height: 17)
                (Text("sorted by ") + Text(sortLabel).bold().foregroundColor(ThemeColors.textPrimary))
                    .frame(height: 17)
            }
            .font(.system(size: 11))
            .foregroundColor(ThemeColors.textMuted)
            .padding(.bottom, 4)
        }
    }

    private func table(_ result: AllResult) -> some View {
        VStack(spacing: 0) {
            HStack {
                Text("APP").foregroundColor(ThemeColors.textMuted)
                Spacer()
                Text("HOURS ↓").foregroundColor(ThemeColors.accent)
            }
            .font(.system(size: 11, weight: .bold))
            .tracking(1)
            .padding(.horizontal, 14)
            .frame(height: 34)
            .bottomRule()

            ScrollView(.vertical, showsIndicators: false) {
                VStack(spacing: 0) {
                    ForEach(Array(sorted(result.apps).enumerated()), id: \.element.appId) { i, row in
                        rowView(row, highlighted: i == 0)
                    }
                }
            }

            HStack {
                Text("Total uptime").foregroundColor(ThemeColors.textPrimary)
                Spacer()
                Text(Formatters.hours(result.totalSeconds)).foregroundColor(ThemeColors.accent)
            }
            .font(.system(size: 14, weight: .bold))
            .padding(.horizontal, 14)
            .frame(height: 44)
            .topRule(ThemeColors.border)
        }
        .clipShape(RoundedRectangle(cornerRadius: 12))
        .card(border: ThemeColors.border, radius: 12)
        .padding(.top, 4)
        .padding(.bottom, 6)
    }

    private func lastOpened(_ row: AllAppRow) -> String {
        let span = Date().timeIntervalSince1970 - Double(row.lastUsedUtc)
        if row.lastUsedUtc <= 0 { return "Never" }
        if span < 3600 { return "Just now" }
        if span < 86400 { return "\(Int(span / 3600)) h ago" }
        if span < 172800 { return "Yesterday" }
        return "\(Int(span / 86400)) d ago"
    }

    private func rowView(_ row: AllAppRow, highlighted: Bool) -> some View {
        Button(action: { state.openAppDetail(appId: row.appId) }) {
            HStack(spacing: 12) {
                AppTile(name: row.name, png: state.iconCache.getCachedIcon(forAppId: row.appId), size: 36, radius: 10, fontSize: 17, fill: ThemeColors.background)

                VStack(alignment: .leading, spacing: 3) {
                    HStack(spacing: 6) {
                        Text(row.name)
                            .font(.system(size: 14, weight: .bold))
                            .foregroundColor(ThemeColors.textPrimary)
                            .lineLimit(1)
                        ForEach(row.devices ?? [], id: \.self) { DeviceMarker(isLocal: state.isLocalDevice($0), size: 6) }
                    }
                    HStack(spacing: 14) {
                        HStack(spacing: 5) {
                            IconView(.clock, size: 12, weight: 1.2, color: ThemeColors.textMuted)
                            Text(Formatters.paddedHM(Int64(row.averageSessionSeconds)))
                        }
                        HStack(spacing: 5) {
                            IconView(.history, size: 12, weight: 1.2, color: ThemeColors.textMuted)
                            Text(lastOpened(row))
                        }
                    }
                    .font(.system(size: 11))
                    .foregroundColor(ThemeColors.textDim)
                }

                Spacer(minLength: 4)

                HStack(alignment: .firstTextBaseline, spacing: 0) {
                    Text(Formatters.grouped((row.seconds + 1800) / 3600))
                        .font(.system(size: 22, weight: .bold))
                        .foregroundColor(ThemeColors.accent)
                    Text(" h")
                        .font(.system(size: 13, weight: .medium))
                        .foregroundColor(ThemeColors.textMuted)
                }
            }
            .padding(.horizontal, 14)
            .frame(height: 56)
            .background(highlighted ? ThemeColors.barBackground : .clear)
            .bottomRule()
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }
}
