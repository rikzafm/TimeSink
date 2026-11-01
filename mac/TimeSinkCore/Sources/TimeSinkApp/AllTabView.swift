import SwiftUI
import TimeSinkCore

public struct AllTabView: View {
    @ObservedObject var state: AppState

    public init(state: AppState) {
        self.state = state
    }

    private var isCompact: Bool {
        state.allLayout == "icons"
    }

    public var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 8) {
                Text("Lifetime · every app, one table")
                    .font(.system(size: 11))
                    .foregroundColor(ThemeColors.textMuted)

                Spacer()

                Button(action: {
                    let nextLayout = isCompact ? "labeled" : "icons"
                    state.setAllLayout(nextLayout)
                }) {
                    IconView(
                        .tableOptions,
                        size: 16,
                        weight: 1.6,
                        color: isCompact ? ThemeColors.accent : ThemeColors.textMuted
                    )
                    .frame(width: 24, height: 24)
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .help(isCompact ? "Labeled stats" : "Icon stats")
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

            VStack(alignment: .trailing, spacing: 2) {
                (Text("\(result.apps.count)").bold().foregroundColor(ThemeColors.textPrimary) + Text(" tracked · ")
                    + Text("\(result.retired.count)").bold().foregroundColor(ThemeColors.textPrimary) + Text(" retired"))
                    .font(.system(size: 11))
                    .foregroundColor(ThemeColors.textMuted)

                HStack(spacing: 0) {
                    Text("sorted by ")
                        .font(.system(size: 11))
                        .foregroundColor(ThemeColors.textMuted)

                    Button(action: {
                        state.allSort.next()
                    }) {
                        Text(state.allSort.column.displayName)
                            .font(.system(size: 11, weight: .semibold))
                            .foregroundColor(ThemeColors.textPrimary)
                    }
                    .buttonStyle(.plain)

                    Text(" ")
                        .font(.system(size: 11))

                    Button(action: {
                        state.allSort.descending.toggle()
                    }) {
                        Text(state.allSort.arrow)
                            .font(.system(size: 11, weight: .semibold))
                            .foregroundColor(ThemeColors.textPrimary)
                    }
                    .buttonStyle(.plain)
                }
            }
            .padding(.bottom, 4)
        }
    }

    private func table(_ result: AllResult) -> some View {
        let sortedRows = AllSorting.apply(rows: result.apps, sort: state.allSort)

        return VStack(spacing: 0) {
            HStack {
                Button(action: {
                    state.allSort.tap(.app)
                }) {
                    HStack(spacing: 4) {
                        Text("APP")
                        if state.allSort.column == .app {
                            Text(state.allSort.arrow)
                        }
                    }
                    .font(.system(size: 11, weight: .bold))
                    .tracking(1)
                    .foregroundColor(state.allSort.column == .app ? ThemeColors.accent : ThemeColors.textMuted)
                }
                .buttonStyle(.plain)

                Spacer()

                Button(action: {
                    state.allSort.tap(.hours)
                }) {
                    HStack(spacing: 4) {
                        Text("HOURS")
                        if state.allSort.column == .hours {
                            Text(state.allSort.arrow)
                        }
                    }
                    .font(.system(size: 11, weight: .bold))
                    .tracking(1)
                    .foregroundColor(state.allSort.column == .hours ? ThemeColors.accent : ThemeColors.textMuted)
                }
                .buttonStyle(.plain)
            }
            .padding(.horizontal, 14)
            .frame(height: 34)
            .bottomRule()

            ScrollView(.vertical, showsIndicators: false) {
                VStack(spacing: 0) {
                    if sortedRows.isEmpty && result.retired.isEmpty {
                        Text("No activity tracked yet")
                            .font(.system(size: 12))
                            .foregroundColor(ThemeColors.textMuted)
                            .padding(14)
                    } else {
                        ForEach(Array(sortedRows.enumerated()), id: \.element.appId) { i, row in
                            rowView(row, highlighted: i == 0)
                        }

                        if !result.retired.isEmpty {
                            HStack {
                                Text("RETIRED")
                                    .font(.system(size: 11, weight: .bold))
                                    .foregroundColor(ThemeColors.textMuted)
                                Spacer()
                            }
                            .padding(.horizontal, 14)
                            .padding(.top, 10)
                            .padding(.bottom, 4)

                            ForEach(result.retired, id: \.appId) { row in
                                retiredRowView(row)
                            }
                        }
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

    private func avgSessionString(_ row: AllAppRow) -> String {
        row.sessionCount > 0 ? Formatters.paddedHM(Int64(row.averageSessionSeconds.rounded())) : "Never"
    }

    private func lastOpenedString(_ row: AllAppRow) -> String {
        guard row.lastUsedUtc > 0 else { return "Never" }
        let now = Date().timeIntervalSince1970
        let span = max(0, now - Double(row.lastUsedUtc))
        if span < 60 { return "Just now" }
        if span < 3600 { return "\(Int(span / 60)) min ago" }
        if span < 86400 { return "\(Int(span / 3600)) h ago" }
        if span < 172800 { return "Yesterday" }
        let days = Int(span / 86400)
        return "\(days) d ago"
    }

    private func rowView(_ row: AllAppRow, highlighted: Bool) -> some View {
        Button(action: {
            state.openAppDetail(appId: row.appId)
        }) {
            Group {
                if isCompact {
                    compactRowContent(row)
                } else {
                    labeledRowContent(row)
                }
            }
            .background(highlighted ? ThemeColors.barBackground : .clear)
            .bottomRule()
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }

    private func labeledRowContent(_ row: AllAppRow) -> some View {
        HStack(spacing: 12) {
            AppTile(name: row.name, png: state.iconCache.getCachedIcon(forAppId: row.appId), size: 36, radius: 10, fontSize: 17, fill: ThemeColors.background)

            VStack(alignment: .leading, spacing: 3) {
                HStack(spacing: 6) {
                    Text(row.name)
                        .font(.system(size: 14, weight: .bold))
                        .foregroundColor(ThemeColors.textPrimary)
                        .lineLimit(1)

                    if state.deviceScope == .all, let devices = row.devices {
                        ForEach(devices, id: \.self) { DeviceMarker(isLocal: state.isLocalDevice($0), size: 6) }
                    }

                    if row.background {
                        IconView(.layers, size: 12, weight: 1.4, color: ThemeColors.textMuted)
                            .help("Background app")
                    }
                }

                HStack(alignment: .top, spacing: 18) {
                    VStack(alignment: .leading, spacing: 1) {
                        Text("AVG SESSION")
                            .font(.system(size: 9, weight: .bold))
                            .foregroundColor(ThemeColors.textMuted)
                        Text(avgSessionString(row))
                            .font(.system(size: 11))
                            .foregroundColor(ThemeColors.textDim)
                    }

                    VStack(alignment: .leading, spacing: 1) {
                        Text("LAST OPENED")
                            .font(.system(size: 9, weight: .bold))
                            .foregroundColor(ThemeColors.textMuted)
                        Text(lastOpenedString(row))
                            .font(.system(size: 11))
                            .foregroundColor(ThemeColors.textDim)
                    }
                }
            }

            Spacer(minLength: 4)

            HStack(alignment: .firstTextBaseline, spacing: 0) {
                Text(Formatters.decimalHours(row.seconds))
                    .font(.system(size: 20, weight: .bold))
                    .foregroundColor(ThemeColors.accent)
                Text(" h")
                    .font(.system(size: 12, weight: .medium))
                    .foregroundColor(ThemeColors.textMuted)
            }
        }
        .padding(.horizontal, 14)
        .frame(height: 60)
    }

    private func compactRowContent(_ row: AllAppRow) -> some View {
        HStack(spacing: 12) {
            AppTile(name: row.name, png: state.iconCache.getCachedIcon(forAppId: row.appId), size: 36, radius: 10, fontSize: 17, fill: ThemeColors.background)

            VStack(alignment: .leading, spacing: 3) {
                HStack(spacing: 6) {
                    Text(row.name)
                        .font(.system(size: 14, weight: .bold))
                        .foregroundColor(ThemeColors.textPrimary)
                        .lineLimit(1)

                    if state.deviceScope == .all, let devices = row.devices {
                        ForEach(devices, id: \.self) { DeviceMarker(isLocal: state.isLocalDevice($0), size: 6) }
                    }

                    if row.background {
                        IconView(.layers, size: 12, weight: 1.4, color: ThemeColors.textMuted)
                            .help("Background app")
                    }
                }

                HStack(spacing: 14) {
                    HStack(spacing: 5) {
                        IconView(.clock, size: 11, weight: 1.4, color: ThemeColors.textMuted)
                        Text(avgSessionString(row))
                    }
                    HStack(spacing: 5) {
                        IconView(.history, size: 11, weight: 1.4, color: ThemeColors.textMuted)
                        Text(lastOpenedString(row))
                    }
                }
                .font(.system(size: 11))
                .foregroundColor(ThemeColors.textDim)
            }

            Spacer(minLength: 4)

            HStack(alignment: .firstTextBaseline, spacing: 0) {
                Text(Formatters.decimalHours(row.seconds))
                    .font(.system(size: 20, weight: .bold))
                    .foregroundColor(ThemeColors.accent)
                Text(" h")
                    .font(.system(size: 12, weight: .medium))
                    .foregroundColor(ThemeColors.textMuted)
            }
        }
        .padding(.horizontal, 14)
        .frame(height: 50)
    }

    private func retiredRowView(_ row: AllRetiredRow) -> some View {
        HStack(spacing: 12) {
            VStack(alignment: .leading, spacing: 2) {
                Text(row.name)
                    .font(.system(size: 14, weight: .bold))
                    .foregroundColor(ThemeColors.textPrimary)
                    .lineLimit(1)

                Text("Stopped \(Formatters.dayText(row.stoppedDate, format: "d MMM"))")
                    .font(.system(size: 11))
                    .foregroundColor(ThemeColors.textMuted)
            }

            Spacer(minLength: 4)

            HStack(alignment: .firstTextBaseline, spacing: 0) {
                Text(Formatters.decimalHours(row.seconds))
                    .font(.system(size: 15, weight: .bold))
                    .foregroundColor(ThemeColors.textPrimary)
                Text(" h")
                    .font(.system(size: 12, weight: .medium))
                    .foregroundColor(ThemeColors.textMuted)
            }
        }
        .padding(.horizontal, 14)
        .frame(height: isCompact ? 46 : 52)
        .opacity(0.55)
        .bottomRule()
    }
}
