import AppKit
import SwiftUI
import TimeSinkCore

@MainActor
public final class AppDelegate: NSObject, NSApplicationDelegate {
    private var popover: NSPopover!
    private var statusBarController: StatusBarController!
    private var db: SQLiteDatabase!
    private var store: SQLiteSessionStore!
    private var engine: TrackingEngine!
    private var processProvider: NSWorkspaceProcessProvider!
    private var powerHub: MacPowerEventHub!
    private var iconCache: MacIconCache!
    public private(set) var syncHost: SyncHost!
    private var appState: AppState!
    private var timer: Timer?

    public func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)

        let dbPath = Self.databasePath()
        do {
            let database = try SQLiteDatabase(path: dbPath)
            db = database
            try Migrator.apply(to: database)

            let library = MacAppLibrary(db: database)
            library.repairMissingBundleIds()

            store = SQLiteSessionStore(db: database)
            processProvider = NSWorkspaceProcessProvider()
            powerHub = MacPowerEventHub()
            iconCache = MacIconCache(db: database)
            syncHost = SyncHost(dbPath: dbPath, key: database.key, timeZone: .current)
            appState = AppState(db: database, syncHost: syncHost)
            syncHost.start()
            library.repairMissingIcons(iconCache: iconCache, localDeviceId: appState.localDeviceId)

            let settings = DbTrackingSettings(db: database)
            let clock = SystemClock()
            engine = try TrackingEngine(
                store: store,
                processes: processProvider,
                clock: clock,
                settings: settings,
                timeZone: .current
            )
            engine.attach(power: powerHub.hub)
            powerHub.hub.onResume.append { [weak self] _ in
                Task { @MainActor [weak self] in
                    self?.processProvider?.reconcileRunningApplications()
                }
            }
            try engine.recoverFromCrash(Date())

            processProvider.onProcessChanged = { [weak self] in
                Task { @MainActor [weak self] in
                    try? self?.engine.onTick(Date())
                    self?.appState?.refresh()
                }
            }

            // Popover setup
            popover = NSPopover()
            popover.contentSize = NSSize(width: 400, height: 640)
            popover.behavior = .transient
            popover.appearance = ThemeManager.shared.nsAppearance ?? NSAppearance(named: .darkAqua)
            popover.contentViewController = NSHostingController(rootView: PopoverRootView(state: appState))

            statusBarController = StatusBarController(popover: popover, appState: appState, initialIconId: appState.appIcon)
            updateDockIcon(appState.appIcon)

            appState.onClosePopover = { [weak self] in
                self?.statusBarController?.closePopover()
            }
            appState.onTickRequest = { [weak self] in
                self?.tick()
            }

            appState.onThemeChanged = { [weak self] in
                self?.popover.appearance = ThemeManager.shared.nsAppearance
                self?.statusBarController?.update(
                    todaySeconds: self?.appState?.currentTodaySeconds ?? 0,
                    showTime: self?.appState?.showMenuBarTime ?? false,
                    iconId: self?.appState?.appIcon
                )
                if let currentIcon = self?.appState?.appIcon {
                    self?.updateDockIcon(currentIcon)
                }
            }
            appState.onMenuBarTimeChanged = { [weak self] todaySec, showTime in
                self?.statusBarController?.update(todaySeconds: todaySec, showTime: showTime, iconId: self?.appState?.appIcon)
            }
            appState.onAppIconChanged = { [weak self] iconId in
                self?.statusBarController?.update(
                    todaySeconds: self?.appState?.currentTodaySeconds ?? 0,
                    showTime: self?.appState?.showMenuBarTime ?? false,
                    iconId: iconId
                )
                self?.updateDockIcon(iconId)
            }

            // Heartbeat timer every 15s
            timer = Timer.scheduledTimer(withTimeInterval: 15, repeats: true) { [weak self] _ in
                Task { @MainActor [weak self] in
                    self?.tick()
                }
            }
            tick()
        } catch {
            NSLog("Failed to initialize TimeSink: \(error)")
        }
    }

    public func applicationWillTerminate(_ notification: Notification) {
        timer?.invalidate()
        syncHost?.stop()
        try? engine.onShutdown(Date())
    }

    private func tick() {
        let now = Date()
        processProvider?.reconcileRunningApplications()
        try? engine.reloadTrackedApps()
        try? engine.onTick(now)
        appState?.checkAutoBackup()
        appState?.refresh()
    }

    private func updateDockIcon(_ iconId: String) {
        let accentColor = NSColor(ThemeColors.accent)
        guard let appImg = IconCatalogue.image(for: iconId, size: 512, accentColor: accentColor) ??
                           IconCatalogue.image(for: iconId, size: 256, accentColor: accentColor) else {
            return
        }
        NSApp.applicationIconImage = appImg

        let mainBundlePath = Bundle.main.bundleURL.path
        if !mainBundlePath.isEmpty && FileManager.default.fileExists(atPath: mainBundlePath) {
            NSWorkspace.shared.setIcon(appImg, forFile: mainBundlePath, options: [])
            NSWorkspace.shared.noteFileSystemChanged(mainBundlePath)
        }

        let installedPath = "/Applications/TimeSink.app"
        if mainBundlePath != installedPath && FileManager.default.fileExists(atPath: installedPath) {
            NSWorkspace.shared.setIcon(appImg, forFile: installedPath, options: [])
            NSWorkspace.shared.noteFileSystemChanged(installedPath)
        }
    }

    public static func databasePath() -> String {
        if let env = ProcessInfo.processInfo.environment["TIMESINK_DB"], !env.isEmpty {
            return env
        }
        let appSupport = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first!
        let dir = appSupport.appendingPathComponent("TimeSink")
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir.appendingPathComponent("timesink.db").path
    }
}
