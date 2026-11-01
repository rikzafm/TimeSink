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

            store = SQLiteSessionStore(db: database)
            processProvider = NSWorkspaceProcessProvider()
            powerHub = MacPowerEventHub()
            iconCache = MacIconCache(db: database)
            syncHost = SyncHost(db: database, timeZone: .current)
            appState = AppState(db: database, syncHost: syncHost)

            syncHost.onSyncCompleted = { [weak self] in
                Task { @MainActor [weak self] in
                    self?.appState?.handleSyncCompleted()
                }
            }
            syncHost.start()

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
            try engine.recoverFromCrash(Date())

            processProvider.onProcessChanged = { [weak self] in
                Task { @MainActor [weak self] in
                    try? self?.engine.onTick(Date())
                }
            }

            // Popover setup
            popover = NSPopover()
            popover.contentSize = NSSize(width: 400, height: 640)
            popover.behavior = .transient
            popover.appearance = NSAppearance(named: .darkAqua)
            popover.contentViewController = NSHostingController(rootView: PopoverRootView(state: appState))

            statusBarController = StatusBarController(popover: popover)

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
        try? engine.onTick(now)
        appState?.refresh()
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
