import SwiftUI
import TimeSinkCore

public struct MergeView: View {
    @ObservedObject var state: AppState
    public let mainAppId: Int64
    public let onDismiss: () -> Void

    @State private var plan: MergePlan?
    @State private var devices: [DeviceRow] = []
    @State private var selectedDevice: String?
    @State private var searchQuery = ""
    @State private var filter: MergeFilter = .tracked
    @State private var tickRevision = 0
    @State private var mainSeconds: Int64 = 0

    public init(state: AppState, mainAppId: Int64, onDismiss: @escaping () -> Void) {
        self.state = state
        self.mainAppId = mainAppId
        self.onDismiss = onDismiss
    }

    private var mainName: String { plan?.mainName ?? "" }
    private var deviceName: String { devices.first { $0.deviceId == selectedDevice }?.name ?? "" }

    private var ticked: [MergeCandidate] {
        _ = tickRevision
        guard let plan else { return [] }
        return plan.candidates.filter { plan.isTicked($0.key) }
    }

    private var changes: (add: [MergeCandidate], remove: [MergeCandidate]) {
        _ = tickRevision
        guard let plan else { return ([], []) }
        return (
            plan.candidates.filter { plan.isTicked($0.key) && $0.kind != .merged },
            plan.candidates.filter { !plan.isTicked($0.key) && $0.kind == .merged }
        )
    }

    private var shown: [MergeCandidate] {
        _ = tickRevision
        guard let plan else { return [] }
        plan.filter = filter
        plan.deviceFilter = selectedDevice
        plan.query = searchQuery
        return plan.shown()
    }

    public var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 8) {
                Button(action: onDismiss) {
                    IconView(.back, size: 16, weight: 1.8, color: ThemeColors.textPrimary)
                        .frame(width: 28, height: 28)
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                Text("Merge into “\(mainName)”")
                    .font(.system(size: 16, weight: .bold))
                    .foregroundColor(ThemeColors.textPrimary)
                    .lineLimit(1)
                Spacer()
            }
            .padding(.horizontal, 12)
            .frame(height: 50)

            deviceTabs

            ScrollView(.vertical, showsIndicators: false) {
                VStack(alignment: .leading, spacing: 10) {
                    Text("Tick the apps to count under this one. Their time is added together in every view. Untick a merged app to unmerge it.")
                        .font(.system(size: 12))
                        .lineSpacing(2)
                        .foregroundColor(ThemeColors.textDim)
                        .fixedSize(horizontal: false, vertical: true)

                    HStack(spacing: 8) {
                        HStack(spacing: 8) {
                            IconView(.searchSmall, size: 14, weight: 1.6, color: ThemeColors.textMuted)
                            TextField("Search apps on \(deviceName)", text: $searchQuery)
                                .textFieldStyle(.plain)
                                .font(.system(size: 12))
                                .foregroundColor(ThemeColors.textPrimary)
                        }
                        .padding(.horizontal, 12)
                        .frame(height: 34)
                        .card(fill: Tokens.segmentBg, border: ThemeColors.border)

                        Segmented(
                            options: [(MergeFilter.tracked, "Tracked"), (.untracked, "Untracked"), (.all, "All")],
                            selection: $filter,
                            vPad: 4
                        )
                    }

                    if shown.isEmpty {
                        let q = searchQuery.trimmingCharacters(in: .whitespacesAndNewlines)
                        Text(q.isEmpty ? "No candidates found" : "No installed app matches “\(q)”")
                            .font(.system(size: 12))
                            .foregroundColor(ThemeColors.textDim)
                            .frame(maxWidth: .infinity, alignment: .center)
                            .padding(.vertical, 24)
                    } else {
                        VStack(spacing: 6) {
                            ForEach(shown, id: \.key) { row($0) }
                        }
                    }

                    if !ticked.isEmpty { preview }
                    footnote
                }
                .padding(.horizontal, 16)
                .padding(.top, 12)
                .padding(.bottom, 10)
            }

            bottomBar
        }
        .frame(width: 400, height: 640)
        .background(ThemeColors.background)
        .onAppear(perform: loadPlan)
    }

    private var deviceTabs: some View {
        HStack(spacing: 16) {
            ForEach(devices) { device in
                let on = device.deviceId == selectedDevice
                let count = countFor(device.deviceId)
                Button(action: { selectedDevice = device.deviceId }) {
                    HStack(spacing: 6) {
                        if device.isLocal {
                            Circle().fill(ThemeColors.deviceA).frame(width: 7, height: 7)
                        } else {
                            RoundedRectangle(cornerRadius: 2).fill(ThemeColors.deviceB).frame(width: 7, height: 7)
                        }
                        Text(device.isLocal ? "This Mac" : device.name)
                            .font(.system(size: 13, weight: on ? .semibold : .regular))
                            .foregroundColor(on ? ThemeColors.textPrimary : ThemeColors.textMuted)
                            .lineLimit(1)
                        if count > 0 {
                            Text("\(count)")
                                .font(.system(size: 10, weight: .bold))
                                .foregroundColor(ThemeColors.background)
                                .padding(.horizontal, 6)
                                .background(Capsule().fill(ThemeColors.accent))
                        }
                    }
                    .frame(height: 32)
                    .overlay(Rectangle().fill(on ? ThemeColors.accent : .clear).frame(height: 2), alignment: .bottom)
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
            }
            Spacer()
        }
        .bottomRule()
        .padding(.horizontal, 16)
    }

    private func countFor(_ id: String) -> Int {
        _ = tickRevision
        return plan?.tickedCountForDevice(id) ?? 0
    }

    private func subtitle(_ c: MergeCandidate) -> String {
        let name = c.exePath.split(whereSeparator: { $0 == "/" || $0 == "\\" }).last.map(String.init) ?? c.exePath
        let base = name == c.name ? "" : "\(name) · "
        return base + (c.isTracked ? "tracked" : "untracked")
    }

    private func row(_ c: MergeCandidate) -> some View {
        let on = plan?.isTicked(c.key) ?? false
        return Button(action: {
            plan?.toggle(c.key)
            tickRevision += 1
        }) {
            HStack(spacing: 10) {
                ZStack {
                    RoundedRectangle(cornerRadius: 5).fill(on ? ThemeColors.accent : .clear)
                    RoundedRectangle(cornerRadius: 5).strokeBorder(on ? ThemeColors.accent : Tokens.ramp[2], lineWidth: 1.5)
                    if on {
                        Text("✓")
                            .font(.system(size: 12, weight: .heavy))
                            .foregroundColor(ThemeColors.background)
                    }
                }
                .frame(width: 20, height: 20)

                AppTile(name: c.name, png: c.png ?? state.iconCache.getCachedIcon(forAppId: c.appId), fill: ThemeColors.background)

                VStack(alignment: .leading, spacing: 1) {
                    Text(c.name)
                        .font(.system(size: 13, weight: .bold))
                        .foregroundColor(ThemeColors.textPrimary)
                        .lineLimit(1)
                    Text(subtitle(c))
                        .font(.system(size: 10))
                        .foregroundColor(ThemeColors.textMuted)
                        .lineLimit(1)
                }
                Spacer(minLength: 0)
                Text(Formatters.hours(c.seconds))
                    .font(.system(size: 12))
                    .foregroundColor(ThemeColors.textDim)
            }
            .padding(.horizontal, 10)
            .frame(height: 50)
            .card(border: on ? ThemeColors.accent : ThemeColors.borderSubtle)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }

    private var preview: some View {
        let total = mainSeconds + ticked.reduce(0) { $0 + $1.seconds }
        let parts = ticked.prefix(3).map { "\($0.name) (\(Formatters.hours($0.seconds)))" }.joined(separator: " + ")
        return (Text("\(mainName) (\(Formatters.hours(mainSeconds))) + \(parts) = ") + Text(Formatters.hours(total)).bold() + Text(" under “\(mainName)”."))
            .font(.system(size: 12))
            .lineSpacing(2)
            .foregroundColor(ThemeColors.textPrimary)
            .padding(.horizontal, 12)
            .padding(.vertical, 10)
            .frame(maxWidth: .infinity, alignment: .leading)
            .card(border: ThemeColors.accent)
    }

    private var footnote: some View {
        let others = devices.filter { $0.deviceId != selectedDevice }
            .map { "\($0.isLocal ? "This Mac" : $0.name) \(countFor($0.deviceId))" }
            .joined(separator: " · ")
        return (Text("Ticked untracked apps start being tracked now and count under \(mainName). Ticks on other devices stay: ")
            + Text("\(others)").bold().foregroundColor(ThemeColors.textDim) + Text("."))
            .font(.system(size: 10))
            .lineSpacing(3)
            .foregroundColor(ThemeColors.textMuted)
            .fixedSize(horizontal: false, vertical: true)
    }

    private var bottomBar: some View {
        let change = changes
        let label = change.remove.isEmpty
            ? "Merge \(change.add.count) into \(mainName)"
            : "Apply \(change.add.count + change.remove.count) change\(change.add.count + change.remove.count == 1 ? "" : "s")"
        let enabled = !change.add.isEmpty || !change.remove.isEmpty
        return HStack {
            Button(action: onDismiss) {
                Text("Cancel")
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundColor(ThemeColors.textPrimary)
                    .padding(.horizontal, 14)
                    .frame(height: 30)
                    .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(ThemeColors.border, lineWidth: 1))
                    .contentShape(Rectangle())
            }
            .buttonStyle(.plain)

            Spacer()

            Button(action: apply) {
                Text(label)
                    .font(.system(size: 12, weight: .bold))
                    .foregroundColor(ThemeColors.background)
                    .lineLimit(1)
                    .padding(.horizontal, 14)
                    .frame(height: 30)
                    .background(RoundedRectangle(cornerRadius: 8).fill(ThemeColors.accent))
                    .opacity(enabled ? 1 : 0.4)
            }
            .buttonStyle(.plain)
            .disabled(!enabled)
        }
        .padding(.horizontal, 16)
        .frame(height: 50)
        .topRule()
    }

    private func loadPlan() {
        let lib = MacAppLibrary(db: state.db)
        guard let p = try? lib.openMergePlan(mainId: mainAppId) else { return }
        plan = p
        devices = state.readDevices().filter { $0.state == "active" }
        selectedDevice = devices.first { !$0.isLocal }?.deviceId ?? devices.first?.deviceId
        if let stmt = try? state.db.prepare("SELECT COALESCE(SUM(seconds), 0) FROM daily_usage WHERE app_id = ?1;") {
            try? stmt.bind(int64: mainAppId, at: 1)
            if (try? stmt.step()) == true { mainSeconds = stmt.columnInt64(at: 0) }
        }
    }

    private func apply() {
        let lib = MacAppLibrary(db: state.db)
        let change = changes
        try? lib.merge(mainId: mainAppId, candidateIds: change.add.map(\.appId))
        for c in change.remove { try? lib.unmerge(appId: c.appId) }
        state.refresh()
        onDismiss()
    }
}
