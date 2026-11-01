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
                        Text("Add app")
                            .font(.system(size: 14, weight: .bold))
                            .foregroundColor(ThemeColors.textPrimary)
                    }
                    .foregroundColor(ThemeColors.textPrimary)
                }
                .buttonStyle(.plain)

                Spacer()

                Button(action: onDismiss) {
                    Image(systemName: "xmark")
                        .font(.system(size: 12, weight: .semibold))
                        .foregroundColor(ThemeColors.textMuted)
                        .frame(width: 24, height: 24)
                }
                .buttonStyle(.plain)
            }
            .padding(.horizontal, 16)
            .padding(.vertical, 10)
            .background(ThemeColors.surface)

            // Tabs bar
            HStack(spacing: 0) {
                ForEach(AddAppTab.allCases) { tab in
                    Button(action: {
                        activeTab = tab
                        if tab == .browse {
                            // Keep in browse tab
                        }
                    }) {
                        VStack(spacing: 6) {
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
            .padding(.horizontal, 16)
            .padding(.top, 6)
            .background(ThemeColors.background)

            // Tab Content
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

            // Bottom Footer Bar
            HStack {
                if activeTab == .tracked {
                    Text("Double-click an app for its settings")
                        .font(.system(size: 11))
                        .foregroundColor(ThemeColors.textDim)
                } else {
                    Text("\(addedThisSession) added this session")
                        .font(.system(size: 11))
                        .foregroundColor(ThemeColors.textDim)
                }

                Spacer()

                Button(action: onDismiss) {
                    Text("Done")
                        .font(.system(size: 12, weight: .semibold))
                        .foregroundColor(ThemeColors.textPrimary)
                        .padding(.horizontal, 16)
                        .padding(.vertical, 6)
                        .background(ThemeColors.surfaceRaised)
                        .cornerRadius(6)
                        .overlay(
                            RoundedRectangle(cornerRadius: 6)
                                .stroke(ThemeColors.border, lineWidth: 1)
                        )
                }
                .buttonStyle(.plain)
            }
            .padding(.horizontal, 16)
            .padding(.vertical, 8)
            .background(ThemeColors.surface)
            .overlay(
                Rectangle()
                    .frame(height: 1)
                    .foregroundColor(ThemeColors.borderSubtle),
                alignment: .top
            )
        }
        .frame(width: 400, height: 640)
        .background(ThemeColors.background)
        .onAppear {
            reloadAll()
        }
        .sheet(item: Binding<IdentifiableInt64?>(
            get: { editingAppId.map { IdentifiableInt64(id: $0) } },
            set: { editingAppId = $0?.id }
        )) { item in
            AppSettingsSheet(state: state, appId: item.id) {
                editingAppId = nil
                reloadAll()
            }
        }
    }

    // MARK: - Running Tab (Page 13)

    private func runningTabView() -> some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 10) {
                Text("Open on your Mac right now")
                    .font(.system(size: 11))
                    .foregroundColor(ThemeColors.textDim)
                    .padding(.top, 4)

                let mockRunning: [(initial: String, name: String, openTime: String, isTracked: Bool)] = [
                    ("N", "Notion", "open 2h 14m", false),
                    ("Z", "Zoom", "open 48m", true),
                    ("T", "Terminal", "open 5h 02m", false),
                    ("P", "Preview", "open 12m", false),
                    ("M", "Mail", "open 6h 30m", false)
                ]

                ForEach(mockRunning, id: \.name) { app in
                    HStack(spacing: 12) {
                        ZStack {
                            RoundedRectangle(cornerRadius: 6)
                                .fill(ThemeColors.surfaceRaised)
                                .frame(width: 32, height: 32)
                            Text(app.initial)
                                .font(.system(size: 13, weight: .bold))
                                .foregroundColor(ThemeColors.textPrimary)
                        }

                        VStack(alignment: .leading, spacing: 2) {
                            Text(app.name)
                                .font(.system(size: 13, weight: .semibold))
                                .foregroundColor(ThemeColors.textPrimary)
                            Text(app.openTime)
                                .font(.system(size: 11))
                                .foregroundColor(ThemeColors.textDim)
                        }

                        Spacer()

                        if app.isTracked {
                            // Green bordered circle with eye
                            ZStack {
                                Circle()
                                    .stroke(ThemeColors.accent, lineWidth: 1.5)
                                    .background(Circle().fill(ThemeColors.surfaceRaised))
                                    .frame(width: 28, height: 28)

                                Image(systemName: "eye.fill")
                                    .font(.system(size: 11))
                                    .foregroundColor(ThemeColors.accent)
                            }
                        } else {
                            // Ignore circle & Track lime pill
                            HStack(spacing: 8) {
                                Button(action: {}) {
                                    ZStack {
                                        Circle()
                                            .fill(ThemeColors.surfaceRaised)
                                            .frame(width: 28, height: 28)
                                        Image(systemName: "eye.slash")
                                            .font(.system(size: 11))
                                            .foregroundColor(ThemeColors.textMuted)
                                    }
                                }
                                .buttonStyle(.plain)

                                Button(action: {
                                    addedThisSession += 1
                                }) {
                                    ZStack {
                                        Capsule()
                                            .fill(ThemeColors.accent)
                                            .frame(width: 44, height: 26)

                                        Image(systemName: "eye")
                                            .font(.system(size: 12, weight: .bold))
                                            .foregroundColor(ThemeColors.background)
                                    }
                                }
                                .buttonStyle(.plain)
                            }
                        }
                    }
                    .padding(.horizontal, 10)
                    .padding(.vertical, 6)
                    .background(ThemeColors.surface)
                    .cornerRadius(6)
                }

                // Don't see it card
                HStack {
                    Text("Don’t see it? Open it first, or look in ")
                        .font(.system(size: 11))
                        .foregroundColor(ThemeColors.textDim)
                    + Text("Installed")
                        .font(.system(size: 11, weight: .semibold))
                        .foregroundColor(ThemeColors.accent)
                    + Text(".")
                        .font(.system(size: 11))
                        .foregroundColor(ThemeColors.textDim)
                }
                .frame(maxWidth: .infinity, alignment: .center)
                .padding(14)
                .background(ThemeColors.surface)
                .cornerRadius(6)
                .overlay(
                    RoundedRectangle(cornerRadius: 6)
                        .stroke(ThemeColors.borderSubtle, style: StrokeStyle(lineWidth: 1, dash: [4, 4]))
                )
                .onTapGesture {
                    activeTab = .installed
                }
                .padding(.top, 8)
            }
            .padding(16)
        }
    }

    // MARK: - Installed Tab (Page 14)

    private func installedTabView() -> some View {
        VStack(spacing: 8) {
            // Search box
            HStack(spacing: 6) {
                Image(systemName: "magnifyingglass")
                    .font(.system(size: 11))
                    .foregroundColor(ThemeColors.textDim)

                TextField("Search installed apps", text: $searchQuery)
                    .textFieldStyle(.plain)
                    .font(.system(size: 12))
                    .foregroundColor(ThemeColors.textPrimary)
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
            .padding(.top, 6)

            HStack(alignment: .top, spacing: 0) {
                ScrollView {
                    VStack(alignment: .leading, spacing: 10) {
                        // Section A
                        installedSectionHeader("A")
                        installedRow(initial: "A", name: "Activity Monitor", sub: "Apple", isTracked: false)
                        installedRow(initial: "A", name: "Affinity Photo", sub: "Serif", isTracked: false)
                        installedRow(initial: "A", name: "Antigravity IDE", sub: "Google LLC", isTracked: true)
                        installedRow(initial: "A", name: "App Store", sub: "Apple", isTracked: false)

                        // Section C
                        installedSectionHeader("C")
                        installedRow(initial: "C", name: "Chrome", sub: "Google LLC · Mac and PC", isTracked: true)
                        installedRow(initial: "C", name: "Calendar", sub: "Apple", isTracked: false)

                        // Section H
                        installedSectionHeader("H")
                        installedRow(initial: "H", name: "Hades", sub: "Supergiant Games", isTracked: true)

                        // Section S
                        installedSectionHeader("S")
                        HStack(spacing: 12) {
                            ZStack {
                                RoundedRectangle(cornerRadius: 6)
                                    .fill(ThemeColors.surfaceRaised)
                                    .frame(width: 32, height: 32)
                                Text("S")
                                    .font(.system(size: 13, weight: .bold))
                                    .foregroundColor(ThemeColors.textPrimary)
                            }

                            VStack(alignment: .leading, spacing: 2) {
                                Text("Steam")
                                    .font(.system(size: 13, weight: .semibold))
                                    .foregroundColor(ThemeColors.textPrimary)
                                Text("Stopped · history kept")
                                    .font(.system(size: 11))
                                    .foregroundColor(ThemeColors.textDim)
                            }

                            Spacer()

                            Button(action: {}) {
                                Text("Restore")
                                    .font(.system(size: 11, weight: .medium))
                                    .foregroundColor(ThemeColors.textMuted)
                                    .padding(.horizontal, 10)
                                    .padding(.vertical, 4)
                                    .background(ThemeColors.surfaceRaised)
                                    .cornerRadius(4)
                            }
                            .buttonStyle(.plain)
                        }
                        .padding(.horizontal, 10)
                        .padding(.vertical, 6)
                        .background(ThemeColors.surface)
                        .cornerRadius(6)
                    }
                    .padding(.horizontal, 16)
                }

                // Alphabetical fast index
                VStack(spacing: 3) {
                    ForEach(["A", "B", "C", "D", "F", "I", "N", "P", "S", "X", "Z"], id: \.self) { letter in
                        Text(letter)
                            .font(.system(size: 9, weight: .semibold))
                            .foregroundColor(ThemeColors.textDim)
                    }
                }
                .padding(.trailing, 8)
                .padding(.top, 4)
            }
        }
    }

    private func installedSectionHeader(_ letter: String) -> some View {
        Text(letter)
            .font(.system(size: 11, weight: .bold))
            .foregroundColor(ThemeColors.textDim)
            .padding(.top, 4)
    }

    private func installedRow(initial: String, name: String, sub: String, isTracked: Bool) -> some View {
        HStack(spacing: 12) {
            ZStack {
                RoundedRectangle(cornerRadius: 6)
                    .fill(ThemeColors.surfaceRaised)
                    .frame(width: 32, height: 32)
                Text(initial)
                    .font(.system(size: 13, weight: .bold))
                    .foregroundColor(ThemeColors.textPrimary)
            }

            VStack(alignment: .leading, spacing: 2) {
                Text(name)
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundColor(ThemeColors.textPrimary)
                Text(sub)
                    .font(.system(size: 11))
                    .foregroundColor(ThemeColors.textDim)
            }

            Spacer()

            if isTracked {
                HStack(spacing: 6) {
                    ZStack {
                        RoundedRectangle(cornerRadius: 4)
                            .stroke(ThemeColors.border, lineWidth: 1)
                            .frame(width: 22, height: 18)
                        Image(systemName: "plus")
                            .font(.system(size: 8, weight: .bold))
                            .foregroundColor(ThemeColors.textMuted)
                    }

                    Image(systemName: "square.3.layers.3d")
                        .font(.system(size: 12))
                        .foregroundColor(ThemeColors.textMuted)

                    ZStack {
                        Circle()
                            .stroke(ThemeColors.accent, lineWidth: 1.5)
                            .background(Circle().fill(ThemeColors.surfaceRaised))
                            .frame(width: 24, height: 24)

                        Image(systemName: "eye.fill")
                            .font(.system(size: 10))
                            .foregroundColor(ThemeColors.accent)
                    }
                }
            } else {
                Button(action: { addedThisSession += 1 }) {
                    ZStack {
                        Capsule()
                            .fill(ThemeColors.accent)
                            .frame(width: 44, height: 26)
                        Image(systemName: "eye")
                            .font(.system(size: 12, weight: .bold))
                            .foregroundColor(ThemeColors.background)
                    }
                }
                .buttonStyle(.plain)
            }
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 6)
        .background(ThemeColors.surface)
        .cornerRadius(6)
    }

    // MARK: - Tracked Tab (Page 15)

    private func trackedTabView() -> some View {
        VStack(spacing: 8) {
            // Search box
            HStack(spacing: 6) {
                Image(systemName: "magnifyingglass")
                    .font(.system(size: 11))
                    .foregroundColor(ThemeColors.textDim)

                TextField("Search tracked apps", text: $searchQuery)
                    .textFieldStyle(.plain)
                    .font(.system(size: 12))
                    .foregroundColor(ThemeColors.textPrimary)
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
            .padding(.top, 6)

            ScrollView {
                VStack(alignment: .leading, spacing: 6) {
                    Text("TRACKED NOW")
                        .font(.system(size: 10, weight: .bold))
                        .foregroundColor(ThemeColors.textDim)
                        .padding(.top, 4)

                    let trackedList: [(id: Int64, initial: String, name: String, vendor: String, badge: Int?)] = [
                        (1, "X", "Xcode", "Apple", nil),
                        (2, "C", "Chrome", "Google LLC", 3),
                        (3, "H", "Hades", "Supergiant Games", nil),
                        (4, "B", "Balatro", "Steam", nil),
                        (5, "F", "Figma", "Figma, Inc.", nil),
                        (6, "S", "Spotify", "Spotify AB", 2),
                        (7, "L", "LM Studio", "LM Studio", nil)
                    ]

                    ForEach(trackedList, id: \.id) { app in
                        HStack(spacing: 12) {
                            ZStack(alignment: .bottomTrailing) {
                                RoundedRectangle(cornerRadius: 6)
                                    .fill(ThemeColors.surfaceRaised)
                                    .frame(width: 32, height: 32)

                                Text(app.initial)
                                    .font(.system(size: 13, weight: .bold))
                                    .foregroundColor(ThemeColors.textPrimary)

                                if let b = app.badge {
                                    Text("\(b)")
                                        .font(.system(size: 8, weight: .bold))
                                        .foregroundColor(ThemeColors.background)
                                        .padding(2)
                                        .background(Circle().fill(ThemeColors.accent))
                                        .offset(x: 3, y: 3)
                                }
                            }

                            VStack(alignment: .leading, spacing: 2) {
                                Text(app.name)
                                    .font(.system(size: 13, weight: .semibold))
                                    .foregroundColor(ThemeColors.textPrimary)

                                Text(app.vendor)
                                    .font(.system(size: 11))
                                    .foregroundColor(ThemeColors.textDim)
                            }

                            Spacer()

                            HStack(spacing: 8) {
                                ZStack {
                                    RoundedRectangle(cornerRadius: 4)
                                        .stroke(ThemeColors.border, lineWidth: 1)
                                        .frame(width: 22, height: 18)
                                    Image(systemName: "plus")
                                        .font(.system(size: 8, weight: .bold))
                                        .foregroundColor(ThemeColors.textMuted)
                                }

                                Image(systemName: "square.3.layers.3d")
                                    .font(.system(size: 12))
                                    .foregroundColor(ThemeColors.textMuted)

                                ZStack {
                                    Circle()
                                        .stroke(ThemeColors.accent, lineWidth: 1.5)
                                        .background(Circle().fill(ThemeColors.surfaceRaised))
                                        .frame(width: 24, height: 24)

                                    Image(systemName: "eye.fill")
                                        .font(.system(size: 10))
                                        .foregroundColor(ThemeColors.accent)
                                }
                            }
                        }
                        .padding(.horizontal, 10)
                        .padding(.vertical, 7)
                        .background(ThemeColors.surface)
                        .cornerRadius(6)
                        .onTapGesture(count: 2) {
                            editingAppId = app.id
                        }
                    }
                }
                .padding(.horizontal, 16)
            }
        }
    }

    // MARK: - Browse Tab (Page 17)

    private func browseTabView() -> some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                // Dashed drop zone
                VStack(spacing: 8) {
                    Image(systemName: "arrow.up")
                        .font(.system(size: 20, weight: .bold))
                        .foregroundColor(ThemeColors.accent)

                    Text("Drop an .app or alias here")
                        .font(.system(size: 13, weight: .semibold))
                        .foregroundColor(ThemeColors.textPrimary)

                    Text("Works for apps and games in any folder")
                        .font(.system(size: 11))
                        .foregroundColor(ThemeColors.textDim)

                    Button(action: openFileBrowser) {
                        Text("Choose file…")
                            .font(.system(size: 12, weight: .semibold))
                            .foregroundColor(ThemeColors.background)
                            .padding(.horizontal, 18)
                            .padding(.vertical, 7)
                            .background(ThemeColors.accent)
                            .cornerRadius(6)
                    }
                    .buttonStyle(.plain)
                    .padding(.top, 4)
                }
                .frame(maxWidth: .infinity)
                .padding(.vertical, 24)
                .background(ThemeColors.surface)
                .cornerRadius(8)
                .overlay(
                    RoundedRectangle(cornerRadius: 8)
                        .stroke(ThemeColors.borderSubtle, style: StrokeStyle(lineWidth: 1, dash: [4, 4]))
                )

                // Common Places Section
                VStack(alignment: .leading, spacing: 8) {
                    Text("COMMON PLACES")
                        .font(.system(size: 10, weight: .bold))
                        .foregroundColor(ThemeColors.textDim)

                    commonPlaceRow(name: "Applications", path: "/Applications ›")
                    commonPlaceRow(name: "Your Applications", path: "~/Applications ›")
                    commonPlaceRow(name: "Utilities", path: "/Applications/Utilities ›")
                    commonPlaceRow(name: "Steam games", path: "steamapps/common ›")
                }
            }
            .padding(16)
        }
    }

    private func commonPlaceRow(name: String, path: String) -> some View {
        Button(action: openFileBrowser) {
            HStack {
                Image(systemName: "folder")
                    .font(.system(size: 13))
                    .foregroundColor(ThemeColors.textMuted)

                Text(name)
                    .font(.system(size: 13, weight: .medium))
                    .foregroundColor(ThemeColors.textPrimary)

                Spacer()

                Text(path)
                    .font(.system(size: 11))
                    .foregroundColor(ThemeColors.textDim)
            }
            .padding(.horizontal, 10)
            .padding(.vertical, 8)
            .background(ThemeColors.surface)
            .cornerRadius(6)
        }
        .buttonStyle(.plain)
    }

    private func reloadAll() {
        let lib = MacAppLibrary(db: state.db)
        self.trackedApps = (try? lib.getTrackedApps()) ?? []
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

    private func trackApp(path: String, name: String) {
        let lib = MacAppLibrary(db: state.db)
        do {
            _ = try lib.trackApp(
                exePath: path,
                displayName: name,
                category: "Other",
                kind: .other,
                deviceId: state.localDeviceId
            )
            addedThisSession += 1
            reloadAll()
            state.refresh()
        } catch {
            NSLog("Failed to track app: \(error)")
        }
    }
}

public struct IdentifiableInt64: Identifiable {
    public let id: Int64
}
