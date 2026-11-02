import SwiftUI
import TimeSinkCore

public struct AppSettingsSheet: View {
    @ObservedObject var state: AppState
    public let appId: Int64
    public let onDismiss: () -> Void

    @State private var displayName = ""
    @State private var kind: AppKind = .other
    @State private var isBackground = false
    @State private var main: TrackedAppItem?
    @State private var mergedChildren: [TrackedAppItem] = []

    @State private var appPath = ""
    @State private var addedUtc: Int64 = 0
    @State private var deviceId = ""
    @State private var totalSeconds: Int64 = 0
    @State private var loaded = false

    public init(state: AppState, appId: Int64, onDismiss: @escaping () -> Void) {
        self.state = state
        self.appId = appId
        self.onDismiss = onDismiss
    }

    private var addedDate: Date { Date(timeIntervalSince1970: TimeInterval(addedUtc)) }

    private func dateText(_ format: String) -> String {
        let df = DateFormatter()
        df.dateFormat = format
        return df.string(from: addedDate)
    }

    public var body: some View {
        ZStack {
            VStack(spacing: 0) {
                header
                ScrollView(.vertical, showsIndicators: false) {
                    VStack(alignment: .leading, spacing: 14) {
                        hero
                        settingsCard
                        mergedSection
                        aboutSection
                    }
                    .padding(.horizontal, 14)
                    .padding(.top, 4)
                    .padding(.bottom, 10)
                }
                dangerBar
            }
            .background(ThemeColors.background)

            if state.appSettingsShowingMerge {
                MergeView(state: state, mainAppId: appId) {
                    state.appSettingsShowingMerge = false
                    loadDetails()
                }
            }
        }
        .frame(width: 400, height: 640)
        .onAppear(perform: loadDetails)
        .alert("Stop tracking?", isPresented: $state.appSettingsShowingStopConfirm) {
            Button("Stop tracking", role: .destructive) {
                try? MacAppLibrary(db: state.db).stopTracking(appId: appId)
                state.refresh()
                onDismiss()
            }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text("TimeSink will stop tracking new sessions for this app. Existing history will be kept.")
        }
        .alert("Clear history?", isPresented: $state.appSettingsShowingClearConfirm) {
            Button("Clear all history", role: .destructive) {
                try? MacAppLibrary(db: state.db).clearHistory(appId: appId)
                state.refresh()
                loadDetails()
            }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text("This will permanently delete all recorded sessions and daily usage for this app.")
        }
    }

    private var header: some View {
        HStack(spacing: 8) {
            Button(action: onDismiss) {
                IconView(.back, size: 16, weight: 1.8, color: ThemeColors.textPrimary)
                    .frame(width: 28, height: 28)
                    .contentShape(Rectangle())
            }
            .buttonStyle(.plain)

            Text("App settings")
                .font(.system(size: 17, weight: .bold))
                .foregroundColor(ThemeColors.textPrimary)
                .frame(maxWidth: .infinity, alignment: .leading)

            Text("Changes save as you make them")
                .font(.system(size: 10))
                .foregroundColor(ThemeColors.textMuted)
        }
        .padding(.horizontal, 12)
        .frame(height: 50)
    }

    private var hero: some View {
        HStack(spacing: 12) {
            AppTile(name: displayName, png: state.iconCache.getCachedIcon(forAppId: appId, fallbackExePath: appPath), size: 48, radius: 12, fontSize: 22)
            VStack(alignment: .leading, spacing: 4) {
                TextField("App name", text: $displayName)
                    .textFieldStyle(.plain)
                    .font(.system(size: 18, weight: .bold))
                    .foregroundColor(ThemeColors.textPrimary)
                    .fixedSize()
                    .padding(.horizontal, 8)
                    .padding(.vertical, 3)
                    .overlay(RoundedRectangle(cornerRadius: 6).strokeBorder(ThemeColors.accent, lineWidth: 1))
                    .onChange(of: displayName) { _, _ in saveChanges() }
                Text("\(Formatters.hours(totalSeconds)) tracked · since \(dateText("MMM yyyy"))")
                    .font(.system(size: 11))
                    .foregroundColor(ThemeColors.textMuted)
            }
            Spacer(minLength: 0)
        }
        .padding(.horizontal, 4)
        .padding(.top, 6)
    }

    private func row<Trailing: View>(_ title: String, _ sub: String, divider: Bool = true, @ViewBuilder trailing: () -> Trailing) -> some View {
        HStack(spacing: 10) {
            VStack(alignment: .leading, spacing: 1) {
                Text(title)
                    .font(.system(size: 13))
                    .foregroundColor(ThemeColors.textPrimary)
                Text(sub)
                    .font(.system(size: 11))
                    .foregroundColor(ThemeColors.textMuted)
                    .lineLimit(1)
            }
            Spacer(minLength: 0)
            trailing()
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 6)
        .frame(minHeight: 46)
        .topRule(divider ? ThemeColors.borderSubtle : .clear)
    }

    private var settingsCard: some View {
        VStack(spacing: 0) {
            row("Game type", "Used by Year › Wrapped", divider: false) {
                Segmented(
                    options: [(AppKind.other, "Other"), (.game, "Game"), (.gacha, "Gacha")],
                    selection: Binding(get: { kind }, set: { kind = $0; saveChanges() }),
                    hPad: 9,
                    radius: 7,
                    fill: Tokens.well,
                    border: ThemeColors.borderSubtle
                )
            }
            row("Background", "Runs all day. Still counted, but ranked after your other apps") {
                TSSwitch(isOn: Binding(get: { isBackground }, set: { isBackground = $0; saveChanges() }))
            }
            row("Icon", loaded ? "Detected from the app" : "") {
                Text("Change")
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundColor(ThemeColors.accent)
            }
        }
        .card()
    }

    private func deviceLabel(_ id: String) -> String {
        state.deviceDisplayName(id)
    }

    private func bundleText(_ path: String) -> String {
        if let bundle = MacIconCache.resolveBundlePath(from: path).flatMap({ Bundle(path: $0) }), let id = bundle.bundleIdentifier {
            return id
        }
        return (path as NSString).lastPathComponent
    }

    private func mergedRow(name: String, device: String, path: String, seconds: Int64, role: String, divider: Bool, childId: Int64?) -> some View {
        HStack(spacing: 10) {
            DeviceMarker(isLocal: state.isLocalDevice(device), size: 7)
            VStack(alignment: .leading, spacing: 0) {
                Text(name)
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundColor(ThemeColors.textPrimary)
                Text(state.isLocalDevice(device) ? "Mac · \(bundleText(path)) · \(role)" : "\(deviceLabel(device)) · \(role)")
                    .font(.system(size: 10))
                    .foregroundColor(ThemeColors.textMuted)
                    .lineLimit(1)
            }
            Spacer(minLength: 0)
            Text(Formatters.hours(seconds))
                .font(.system(size: 12))
                .foregroundColor(ThemeColors.textDim)
            if let childId {
                Button(action: {
                    try? MacAppLibrary(db: state.db).unmerge(appId: childId)
                    state.refresh()
                    loadDetails()
                }) {
                    IconView(.close, size: 10, weight: 1.6, color: ThemeColors.textMuted)
                        .frame(width: 22, height: 22)
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .help("Unmerge")
            }
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 9)
        .topRule(divider ? ThemeColors.borderSubtle : .clear)
    }

    private var mergedSection: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text("MERGED APPS").caps(10)
                Spacer()
                Button(action: { state.appSettingsShowingMerge = true }) {
                    Text("Merge…")
                        .font(.system(size: 11, weight: .semibold))
                        .foregroundColor(ThemeColors.accent)
                }
                .buttonStyle(.plain)
            }
            .padding(.horizontal, 4)

            VStack(spacing: 0) {
                mergedRow(
                    name: displayName,
                    device: deviceId,
                    path: appPath,
                    seconds: totalSeconds - mergedChildren.reduce(0) { $0 + $1.totalSeconds },
                    role: "main app",
                    divider: false,
                    childId: nil
                )
                ForEach(mergedChildren) { child in
                    mergedRow(name: child.displayName, device: child.deviceId, path: child.exePath, seconds: child.totalSeconds, role: "merged", divider: true, childId: child.id)
                }
            }
            .card()

            Text("Merged time is added together in every view. Sessions keep their own device.")
                .font(.system(size: 10))
                .foregroundColor(ThemeColors.textMuted)
                .padding(.horizontal, 4)
        }
    }

    private var aboutSection: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text("ABOUT").caps(10).padding(.horizontal, 4)
            VStack(spacing: 0) {
                aboutRow("Path", MacIconCache.resolveBundlePath(from: appPath) ?? appPath, divider: false)
                aboutRow("First tracked", dateText("d MMM yyyy"), divider: true)
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 4)
            .card()
        }
    }

    private func aboutRow(_ label: String, _ value: String, divider: Bool) -> some View {
        HStack {
            Text(label).foregroundColor(ThemeColors.textMuted)
            Spacer()
            Text(value)
                .foregroundColor(ThemeColors.textDim)
                .lineLimit(1)
                .truncationMode(.middle)
        }
        .font(.system(size: 11))
        .padding(.vertical, 6)
        .topRule(divider ? ThemeColors.borderSubtle : .clear)
    }

    private func dangerButton(_ title: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Text(title)
                .font(.system(size: 12, weight: .semibold))
                .foregroundColor(ThemeColors.danger)
                .frame(maxWidth: .infinity)
                .frame(height: 32)
                .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(Tokens.dangerBorder, lineWidth: 1))
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }

    private var dangerBar: some View {
        HStack(spacing: 10) {
            dangerButton("Stop tracking") { state.appSettingsShowingStopConfirm = true }
            dangerButton("Clear history…") { state.appSettingsShowingClearConfirm = true }
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
        .topRule()
    }

    private func loadDetails() {
        let lib = MacAppLibrary(db: state.db)
        do {
            let stmt = try state.db.prepare("SELECT display_name, kind, is_background, exe_path, added_utc, device_id FROM apps WHERE id = ?1;")
            try stmt.bind(int64: appId, at: 1)
            if try stmt.step() {
                displayName = stmt.columnText(at: 0) ?? ""
                let rawKind = stmt.columnIsNull(at: 1) ? nil : stmt.columnInt64(at: 1)
                isBackground = stmt.columnInt64(at: 2) == 1
                appPath = stmt.columnText(at: 3) ?? ""
                addedUtc = stmt.columnInt64(at: 4)
                deviceId = stmt.columnText(at: 5) ?? ""
                kind = AppKinds.resolve(kind: rawKind, exePath: appPath, displayName: displayName)
            }
            mergedChildren = try lib.getMergedChildren(mainId: appId)
            totalSeconds = (try? lib.getActiveSeconds(appId: appId)) ?? 0
            if totalSeconds == 0 {
                let usage = try state.db.prepare("SELECT COALESCE(SUM(seconds), 0) FROM daily_usage WHERE app_id = ?1 OR app_id IN (SELECT id FROM apps WHERE canonical_id = ?1);")
                try usage.bind(int64: appId, at: 1)
                if try usage.step() { totalSeconds = usage.columnInt64(at: 0) }
            }
            loaded = true
        } catch {
            NSLog("Failed to load app settings: \(error)")
        }
    }

    private func saveChanges() {
        guard loaded, !displayName.isEmpty else { return }
        try? MacAppLibrary(db: state.db).updateApp(appId: appId, displayName: displayName, kind: kind, isBackground: isBackground)
        state.refresh()
    }
}
