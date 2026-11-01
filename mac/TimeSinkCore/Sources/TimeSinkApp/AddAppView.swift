import SwiftUI
import AppKit
import TimeSinkCore

public enum AddAppTab: String, CaseIterable, Identifiable {
    case running = "Running"
    case installed = "Installed"
    case tracked = "Tracked"
    case browse = "Browse…"

    public var id: String { rawValue }
}

public struct AddAppView: View {
    @ObservedObject var state: AppState
    public let onDismiss: () -> Void

    @State private var activeTab: AddAppTab = .running
    @State private var searchQuery: String = ""
    @State private var addedThisSession: Int = 0

    // Data lists
    @State private var runningApps: [ProcInfo] = []
    @State private var installedApps: [InstalledAppItem] = []
    @State private var trackedApps: [TrackedAppItem] = []

    // Navigation to app settings
    @State private var editingAppId: Int64?

    public init(state: AppState, onDismiss: @escaping () -> Void) {
        self.state = state
        self.onDismiss = onDismiss
    }

    public var body: some View {
        VStack(spacing: 0) {
            // Header bar
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

                Text("Add Application")
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundColor(ThemeColors.textPrimary)

                Spacer()

                if addedThisSession > 0 {
                    Text("\(addedThisSession) added")
                        .font(.system(size: 10, weight: .semibold))
                        .foregroundColor(ThemeColors.background)
                        .padding(.horizontal, 6)
                        .padding(.vertical, 2)
                        .background(ThemeColors.accent)
                        .cornerRadius(8)
                }
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

            // Tabs bar
            HStack(spacing: 0) {
                ForEach(AddAppTab.allCases) { tab in
                    Button(action: {
                        activeTab = tab
                        if tab == .browse {
                            openFileBrowser()
                        }
                    }) {
                        VStack(spacing: 5) {
                            Text(tab.rawValue)
                                .font(.system(size: 12, weight: activeTab == tab ? .semibold : .medium))
                                .foregroundColor(activeTab == tab ? ThemeColors.textPrimary : ThemeColors.textMuted)

                            Rectangle()
                                .fill(activeTab == tab ? ThemeColors.accent : Color.clear)
                                .frame(height: 2)
                        }
                        .frame(maxWidth: .infinity)
                    }
                    .buttonStyle(.plain)
                }
            }
            .padding(.horizontal, 12)
            .padding(.top, 6)
            .background(ThemeColors.background)

            // Search box for installed or tracked
            if activeTab == .running || activeTab == .installed || activeTab == .tracked {
                HStack(spacing: 6) {
                    Image(systemName: "magnifyingglass")
                        .font(.system(size: 11))
                        .foregroundColor(ThemeColors.textDim)

                    TextField("Search...", text: $searchQuery)
                        .textFieldStyle(.plain)
                        .font(.system(size: 12))
                        .foregroundColor(ThemeColors.textPrimary)

                    if !searchQuery.isEmpty {
                        Button(action: { searchQuery = "" }) {
                            Image(systemName: "xmark.circle.fill")
                                .font(.system(size: 11))
                                .foregroundColor(ThemeColors.textDim)
                        }
                        .buttonStyle(.plain)
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
                .padding(.vertical, 8)
            }

            // Tab Content
            ScrollView {
                VStack(spacing: 4) {
                    switch activeTab {
                    case .running:
                        runningTabView()
                    case .installed:
                        installedTabView()
                    case .tracked:
                        trackedTabView()
                    case .browse:
                        browseTabView()
                    }
                }
                .padding(.horizontal, 16)
                .padding(.vertical, 6)
            }
        }
        .frame(width: 400, height: 640)
        .background(ThemeColors.background)
        .onAppear {
            reloadAll()
        }
        .sheet(item: Binding<TrackedAppItem?>(
            get: { trackedApps.first(where: { $0.id == editingAppId }) },
            set: { editingAppId = $0?.id }
        )) { item in
            AppSettingsSheet(state: state, appId: item.id) {
                editingAppId = nil
                reloadAll()
            }
        }
    }

    private func reloadAll() {
        let wsProvider = NSWorkspaceProcessProvider()
        self.runningApps = wsProvider.snapshot().filter { !($0.bundleId?.contains("com.apple") ?? false) }

        DispatchQueue.global(qos: .userInitiated).async {
            let installed = MacAppLibrary.scanInstalledApplications()
            DispatchQueue.main.async {
                self.installedApps = installed
            }
        }

        let lib = MacAppLibrary(db: state.db)
        self.trackedApps = (try? lib.getTrackedApps()) ?? []
    }

    // MARK: - Running Tab

    private func runningTabView() -> some View {
        let trackedPaths = Set(trackedApps.map(\.exePath))
        let filtered = runningApps.filter {
            searchQuery.isEmpty || $0.name.localizedCaseInsensitiveContains(searchQuery)
        }

        return ForEach(filtered, id: \.pid) { proc in
            let path = proc.fullPath ?? ""
            let isTracked = trackedPaths.contains(path)

            HStack(spacing: 10) {
                appTile(name: proc.name, path: path)

                VStack(alignment: .leading, spacing: 2) {
                    Text(proc.name)
                        .font(.system(size: 13, weight: .medium))
                        .foregroundColor(ThemeColors.textPrimary)

                    if let bundle = proc.bundleId {
                        Text(bundle)
                            .font(.system(size: 10))
                            .foregroundColor(ThemeColors.textDim)
                            .lineLimit(1)
                    }
                }

                Spacer()

                if isTracked {
                    Text("Tracked")
                        .font(.system(size: 11, weight: .medium))
                        .foregroundColor(ThemeColors.accent)
                } else {
                    Button(action: {
                        trackApp(path: path, name: proc.name)
                    }) {
                        HStack(spacing: 4) {
                            Image(systemName: "plus")
                                .font(.system(size: 10, weight: .bold))
                            Text("Track")
                                .font(.system(size: 11, weight: .medium))
                        }
                        .foregroundColor(ThemeColors.background)
                        .padding(.horizontal, 8)
                        .padding(.vertical, 4)
                        .background(ThemeColors.accent)
                        .cornerRadius(4)
                    }
                    .buttonStyle(.plain)
                }
            }
            .padding(.horizontal, 10)
            .padding(.vertical, 7)
            .background(ThemeColors.surface)
            .cornerRadius(6)
        }
    }

    // MARK: - Installed Tab

    private func installedTabView() -> some View {
        let trackedPaths = Set(trackedApps.map(\.exePath))
        let filtered = installedApps.filter {
            searchQuery.isEmpty || $0.name.localizedCaseInsensitiveContains(searchQuery)
        }

        return ForEach(filtered) { app in
            let isTracked = trackedPaths.contains(app.executablePath) || trackedPaths.contains(app.bundleUrl.path)

            HStack(spacing: 10) {
                appTile(name: app.name, path: app.bundleUrl.path)

                VStack(alignment: .leading, spacing: 2) {
                    Text(app.name)
                        .font(.system(size: 13, weight: .medium))
                        .foregroundColor(ThemeColors.textPrimary)

                    Text(app.bundleUrl.path)
                        .font(.system(size: 10))
                        .foregroundColor(ThemeColors.textDim)
                        .lineLimit(1)
                }

                Spacer()

                if isTracked {
                    Text("Tracked")
                        .font(.system(size: 11, weight: .medium))
                        .foregroundColor(ThemeColors.accent)
                } else {
                    Button(action: {
                        trackApp(path: app.executablePath, name: app.name)
                    }) {
                        HStack(spacing: 4) {
                            Image(systemName: "plus")
                                .font(.system(size: 10, weight: .bold))
                            Text("Track")
                                .font(.system(size: 11, weight: .medium))
                        }
                        .foregroundColor(ThemeColors.background)
                        .padding(.horizontal, 8)
                        .padding(.vertical, 4)
                        .background(ThemeColors.accent)
                        .cornerRadius(4)
                    }
                    .buttonStyle(.plain)
                }
            }
            .padding(.horizontal, 10)
            .padding(.vertical, 7)
            .background(ThemeColors.surface)
            .cornerRadius(6)
        }
    }

    // MARK: - Tracked Tab

    private func trackedTabView() -> some View {
        let filtered = trackedApps.filter {
            searchQuery.isEmpty || $0.displayName.localizedCaseInsensitiveContains(searchQuery)
        }

        return ForEach(filtered) { app in
            Button(action: {
                editingAppId = app.id
            }) {
                HStack(spacing: 10) {
                    appTile(name: app.displayName, path: app.exePath)

                    VStack(alignment: .leading, spacing: 2) {
                        HStack(spacing: 6) {
                            Text(app.displayName)
                                .font(.system(size: 13, weight: .medium))
                                .foregroundColor(ThemeColors.textPrimary)

                            DeviceMarker(isLocal: app.deviceId.caseInsensitiveCompare(state.localDeviceId) == .orderedSame, size: 6)

                            if app.mergedCount > 0 {
                                Text("+\(app.mergedCount)")
                                    .font(.system(size: 9, weight: .bold))
                                    .foregroundColor(ThemeColors.background)
                                    .padding(.horizontal, 4)
                                    .padding(.vertical, 1)
                                    .background(ThemeColors.accent)
                                    .cornerRadius(6)
                            }
                        }

                        Text(app.exePath)
                            .font(.system(size: 10))
                            .foregroundColor(ThemeColors.textDim)
                            .lineLimit(1)
                    }

                    Spacer()

                    if app.totalSeconds > 0 {
                        Text(Formatters.hoursMinutes(seconds: app.totalSeconds))
                            .font(.system(size: 11, design: .monospaced))
                            .foregroundColor(ThemeColors.textMuted)
                    }

                    Image(systemName: "gearshape")
                        .font(.system(size: 12))
                        .foregroundColor(ThemeColors.textDim)
                }
                .padding(.horizontal, 10)
                .padding(.vertical, 7)
                .background(ThemeColors.surface)
                .cornerRadius(6)
            }
            .buttonStyle(.plain)
        }
    }

    // MARK: - Browse Tab

    private func browseTabView() -> some View {
        VStack(spacing: 12) {
            Image(systemName: "folder")
                .font(.system(size: 32))
                .foregroundColor(ThemeColors.accent)
                .padding(.top, 40)

            Text("Browse for Applications")
                .font(.system(size: 14, weight: .semibold))
                .foregroundColor(ThemeColors.textPrimary)

            Text("Select any .app bundle or executable binary on your Mac.")
                .font(.system(size: 12))
                .foregroundColor(ThemeColors.textDim)
                .multilineTextAlignment(.center)
                .padding(.horizontal, 32)

            Button(action: openFileBrowser) {
                Text("Open File Chooser…")
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundColor(ThemeColors.background)
                    .padding(.horizontal, 16)
                    .padding(.vertical, 8)
                    .background(ThemeColors.accent)
                    .cornerRadius(6)
            }
            .buttonStyle(.plain)
            .padding(.top, 8)
        }
        .frame(maxWidth: .infinity)
    }

    private func appTile(name: String, path: String) -> some View {
        ZStack {
            RoundedRectangle(cornerRadius: 6)
                .fill(ThemeColors.surfaceRaised)
                .frame(width: 28, height: 28)

            if let png = state.iconCache.iconData(forFilePath: path, targetSize: 28),
               let nsImg = NSImage(data: png) {
                Image(nsImage: nsImg)
                    .resizable()
                    .frame(width: 20, height: 20)
                    .cornerRadius(3)
            } else {
                Text(String(name.prefix(1)).uppercased())
                    .font(.system(size: 12, weight: .bold))
                    .foregroundColor(ThemeColors.accent)
            }
        }
    }

    private func trackApp(path: String, name: String) {
        let lib = MacAppLibrary(db: state.db)
        do {
            let appId = try lib.trackApp(
                exePath: path,
                displayName: name,
                category: "Other",
                kind: .other,
                deviceId: state.localDeviceId
            )
            // Cache icon
            if let png = state.iconCache.iconData(forFilePath: path, targetSize: 64) {
                state.iconCache.saveIcon(forAppId: appId, hash: "extracted:\(path)", png: png)
            }
            addedThisSession += 1
            reloadAll()
            state.refresh()
        } catch {
            NSLog("Failed to track app: \(error)")
        }
    }

    private func openFileBrowser() {
        let panel = NSOpenPanel()
        panel.canChooseFiles = true
        panel.canChooseDirectories = false
        panel.allowsMultipleSelection = false
        panel.allowedContentTypes = [.application, .unixExecutable]

        if panel.runModal() == .OK, let url = panel.url {
            let path = url.path
            let bundle = Bundle(url: url)
            let name = bundle?.localizedInfoDictionary?["CFBundleDisplayName"] as? String
                ?? bundle?.infoDictionary?["CFBundleName"] as? String
                ?? url.deletingPathExtension().lastPathComponent
            let execPath = bundle?.executableURL?.path ?? path
            trackApp(path: execPath, name: name)
        }
    }
}
