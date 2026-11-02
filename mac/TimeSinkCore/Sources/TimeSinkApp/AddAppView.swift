import SwiftUI
import AppKit
import TimeSinkCore
import UniformTypeIdentifiers

public enum AddAppTab: String, CaseIterable, Identifiable {
    case running = "Running"
    case installed = "Installed"
    case tracked = "Tracked"
    case browse = "Browse…"

    public var id: String { rawValue }
}

private enum TrackedDeviceFilter: String, CaseIterable, Identifiable {
    case local
    case synced

    public var id: String { rawValue }
}

private struct RunningApp: Identifiable {
    var id: String { path }
    let name: String
    let path: String
    let bundleId: String?
    let launched: Date?
    let icon: NSImage?
}

private struct RetiredApp: Identifiable {
    let id: Int64
    let name: String
    let exePath: String
}

public struct AddAppView: View {
    @ObservedObject var state: AppState
    public let onDismiss: () -> Void

    @State private var activeTab: AddAppTab = .running
    @State private var searchQuery = ""
    @State private var addedThisSession = 0
    @State private var hiddenThisSession: Set<String> = []
    @State private var hoveredId: Int64?
    @State private var dropTargeted = false
    @State private var deviceFilter: TrackedDeviceFilter = .local

    @State private var runningApps: [RunningApp] = []
    @State private var installedApps: [InstalledAppItem] = []
    @State private var trackedApps: [TrackedAppItem] = []
    @State private var trackedExePaths: Set<String> = []
    @State private var trackedBundleIds: Set<String> = []
    @State private var retiredApps: [RetiredApp] = []


    public init(state: AppState, onDismiss: @escaping () -> Void) {
        self.state = state
        self.onDismiss = onDismiss
    }

    private var library: MacAppLibrary { MacAppLibrary(db: state.db) }

    public var body: some View {
        ZStack {
            VStack(spacing: 0) {
                header
                tabs
                Group {
                    switch activeTab {
                    case .running: runningTab
                    case .installed: installedTab
                    case .tracked: trackedTab
                    case .browse: browseTab
                    }
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
                footer
            }
            .background(ThemeColors.background)

            if let id = state.addAppEditingAppId {
                AppSettingsSheet(state: state, appId: id) {
                    state.addAppEditingAppId = nil
                    reloadAll()
                }
            }
        }
        .frame(width: 400, height: 640)
        .onAppear {
            if let filter = state.addAppInitialDeviceFilter {
                deviceFilter = filter == "synced" ? .synced : .local
                state.addAppInitialDeviceFilter = nil
            } else {
                deviceFilter = .local
            }
            if state.addAppInitialTab != .running {
                activeTab = state.addAppInitialTab
                state.addAppInitialTab = .running
            }
            reloadAll()
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

            Text("Add app")
                .font(.system(size: 17, weight: .bold))
                .foregroundColor(ThemeColors.textPrimary)
                .frame(maxWidth: .infinity, alignment: .leading)

            Button(action: onDismiss) {
                IconView(.close, size: 14, weight: 1.6, color: ThemeColors.textDim)
                    .frame(width: 28, height: 28)
                    .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
        }
        .padding(.horizontal, 12)
        .frame(height: 50)
    }

    private var tabs: some View {
        HStack(spacing: 18) {
            ForEach(AddAppTab.allCases) { tab in
                let on = activeTab == tab
                Button(action: {
                    activeTab = tab
                    searchQuery = ""
                }) {
                    Text(tab.rawValue)
                        .font(.system(size: 13, weight: on ? .semibold : .regular))
                        .foregroundColor(on ? ThemeColors.textPrimary : ThemeColors.textMuted)
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

    private var footer: some View {
        HStack {
            Text(activeTab == .tracked ? "Double-click an app for its settings" : "\(addedThisSession) added this session")
                .font(.system(size: 11))
                .foregroundColor(ThemeColors.textMuted)
            Spacer()
            Button(action: onDismiss) {
                Text("Done")
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundColor(ThemeColors.textPrimary)
                    .padding(.horizontal, 14)
                    .frame(height: 28)
                    .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(ThemeColors.border, lineWidth: 1))
                    .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
        }
        .padding(.horizontal, 16)
        .frame(height: 46)
        .topRule()
    }

    // MARK: - Shared row controls

    private func tile(_ name: String, png: Data?, size: CGFloat = 38, radius: CGFloat = 10, font: CGFloat = 17) -> some View {
        AppTile(name: name, png: png, size: size, radius: radius, fontSize: font)
    }

    private func iconButton(_ icon: TSIcon, width: CGFloat = 38, border: Color = ThemeColors.border, color: Color = ThemeColors.textDim, help: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            IconView(icon, size: 18, weight: icon == .gamepad ? 1.9 : 1.4, color: color)
                .frame(width: width, height: 32)
                .overlay(RoundedRectangle(cornerRadius: 10).strokeBorder(border, lineWidth: 1))
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help(help)
    }

    private func trackButton(help: String = "Track", action: @escaping () -> Void) -> some View {
        Button(action: action) {
            IconView(.lashes, size: 20, weight: 1.6, color: ThemeColors.background)
                .frame(width: 40, height: 32)
                .background(RoundedRectangle(cornerRadius: 10).fill(ThemeColors.accent))
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help(help)
    }

    private func trackedButtons(_ app: TrackedAppItem, width: CGFloat, eyeLime: Bool) -> some View {
        let game = app.kind != .other
        let isBg = app.isBackground
        return HStack(spacing: width == 38 ? 12 : 10) {
            iconButton(.gamepad, width: width, border: game ? ThemeColors.accent : ThemeColors.border, color: game ? ThemeColors.accent : ThemeColors.textDim, help: "Game type") {
                try? library.updateApp(appId: app.id, displayName: app.displayName, kind: game ? .other : .game, isBackground: app.isBackground)
                state.refresh()
                reloadAll()
            }
            iconButton(.layers, width: width, border: isBg ? ThemeColors.accent : ThemeColors.border, color: isBg ? ThemeColors.accent : ThemeColors.textDim, help: "Background app") {
                try? library.updateApp(appId: app.id, displayName: app.displayName, kind: app.kind, isBackground: !isBg)
                state.refresh()
                reloadAll()
            }
            iconButton(.eye, width: width, color: eyeLime ? ThemeColors.accent : ThemeColors.textDim, help: "Stop tracking") { stop(app.id) }
        }
    }

    private func searchField(_ placeholder: String) -> some View {
        HStack(spacing: 10) {
            IconView(.search, size: 18, weight: 1.5, color: ThemeColors.textDim)
            TextField(placeholder, text: $searchQuery)
                .textFieldStyle(.plain)
                .font(.system(size: 13))
                .foregroundColor(ThemeColors.textPrimary)
        }
        .padding(.horizontal, 14)
        .frame(height: 40)
        .card(border: Tokens.fieldBorder, radius: 12)
    }

    // MARK: - Running

    private func trackedItem(path: String) -> TrackedAppItem? {
        let norm = (path as NSString).standardizingPath.lowercased()
        return trackedApps.first {
            $0.exePath == path ||
            ($0.exePath as NSString).standardizingPath.lowercased() == norm
        }
    }

    private func isTracked(_ app: RunningApp) -> Bool {
        if trackedItem(path: app.path) != nil { return true }
        let norm = (app.path as NSString).standardizingPath.lowercased()
        if trackedExePaths.contains(norm) { return true }
        if let bundleId = app.bundleId?.lowercased(), !bundleId.isEmpty, trackedBundleIds.contains(bundleId) {
            return true
        }
        return false
    }

    private var untrackedRunningApps: [RunningApp] {
        runningApps.filter { app in
            !hiddenThisSession.contains(app.path) && !isTracked(app)
        }
    }

    private var runningTab: some View {
        ScrollView(.vertical, showsIndicators: false) {
            VStack(alignment: .leading, spacing: 12) {
                Text("Open on your Mac right now")
                    .font(.system(size: 12))
                    .foregroundColor(ThemeColors.textMuted)

                ForEach(untrackedRunningApps) { app in
                    HStack(spacing: 12) {
                        runningTile(app)
                        VStack(alignment: .leading, spacing: 0) {
                            Text(app.name)
                                .font(.system(size: 14, weight: .bold))
                                .foregroundColor(ThemeColors.textPrimary)
                                .lineLimit(1)
                            Text(openFor(app))
                                .font(.system(size: 11))
                                .foregroundColor(ThemeColors.textMuted)
                        }
                        Spacer(minLength: 4)
                        iconButton(.eyeSlash, color: ThemeColors.textMuted, help: "Not this one") {
                            hiddenThisSession.insert(app.path)
                        }
                        trackButton { track(path: app.path, name: app.name) }
                    }
                    .frame(height: 44)
                }

                if untrackedRunningApps.isEmpty {
                    Text(runningApps.isEmpty ? "No open apps detected." : "All open apps are already tracked.")
                        .font(.system(size: 12))
                        .foregroundColor(ThemeColors.textMuted)
                        .padding(.vertical, 4)
                }

                dontSeeIt
            }
            .padding(.horizontal, 16)
            .padding(.top, 14)
        }
    }

    @ViewBuilder
    private func runningTile(_ app: RunningApp) -> some View {
        if let image = app.icon {
            Image(nsImage: image).resizable().scaledToFit().frame(width: 38, height: 38)
        } else {
            tile(app.name, png: nil)
        }
    }

    private func openFor(_ app: RunningApp) -> String {
        guard let launched = app.launched else { return "open" }
        return "open \(Formatters.paddedHM(Int64(Date().timeIntervalSince(launched))))"
    }

    private var dontSeeIt: some View {
        Button(action: { activeTab = .installed }) {
            (Text("Don't see it? Open it first, or look in ")
                + Text("Installed").fontWeight(.bold).foregroundColor(ThemeColors.accent).underline()
                + Text("."))
                .font(.system(size: 12))
                .foregroundColor(ThemeColors.textDim)
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.horizontal, 14)
                .padding(.vertical, 12)
                .dashedCard(color: ThemeColors.border, radius: 10)
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }

    // MARK: - Installed

    private var unrankedInstalledGroups: [(letter: String, apps: [InstalledAppItem])] {
        let groups = Dictionary(grouping: installedApps) { String($0.name.prefix(1)).uppercased() }
        return groups.keys.sorted().map { ($0, groups[$0]!.sorted { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }) }
    }

    private var installedTab: some View {
        let q = searchQuery.trimmingCharacters(in: .whitespaces)
        let isSearching = !q.isEmpty
        let shown = isSearching ? InstalledSearch.search(installedApps, query: q, publisherOf: { AppVendor.name(executable: $0) }) : []

        return VStack(spacing: 6) {
            searchField("Search installed apps").padding(.trailing, 4)
            if isSearching {
                ScrollView(.vertical, showsIndicators: false) {
                    if shown.isEmpty {
                        Text("No installed app matches “\(q)”")
                            .font(.system(size: 12))
                            .foregroundColor(ThemeColors.textDim)
                            .frame(maxWidth: .infinity, alignment: .center)
                            .padding(.vertical, 32)
                    } else {
                        LazyVStack(alignment: .leading, spacing: 0) {
                            ForEach(shown) { installedRow($0) }
                        }
                    }
                }
            } else {
                ScrollViewReader { proxy in
                    HStack(spacing: 6) {
                        ScrollView(.vertical, showsIndicators: false) {
                            LazyVStack(alignment: .leading, spacing: 0) {
                                ForEach(unrankedInstalledGroups, id: \.letter) { group in
                                    Text(group.letter)
                                        .font(.system(size: 11, weight: .bold))
                                        .foregroundColor(ThemeColors.accent)
                                        .frame(height: 24)
                                        .id(group.letter)
                                    ForEach(group.apps) { installedRow($0) }
                                }
                            }
                        }
                        VStack(spacing: 3) {
                            ForEach(unrankedInstalledGroups, id: \.letter) { group in
                                Button(action: { proxy.scrollTo(group.letter, anchor: .top) }) {
                                    Text(group.letter)
                                        .font(.system(size: 8, weight: .semibold))
                                        .foregroundColor(ThemeColors.textMuted)
                                        .contentShape(Rectangle())
                                }
                                .buttonStyle(.plain)
                            }
                        }
                        .frame(width: 12)
                    }
                }
            }
        }
        .padding(.leading, 16)
        .padding(.trailing, 12)
        .padding(.top, 12)
    }

    private func installedRow(_ app: InstalledAppItem) -> some View {
        let isSiri = app.name == "Siri" || app.bundleId == "com.apple.siri.launcher"
        let tracked = trackedItem(path: app.executablePath)
            ?? (isSiri ? trackedApps.first { $0.displayName == "Siri" || $0.exePath.contains("Siri") } : nil)
        let retired = retiredApps.first {
            $0.exePath == app.executablePath || (isSiri && ($0.name == "Siri" || $0.exePath.contains("Siri")))
        }
        return HStack(spacing: 12) {
            tile(app.name, png: state.iconCache.iconData(forFilePath: app.bundleUrl.path), size: 36, radius: 9, font: 16)
            VStack(alignment: .leading, spacing: 0) {
                Text(app.name)
                    .font(.system(size: 13, weight: .bold))
                    .foregroundColor(ThemeColors.textPrimary)
                    .lineLimit(1)
                Text(retired != nil ? "Stopped · history kept" : AppVendor.name(executable: app.executablePath))
                    .font(.system(size: 10))
                    .foregroundColor(ThemeColors.textMuted)
                    .lineLimit(1)
            }
            Spacer(minLength: 4)
            if let tracked {
                trackedButtons(tracked, width: 36, eyeLime: true)
            } else {
                trackButton(help: retired != nil ? "Restore" : "Track") {
                    track(path: retired?.exePath ?? app.executablePath, name: app.name)
                }
                .padding(.trailing, 10)
            }
        }
        .frame(height: 54)
    }

    // MARK: - Tracked

    private func hasLocalPresence(_ app: TrackedAppItem) -> Bool {
        if state.isLocalDevice(app.deviceId) { return true }
        return app.mergedDeviceIds.contains { state.isLocalDevice($0) }
    }

    private func hasRemotePresence(_ app: TrackedAppItem) -> Bool {
        if !app.deviceId.isEmpty && !state.isLocalDevice(app.deviceId) { return true }
        return app.mergedDeviceIds.contains { !$0.isEmpty && !state.isLocalDevice($0) }
    }

    private var hasSyncedApps: Bool {
        state.hasMultipleDevices || trackedApps.contains { hasRemotePresence($0) }
    }

    private var filteredTrackedApps: [TrackedAppItem] {
        if !hasSyncedApps { return trackedApps }
        return trackedApps.filter { app in
            switch deviceFilter {
            case .local: return hasLocalPresence(app)
            case .synced: return hasRemotePresence(app)
            }
        }
    }

    private var trackedTab: some View {
        let q = searchQuery.trimmingCharacters(in: .whitespaces)
        let appsToFilter = filteredTrackedApps
        let shown = TrackedSearch.apply(appsToFilter, query: q, publisherOf: { AppVendor.name(executable: $0) })
        let headerText = q.isEmpty ? "TRACKED NOW" : "\(shown.count) APPS TRACKED"

        return VStack(alignment: .leading, spacing: 10) {
            searchField("Search tracked apps")
            HStack(alignment: .center) {
                Text(headerText).caps(10, tracking: 1)
                Spacer()
                if hasSyncedApps {
                    Segmented(
                        options: [(TrackedDeviceFilter.local, "This Mac"), (TrackedDeviceFilter.synced, "Synced")],
                        selection: $deviceFilter,
                        hPad: 6,
                        vPad: 2,
                        radius: 6
                    )
                }
            }
            ScrollView(.vertical, showsIndicators: false) {
                if appsToFilter.isEmpty {
                    Text(deviceFilter == .synced ? "No tracked apps from synced devices." : "Nothing is tracked yet. Add an app from Running or Installed.")
                        .font(.system(size: 12))
                        .foregroundColor(ThemeColors.textDim)
                        .frame(maxWidth: .infinity, alignment: deviceFilter == .synced ? .center : .leading)
                        .padding(.vertical, 16)
                } else if shown.isEmpty {
                    Text("No installed app matches “\(q)”")
                        .font(.system(size: 12))
                        .foregroundColor(ThemeColors.textDim)
                        .frame(maxWidth: .infinity, alignment: .center)
                        .padding(.vertical, 32)
                } else {
                    VStack(spacing: 0) {
                        ForEach(shown) { trackedRow($0) }
                    }
                }
            }
        }
        .padding(.horizontal, 16)
        .padding(.top, 12)
    }

    private func trackedRow(_ app: TrackedAppItem) -> some View {
        let hot = hoveredId == app.id
        let allDevs = ([app.deviceId] + app.mergedDeviceIds).filter { !$0.isEmpty }
        var distinctDevs: [String] = []
        for d in allDevs {
            if !distinctDevs.contains(where: { $0.caseInsensitiveCompare(d) == .orderedSame }) {
                distinctDevs.append(d)
            }
        }
        let showDeviceMarkers = distinctDevs.count > 1

        return HStack(spacing: 12) {
            ZStack(alignment: .bottomTrailing) {
                tile(app.displayName, png: state.iconCache.getCachedIcon(forAppId: app.id, fallbackExePath: app.exePath))
                if app.mergedCount > 0 {
                    Text("\(app.mergedCount + 1)")
                        .font(.system(size: 9, weight: .heavy))
                        .foregroundColor(ThemeColors.background)
                        .frame(width: 16, height: 16)
                        .background(Circle().fill(Tokens.badge))
                        .overlay(Circle().strokeBorder(ThemeColors.background, lineWidth: 2))
                        .offset(x: 6, y: 6)
                }
            }
            VStack(alignment: .leading, spacing: 0) {
                HStack(spacing: 5) {
                    Text(app.displayName)
                        .font(.system(size: 14, weight: .bold))
                        .foregroundColor(ThemeColors.textPrimary)
                        .lineLimit(1)
                    if showDeviceMarkers {
                        let sortedDevs = distinctDevs.sorted { state.isLocalDevice($0) && !state.isLocalDevice($1) }
                        ForEach(sortedDevs, id: \.self) { dev in
                            DeviceMarker(isLocal: state.isLocalDevice(dev), size: 6)
                        }
                    }
                }
                Text(AppVendor.name(executable: app.exePath))
                    .font(.system(size: 11))
                    .foregroundColor(ThemeColors.textMuted)
                    .lineLimit(1)
            }
            Spacer(minLength: 4)
            trackedButtons(app, width: 38, eyeLime: false)
        }
        .padding(.horizontal, 4)
        .frame(height: 56)
        .background(RoundedRectangle(cornerRadius: 10).fill(hot ? ThemeColors.surfaceActive : .clear))
        .overlay(alignment: .topLeading) {
            if hot {
                Text("Double-click → app settings")
                    .font(.system(size: 11, weight: .bold))
                    .foregroundColor(ThemeColors.background)
                    .padding(.horizontal, 10)
                    .frame(height: 24)
                    .background(RoundedRectangle(cornerRadius: 7).fill(ThemeColors.textPrimary))
                    .fixedSize()
                    .offset(x: 60, y: 14)
                    .allowsHitTesting(false)
            }
        }
        .contentShape(Rectangle())
        .onHover { hoveredId = $0 ? app.id : (hoveredId == app.id ? nil : hoveredId) }
        .onTapGesture(count: 2) { state.addAppEditingAppId = app.id }
    }

    // MARK: - Browse

    private var browseTab: some View {
        VStack(alignment: .leading, spacing: 16) {
            VStack(spacing: 6) {
                IconView(.upload, size: 28, weight: 1.8, color: ThemeColors.accent)
                Text("Drop an .app or alias here")
                    .font(.system(size: 13, weight: .bold))
                    .foregroundColor(ThemeColors.textPrimary)
                    .padding(.top, 4)
                Text("Works for apps and games in any folder")
                    .font(.system(size: 11))
                    .foregroundColor(ThemeColors.textMuted)
                Button(action: { openFileBrowser() }) {
                    Text("Choose file…")
                        .font(.system(size: 12, weight: .bold))
                        .foregroundColor(ThemeColors.background)
                        .padding(.horizontal, 16)
                        .frame(height: 30)
                        .background(RoundedRectangle(cornerRadius: 8).fill(ThemeColors.accent))
                }
                .buttonStyle(.plain)
                .padding(.top, 8)
            }
            .frame(maxWidth: .infinity)
            .frame(height: 176)
            .dashedCard(color: dropTargeted ? ThemeColors.accent : Tokens.fieldBorder, radius: 14)
            .onDrop(of: [.fileURL], isTargeted: $dropTargeted) { providers in
                guard let provider = providers.first else { return false }
                _ = provider.loadObject(ofClass: URL.self) { url, _ in
                    guard let url else { return }
                    Task { @MainActor in trackBundle(url) }
                }
                return true
            }

            VStack(alignment: .leading, spacing: 8) {
                Text("COMMON PLACES").caps(10, tracking: 1).padding(.horizontal, 2)
                VStack(spacing: 0) {
                    place("Applications", "/Applications", "/Applications")
                    place("Your Applications", "~/Applications", NSHomeDirectory() + "/Applications")
                    place("Utilities", "/Applications/Utilities", "/System/Applications/Utilities")
                    place("Steam games", "steamapps/common", NSHomeDirectory() + "/Library/Application Support/Steam/steamapps/common")
                }
            }
        }
        .padding(.horizontal, 16)
        .padding(.top, 14)
    }

    private func place(_ name: String, _ label: String, _ path: String) -> some View {
        Button(action: { openFileBrowser(in: path) }) {
            HStack(spacing: 10) {
                IconView(.folder, size: 16, weight: 1.3, color: ThemeColors.textDim)
                Text(name)
                    .font(.system(size: 13, weight: .bold))
                    .foregroundColor(ThemeColors.textPrimary)
                Spacer()
                Text(label)
                    .font(.system(size: 10))
                    .foregroundColor(ThemeColors.textMuted)
                Text("›").foregroundColor(ThemeColors.textMuted)
            }
            .frame(height: 36)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }

    // MARK: - Actions

    private func reloadAll() {
        trackedApps = (try? library.getTrackedApps()) ?? []

        var allTracked: Set<String> = Set(trackedApps.map { ($0.exePath as NSString).standardizingPath.lowercased() })
        var allBundleIds = Set<String>()
        if let stmt = try? state.db.prepare("SELECT exe_path, bundle_id FROM apps WHERE state = 'tracked';") {
            while (try? stmt.step()) == true {
                if let p = stmt.columnText(at: 0) {
                    allTracked.insert((p as NSString).standardizingPath.lowercased())
                }
                if let b = stmt.columnText(at: 1), !b.isEmpty {
                    allBundleIds.insert(b.lowercased())
                }
            }
        }
        trackedExePaths = allTracked
        trackedBundleIds = allBundleIds

        var retired: [RetiredApp] = []
        if let stmt = try? state.db.prepare("SELECT id, display_name, exe_path FROM apps WHERE state = 'retired' AND canonical_id IS NULL;") {
            while (try? stmt.step()) == true {
                retired.append(RetiredApp(id: stmt.columnInt64(at: 0), name: stmt.columnText(at: 1) ?? "", exePath: stmt.columnText(at: 2) ?? ""))
            }
        }
        retiredApps = retired

        runningApps = NSWorkspace.shared.runningApplications
            .filter { $0.activationPolicy == .regular && $0.bundleIdentifier != Bundle.main.bundleIdentifier }
            .compactMap { app in
                guard let path = app.executableURL?.path else { return nil }
                return RunningApp(
                    name: app.localizedName ?? (path as NSString).lastPathComponent,
                    path: path,
                    bundleId: app.bundleIdentifier,
                    launched: app.launchDate,
                    icon: app.icon
                )
            }
            .sorted { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }

        if installedApps.isEmpty {
            installedApps = MacAppLibrary.scanInstalledApplications()
        }
    }

    private func stop(_ id: Int64) {
        try? library.stopTracking(appId: id)
        reloadAll()
        state.refresh()
    }

    private func track(path: String, name: String) {
        do {
            try library.trackApp(exePath: path, displayName: name, category: "Other", kind: .other, deviceId: state.localDeviceId)
            addedThisSession += 1
            reloadAll()
            state.refresh()
        } catch {
            NSLog("Failed to track app: \(error)")
        }
    }

    private func trackBundle(_ url: URL) {
        let resolved = (try? URL(resolvingAliasFileAt: url)) ?? url
        let bundle = Bundle(url: resolved)
        let name = bundle?.localizedInfoDictionary?["CFBundleDisplayName"] as? String
            ?? bundle?.infoDictionary?["CFBundleName"] as? String
            ?? resolved.deletingPathExtension().lastPathComponent
        track(path: bundle?.executableURL?.path ?? resolved.path, name: name)
    }

    private func openFileBrowser(in folder: String? = nil) {
        let panel = NSOpenPanel()
        panel.canChooseFiles = true
        panel.canChooseDirectories = false
        panel.allowsMultipleSelection = false
        panel.allowedContentTypes = [.application, .unixExecutable]
        if let folder { panel.directoryURL = URL(fileURLWithPath: folder) }
        if panel.runModal() == .OK, let url = panel.url {
            trackBundle(url)
        }
    }
}
