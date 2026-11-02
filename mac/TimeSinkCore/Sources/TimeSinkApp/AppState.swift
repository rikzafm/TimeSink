import Foundation
import SwiftUI
import AppKit
import TimeSinkCore

public enum AppTab: String, CaseIterable, Identifiable {
    case today = "Today"
    case week = "Week"
    case month = "Month"
    case year = "Year"
    case all = "All"

    public var id: String { rawValue }
}

public enum YearSubView: String, CaseIterable, Identifiable {
    case pace = "Pace"
    case wrapped = "Wrapped"
    case matrix = "Matrix"

    public var id: String { rawValue }
}

public typealias AllSortOption = AllColumn

@MainActor
public final class AppState: ObservableObject {
    public let db: SQLiteDatabase
    public let syncHost: SyncHost?
    public let iconCache: MacIconCache

    @Published public var selectedTab: AppTab = .today {
        didSet {
            if oldValue != selectedTab {
                yearOffset = 0
            }
        }
    }
    @Published public var deviceScope: DeviceScope = .this
    @Published public var gamesOnly: Bool = false

    @Published public var hasMultipleDevices: Bool = false
    @Published public var localDeviceId: String = ""
    @Published public var localDeviceName: String = "Mac"
    @Published public var remoteDeviceId: String?
    @Published public var remoteDeviceName: String? = "PC"
    @Published public var shortDeviceNames: Bool = true
    @Published public var deviceDisplayNames: [String: String] = [:]

    // Navigation and sub-views
    @Published public var daysBack: Int = 0
    @Published public var weeksBack: Int = 0
    @Published public var monthsBack: Int = 0
    @Published public var yearOffset: Int = 0
    @Published public var yearSubView: YearSubView = .pace
    @Published public var wrappedCardIndex: Int = 0
    @Published public var detailYear: Int = Calendar.current.component(.year, from: Date())

    @Published public var selectedAppDetailId: Int64?
    @Published public var showingAddApp: Bool = false
    @Published public var showingSettings: Bool = false
    @Published public var showingDevicesSync: Bool = false
    @Published public var showingKeyboardGuide: Bool = false
    @Published public var addAppInitialTab: AddAppTab = .running
    @Published public var addAppInitialDeviceFilter: String? = nil
    @Published public var returnToDevicesSyncOnDismiss: Bool = false

    // Settings & Sync State
    @Published public var syncEnabled: Bool = false
    @Published public var syncFolder: String = ""
    @Published public var syncState: String = ""
    @Published public var lastSyncUtc: Int64 = 0
    @Published public var isSyncing: Bool = false
    @Published public var lastBackupUtc: Int64 = 0
    @Published public var openAtLogin: Bool = false
    @Published public var weekStart: String = "Mon"
    @Published public var pauseWhileSleep: Bool = true
    @Published public var countWhenLocked: Bool = true
    @Published public var minSessionSeconds: Int = 15
    @Published public var theme: String = "System"
    @Published public var accent: String = "Lime"
    @Published public var appIcon: String = IconCatalogue.defaultId
    @Published public var showMenuBarTime: Bool = false
    @Published public var backupMode: String = "Daily"
    @Published public var showingAppIconPicker: Bool = false

    // Inner modals and subviews tracking for keyboard unwinding
    @Published public var showingRestoreSheet: Bool = false
    @Published public var showingExportSheet: Bool = false
    @Published public var showingDeleteAllSheet: Bool = false
    @Published public var showingRenameMacSheet: Bool = false
    @Published public var addAppEditingAppId: Int64?
    @Published public var appSettingsShowingMerge: Bool = false
    @Published public var appSettingsShowingClearConfirm: Bool = false
    @Published public var appSettingsShowingStopConfirm: Bool = false
    @Published public var appDetailShowingSettings: Bool = false

    public var onClosePopover: (() -> Void)?

    public var onMenuBarTimeChanged: ((Int64, Bool) -> Void)?
    public var onThemeChanged: (() -> Void)?
    public var onAppIconChanged: ((String) -> Void)?

    // All tab search, layout and sort
    @Published public var allSearchQuery: String = ""
    @Published public var allSort: AllSort = .default
    @Published public var allLayout: String = "labeled"

    // Cached query results
    @Published public var todayResult: TodayResult?
    @Published public var weekResult: WeekResult?
    @Published public var monthResult: MonthResult?
    @Published public var yearResult: YearResult?
    @Published public var matrixResult: MatrixResult?
    @Published public var wrappedResult: WrappedResult?
    @Published public var allResult: AllResult?
    @Published public var appDetailResult: AppDetailResult?

    public init(db: SQLiteDatabase, syncHost: SyncHost? = nil) {
        self.db = db
        self.syncHost = syncHost
        self.iconCache = MacIconCache(db: db)

        self.syncHost?.onSyncStarted = { [weak self] in
            DispatchQueue.main.async {
                self?.isSyncing = true
            }
        }
        self.syncHost?.onSyncCompleted = { [weak self] in
            DispatchQueue.main.async {
                self?.handleSyncCompleted()
                self?.isSyncing = false
            }
        }
        loadDeviceInfo()
        loadSettings()
        healUntypedGachaApps()
        refresh()
    }

    private func healUntypedGachaApps() {
        for name in AppKinds.gachaNames {
            let escaped = name.replacingOccurrences(of: "'", with: "''")
            try? db.exec("UPDATE apps SET kind = 2 WHERE (kind IS NULL OR kind = 0) AND (LOWER(display_name) LIKE '%\(escaped)%' OR LOWER(exe_path) LIKE '%\(escaped)%');")
        }
    }

    public func loadDeviceInfo() {
        do {
            let devId = try db.scalarText("SELECT value FROM settings WHERE key = 'device.id';") ?? ""
            let devName = try db.scalarText("SELECT value FROM settings WHERE key = 'device.name';") ?? "Mac"
            self.localDeviceId = devId
            self.localDeviceName = devName

            let sdn = try db.scalarText("SELECT value FROM settings WHERE key = '\(SettingKeys.shortDeviceNames)';")
            self.shortDeviceNames = sdn?.lowercased() != "false"

            var rawDevices: [(id: String, name: String, platform: String, isLocal: Bool, firstSeen: String)] = []
            let stmt = try db.prepare("SELECT device_id, name, platform, is_local, first_seen_utc FROM devices WHERE state != 'forgotten' ORDER BY is_local DESC, first_seen_utc ASC, device_id ASC;")
            var remoteFound = false
            while try stmt.step() {
                guard let id = stmt.columnText(at: 0) else { continue }
                let name = stmt.columnText(at: 1) ?? ""
                let platform = stmt.columnText(at: 2) ?? ""
                let isLocal = stmt.columnInt64(at: 3) == 1
                let firstSeen = stmt.columnText(at: 4) ?? ""
                rawDevices.append((id: id, name: name, platform: platform, isLocal: isLocal, firstSeen: firstSeen))
                if !isLocal && !remoteFound {
                    self.remoteDeviceId = id
                    self.remoteDeviceName = name
                    remoteFound = true
                }
            }
            self.deviceDisplayNames = AppState.computeDisplayNames(devices: rawDevices, shortNames: shortDeviceNames)
            let totalActive = (try db.scalarInt("SELECT COUNT(*) FROM devices WHERE state = 'active';")) ?? 1
            self.hasMultipleDevices = totalActive > 1

            // Read device scope setting
            if let scopeStr = try db.scalarText("SELECT value FROM settings WHERE key = 'ui.deviceScope';") {
                self.deviceScope = scopeStr.lowercased() == "all" ? .all : .this
            } else if hasMultipleDevices {
                self.deviceScope = .all
            }
        } catch {
            NSLog("Failed to load device info: \(error)")
        }
    }

    public static func computeDisplayNames(
        devices: [(id: String, name: String, platform: String, isLocal: Bool, firstSeen: String)],
        shortNames: Bool
    ) -> [String: String] {
        DeviceInfoService.computeDisplayNames(devices: devices, shortNames: shortNames)
    }

    public func deviceDisplayName(_ deviceId: String?) -> String {
        guard let deviceId, !deviceId.isEmpty else {
            return isLocalDevice(deviceId ?? "") ? "Mac" : "PC"
        }
        if let name = deviceDisplayNames[deviceId] {
            return name
        }
        return isLocalDevice(deviceId) ? "Mac" : "PC"
    }

    public func deviceADisplayName() -> String {
        deviceDisplayName(localDeviceId)
    }

    public func deviceBDisplayName() -> String {
        deviceDisplayName(remoteDeviceId)
    }

    public func setShortDeviceNames(_ enabled: Bool) {
        self.shortDeviceNames = enabled
        try? db.exec("INSERT INTO settings (key, value) VALUES ('\(SettingKeys.shortDeviceNames)', '\(enabled ? "true" : "false")') ON CONFLICT(key) DO UPDATE SET value = excluded.value;")
        loadDeviceInfo()
        refresh()
    }

    public func setDeviceScope(_ scope: DeviceScope) {
        self.deviceScope = scope
        try? db.exec("INSERT INTO settings (key, value) VALUES ('ui.deviceScope', '\(scope == .all ? "all" : "this")') ON CONFLICT(key) DO UPDATE SET value = excluded.value;")
        refresh()
    }

    public func toggleGamesOnly() {
        self.gamesOnly.toggle()
        try? db.exec("INSERT INTO settings (key, value) VALUES ('ui.gamesOnly', '\(gamesOnly ? "true" : "false")') ON CONFLICT(key) DO UPDATE SET value = excluded.value;")
        refresh()
    }

    public func makeUsageContext(running: Set<Int64>? = nil) -> UsageContext {
        let now = Date()
        return UsageContext(
            now: now,
            zone: .current,
            running: running,
            signInUtc: nil,
            weekStart: WeekStart.parse(weekStart),
            gamesOnly: gamesOnly,
            db: db,
            deviceScope: deviceScope,
            localDeviceId: localDeviceId
        )
    }

    public func isLocalDevice(_ devId: String) -> Bool {
        if devId.isEmpty { return true }
        if localDeviceId.isEmpty { return true }
        return devId.caseInsensitiveCompare(localDeviceId) == .orderedSame
    }

    public func isLocalDevice(devId: String) -> Bool {
        isLocalDevice(devId)
    }

    /// Splits per-device seconds into this Mac and the other device. Only two devices are drawn.
    public func deviceSplit(_ seconds: [String: Int64]?) -> (mac: Int64, pc: Int64) {
        guard let seconds else { return (0, 0) }
        let mac = seconds.filter { isLocalDevice($0.key) }.values.reduce(0, +)
        let pc = seconds.filter { !isLocalDevice($0.key) }.values.max() ?? 0
        return (mac, pc)
    }

    public var showsDeviceSplit: Bool { deviceScope == .all && hasMultipleDevices }

    public func refresh() {
        let ctx = makeUsageContext()
        do {
            switch selectedTab {
            case .today:
                if daysBack > 0 {
                    let cal = Calendar.current
                    let targetDate = cal.date(byAdding: .day, value: -daysBack, to: Date()) ?? Date()
                    let df = DateFormatter()
                    df.dateFormat = "yyyy-MM-dd"
                    let forDay = df.string(from: targetDate)
                    let res = try TodayQuery.run(ctx, forDay: forDay)
                    if res.firstTrackedDay == nil {
                        daysBack = 0
                        todayResult = try TodayQuery.run(ctx)
                    } else if let first = res.firstTrackedDay, res.date < first {
                        if let firstDate = df.date(from: first) {
                            let todayStart = cal.startOfDay(for: Date())
                            let firstStart = cal.startOfDay(for: firstDate)
                            daysBack = max(0, cal.dateComponents([.day], from: firstStart, to: todayStart).day ?? 0)
                            let clampedTarget = cal.date(byAdding: .day, value: -daysBack, to: Date()) ?? Date()
                            todayResult = try TodayQuery.run(ctx, forDay: df.string(from: clampedTarget))
                        } else {
                            todayResult = res
                        }
                    } else {
                        todayResult = res
                    }
                } else {
                    todayResult = try TodayQuery.run(ctx)
                }
            case .week:
                let res = try WeekQuery.run(ctx, weeksBack: weeksBack)
                if res.firstTrackedDay == nil && weeksBack > 0 {
                    weeksBack = 0
                    weekResult = try WeekQuery.run(ctx, weeksBack: 0)
                } else if let first = res.firstTrackedDay, weeksBack > 0 {
                    let ws = WeekStart.parse(weekStart)
                    let earliestWeekStart = WeekStarts.containing(day: first, weekStart: ws)
                    if res.shownWeekStart < earliestWeekStart {
                        let df = DateFormatter()
                        df.dateFormat = "yyyy-MM-dd"
                        if let eDate = df.date(from: earliestWeekStart), let tDate = df.date(from: res.today) {
                            var cal = Calendar(identifier: .gregorian)
                            cal.timeZone = TimeZone(secondsFromGMT: 0)!
                            let diffDays = cal.dateComponents([.day], from: eDate, to: tDate).day ?? 0
                            weeksBack = max(0, diffDays / 7)
                            weekResult = try WeekQuery.run(ctx, weeksBack: weeksBack)
                        } else {
                            weekResult = res
                        }
                    } else {
                        weekResult = res
                    }
                } else {
                    weekResult = res
                }
            case .month:
                let cal = Calendar.current
                let now = Date()
                let targetDate = cal.date(byAdding: .month, value: -monthsBack, to: now) ?? now
                let y = cal.component(.year, from: targetDate)
                let m = cal.component(.month, from: targetDate)
                let res = try MonthQuery.run(ctx, year: y, month: m)
                if res.firstTracked == nil && monthsBack > 0 {
                    monthsBack = 0
                    let cy = cal.component(.year, from: now)
                    let cm = cal.component(.month, from: now)
                    monthResult = try MonthQuery.run(ctx, year: cy, month: cm)
                } else if let first = res.firstTracked, monthsBack > 0 {
                    let earliestPrefix = String(first.prefix(7))
                    let currentPrefix = String(format: "%04d-%02d", res.year, res.month)
                    if currentPrefix < earliestPrefix {
                        let firstYear = Int(first.prefix(4)) ?? y
                        let firstMonth = Int(first.dropFirst(5).prefix(2)) ?? m
                        let nowYear = cal.component(.year, from: now)
                        let nowMonth = cal.component(.month, from: now)
                        let maxMonths = max(0, (nowYear - firstYear) * 12 + (nowMonth - firstMonth))
                        monthsBack = maxMonths
                        let clampedDate = cal.date(byAdding: .month, value: -monthsBack, to: now) ?? now
                        let cy = cal.component(.year, from: clampedDate)
                        let cm = cal.component(.month, from: clampedDate)
                        monthResult = try MonthQuery.run(ctx, year: cy, month: cm)
                    } else {
                        monthResult = res
                    }
                } else {
                    monthResult = res
                }
            case .year:
                let cal = Calendar.current
                let currentYear = cal.component(.year, from: Date()) - yearOffset
                let targetYearDate = cal.date(from: DateComponents(year: currentYear, month: 12, day: 31, hour: 12)) ?? Date()
                let yearCtx = UsageContext(
                    now: yearOffset == 0 ? Date() : targetYearDate,
                    zone: .current,
                    gamesOnly: gamesOnly,
                    db: db,
                    deviceScope: deviceScope,
                    localDeviceId: localDeviceId
                )
                yearResult = try YearQuery.run(yearCtx)
                matrixResult = try MatrixQuery.run(yearCtx)
                wrappedResult = try WrappedQuery.run(yearCtx)
            case .all:
                allResult = try AllQuery.run(ctx)
            }

            if let appId = selectedAppDetailId {
                appDetailResult = AppDetailQuery.run(context: ctx, appId: appId, year: detailYear)
            }
        } catch {
            NSLog("Error executing query for \(selectedTab): \(error)")
        }
        onMenuBarTimeChanged?(currentTodaySeconds, showMenuBarTime)
    }

    public var currentTodaySeconds: Int64 {
        if let res = todayResult, daysBack == 0 {
            return res.totalSeconds
        }
        let ctx = makeUsageContext()
        return (try? TodayQuery.run(ctx).totalSeconds) ?? 0
    }

    public func openAppDetail(appId: Int64) {
        self.selectedAppDetailId = appId
        self.detailYear = Calendar.current.component(.year, from: Date())
        self.appDetailResult = AppDetailQuery.run(context: makeUsageContext(), appId: appId, year: detailYear)
    }

    public func closeAppDetail() {
        self.selectedAppDetailId = nil
        self.appDetailResult = nil
        refresh()
    }

    // MARK: - Navigation

    public var canTodayGoPrev: Bool {
        todayResult?.canPrev == true
    }

    public func previousDay() {
        guard canTodayGoPrev else { return }
        daysBack += 1
        refresh()
    }

    public func nextDay() {
        guard daysBack > 0 else { return }
        daysBack -= 1
        refresh()
    }

    public var canWeekGoPrev: Bool {
        weekResult?.canPrev(weekStart: WeekStart.parse(weekStart)) == true
    }

    public func previousWeek() {
        guard canWeekGoPrev else { return }
        weeksBack += 1
        refresh()
    }

    public func nextWeek() {
        guard weeksBack > 0 else { return }
        weeksBack -= 1
        refresh()
    }

    public var canMonthGoPrev: Bool {
        monthResult?.canPrev == true
    }

    public func previousMonth() {
        guard canMonthGoPrev else { return }
        monthsBack += 1
        refresh()
    }

    public func nextMonth() {
        guard monthsBack > 0 else { return }
        monthsBack -= 1
        refresh()
    }

    public var canYearGoPrev: Bool {
        if let y = yearResult {
            return y.canPrev
        }
        guard let start = todayResult?.firstTrackedDay ?? monthResult?.firstTracked else { return false }
        guard let firstYear = Int(start.prefix(4)) else { return false }
        let currentYear = Calendar.current.component(.year, from: Date()) - yearOffset
        return currentYear > firstYear
    }

    public var canYearGoNext: Bool {
        yearOffset > 0
    }

    public func previousYear() {
        guard canYearGoPrev else { return }
        yearOffset += 1
        refresh()
    }

    public func nextYear() {
        guard canYearGoNext else { return }
        yearOffset -= 1
        refresh()
    }

    public func openDayInToday(dateString: String) {
        let df = DateFormatter()
        df.dateFormat = "yyyy-MM-dd"
        if let date = df.date(from: dateString) {
            var cal = Calendar(identifier: .gregorian)
            cal.timeZone = .current
            let today = cal.startOfDay(for: Date())
            let target = cal.startOfDay(for: date)
            let diff = max(0, cal.dateComponents([.day], from: target, to: today).day ?? 0)
            self.daysBack = diff
            self.selectedTab = .today
            self.refresh()
        }
    }

    public func loadDayDetail(day: String) -> DayDetail? {
        let ctx = makeUsageContext()
        return try? DayDetailQuery.run(ctx, day: day)
    }

    // MARK: - Device and Sync Management

    public func loadSettings() {
        do {
            self.syncEnabled = (try db.scalarText("SELECT value FROM settings WHERE key = 'sync.enabled';"))?.lowercased() == "true"
            self.syncFolder = (try db.scalarText("SELECT value FROM settings WHERE key = 'sync.folder';")) ?? ""
            self.syncState = (try db.scalarText("SELECT value FROM settings WHERE key = 'sync.state';")) ?? ""
            let lastExp = (try? db.scalarInt("SELECT CAST(value AS INTEGER) FROM settings WHERE key = 'sync.last_export_utc';")) ?? 0
            let lastImp = (try? db.scalarInt("SELECT CAST(value AS INTEGER) FROM settings WHERE key = 'sync.last_import_utc';")) ?? 0
            self.lastSyncUtc = max(lastExp, lastImp)
            self.lastBackupUtc = (try? db.scalarInt("SELECT CAST(value AS INTEGER) FROM settings WHERE key = 'backup.last_utc';")) ?? 0
            self.openAtLogin = LoginItemManager.shared.isEnabled
            let sdn = (try? db.scalarText("SELECT value FROM settings WHERE key = '\(SettingKeys.shortDeviceNames)';"))
            self.shortDeviceNames = sdn?.lowercased() != "false"
            let pauseVal = (try? db.scalarText("SELECT value FROM settings WHERE key = '\(SettingKeys.pauseOnSleep)';"))
                ?? (try? db.scalarText("SELECT value FROM settings WHERE key = 'tracking.pauseWhileSleep';"))
            self.pauseWhileSleep = pauseVal?.lowercased() != "false"
            if let ws = try? db.scalarText("SELECT value FROM settings WHERE key = '\(SettingKeys.weekStart)';") {
                self.weekStart = ws
            }
            self.countWhenLocked = (try db.scalarText("SELECT value FROM settings WHERE key = '\(SettingKeys.countWhenLocked)';")) != "false"
            if let s = try db.scalarInt("SELECT CAST(value AS INTEGER) FROM settings WHERE key = 'tracking.minSessionSeconds';"), s > 0 {
                self.minSessionSeconds = Int(s)
            }
            if let t = try? db.scalarText("SELECT value FROM settings WHERE key = '\(SettingKeys.theme)';") {
                self.theme = t
            }
            if let a = try? db.scalarText("SELECT value FROM settings WHERE key = '\(SettingKeys.accent)';") {
                self.accent = a
            }
            ThemeManager.shared.update(theme: self.theme, accent: self.accent)

            let smbt = (try? db.scalarText("SELECT value FROM settings WHERE key = '\(SettingKeys.showMenuBarTime)';"))
            self.showMenuBarTime = smbt?.lowercased() == "true"

            let storedIcon = try? db.scalarText("SELECT value FROM settings WHERE key = '\(SettingKeys.appIcon)';")
            self.appIcon = IconCatalogue.resolve(storedIcon)

            if let bm = try? db.scalarText("SELECT value FROM settings WHERE key = '\(SettingKeys.backupMode)';") {
                self.backupMode = bm
            }

            if let al = try? db.scalarText("SELECT value FROM settings WHERE key = '\(SettingKeys.allLayout)';"), !al.isEmpty {
                self.allLayout = al
            }
        } catch {}
    }

    public func setAllLayout(_ layout: String) {
        self.allLayout = layout
        try? db.exec("INSERT INTO settings (key, value) VALUES ('\(SettingKeys.allLayout)', '\(layout)') ON CONFLICT(key) DO UPDATE SET value = excluded.value;")
    }

    public func setAppIcon(_ iconId: String) {
        let resolved = IconCatalogue.resolve(iconId)
        self.appIcon = resolved
        try? db.exec("INSERT INTO settings (key, value) VALUES ('\(SettingKeys.appIcon)', '\(resolved)') ON CONFLICT(key) DO UPDATE SET value = excluded.value;")
        onAppIconChanged?(resolved)
        objectWillChange.send()
    }

    public func setWeekStart(_ val: String) {
        self.weekStart = val
        try? db.exec("INSERT INTO settings (key, value) VALUES ('\(SettingKeys.weekStart)', '\(val)') ON CONFLICT(key) DO UPDATE SET value = excluded.value;")
        refresh()
    }

    public func setMinSessionSeconds(_ seconds: Int) {
        self.minSessionSeconds = seconds
        try? db.exec("INSERT INTO settings (key, value) VALUES ('\(SettingKeys.minSessionSeconds)', '\(seconds)') ON CONFLICT(key) DO UPDATE SET value = excluded.value;")
    }

    public func setTheme(_ theme: String) {
        self.theme = theme
        ThemeManager.shared.update(theme: theme, accent: accent)
        try? db.exec("INSERT INTO settings (key, value) VALUES ('\(SettingKeys.theme)', '\(theme)') ON CONFLICT(key) DO UPDATE SET value = excluded.value;")
        onThemeChanged?()
        objectWillChange.send()
        refresh()
    }

    public func setAccent(_ accent: String) {
        self.accent = accent
        ThemeManager.shared.update(theme: theme, accent: accent)
        try? db.exec("INSERT INTO settings (key, value) VALUES ('\(SettingKeys.accent)', '\(accent)') ON CONFLICT(key) DO UPDATE SET value = excluded.value;")
        onThemeChanged?()
        objectWillChange.send()
        refresh()
    }

    public func setShowMenuBarTime(_ show: Bool) {
        self.showMenuBarTime = show
        try? db.exec("INSERT INTO settings (key, value) VALUES ('\(SettingKeys.showMenuBarTime)', '\(show ? "true" : "false")') ON CONFLICT(key) DO UPDATE SET value = excluded.value;")
        onMenuBarTimeChanged?(currentTodaySeconds, show)
    }

    public func setBackupMode(_ mode: String) {
        self.backupMode = mode
        try? db.exec("INSERT INTO settings (key, value) VALUES ('\(SettingKeys.backupMode)', '\(mode)') ON CONFLICT(key) DO UPDATE SET value = excluded.value;")
    }

    public func checkAutoBackup() {
        AutoBackup.runIfDue(db: db, folder: defaultBackupFolder)
        self.lastBackupUtc = (try? db.scalarInt("SELECT CAST(value AS INTEGER) FROM settings WHERE key = 'backup.last_utc';")) ?? 0
    }

    public func setSyncEnabled(_ enabled: Bool) {
        self.syncEnabled = enabled
        try? db.exec("INSERT INTO settings (key, value) VALUES ('sync.enabled', '\(enabled ? "true" : "false")') ON CONFLICT(key) DO UPDATE SET value = excluded.value;")
        if enabled {
            let now = Int64(Date().timeIntervalSince1970)
            try? db.exec("INSERT INTO settings (key, value) VALUES ('sync.enabled_utc', '\(now)') ON CONFLICT(key) DO NOTHING;")
            if let host = syncHost {
                host.start()
            } else {
                triggerSyncNow()
            }
        } else {
            syncHost?.stop()
        }
    }

    public func setSyncFolder(_ folder: String) {
        self.syncFolder = folder
        try? db.exec("INSERT INTO settings (key, value) VALUES ('sync.folder', '\(folder)') ON CONFLICT(key) DO UPDATE SET value = excluded.value;")
        if syncEnabled {
            if let host = syncHost {
                host.restart()
            } else {
                triggerSyncNow()
            }
        }
    }

    public func setOpenAtLogin(_ enabled: Bool) {
        self.openAtLogin = enabled
        try? LoginItemManager.shared.setEnabled(enabled)
    }

    public var pauseOnSleep: Bool {
        get { pauseWhileSleep }
        set { setPauseWhileSleep(newValue) }
    }

    public func setPauseOnSleep(_ pause: Bool) {
        setPauseWhileSleep(pause)
    }

    public func setPauseWhileSleep(_ pause: Bool) {
        self.pauseWhileSleep = pause
        try? db.exec("INSERT INTO settings (key, value) VALUES ('\(SettingKeys.pauseOnSleep)', '\(pause ? "true" : "false")') ON CONFLICT(key) DO UPDATE SET value = excluded.value;")
        try? db.exec("DELETE FROM settings WHERE key = 'tracking.pauseWhileSleep';")
    }

    public func setCountWhenLocked(_ count: Bool) {
        self.countWhenLocked = count
        try? db.exec("INSERT INTO settings (key, value) VALUES ('tracking.countWhenLocked', '\(count ? "true" : "false")') ON CONFLICT(key) DO UPDATE SET value = excluded.value;")
    }

    public func readDevices() -> [DeviceRow] {
        var list: [DeviceRow] = []
        do {
            let stmt = try db.prepare("""
                SELECT device_id, name, platform, is_local, first_seen_utc, last_import_utc, last_file_utc, state
                FROM devices
                ORDER BY is_local DESC, first_seen_utc ASC;
            """)
            let iso = ISO8601DateFormatter()
            iso.formatOptions = [.withInternetDateTime]
            while try stmt.step() {
                let id = stmt.columnText(at: 0) ?? ""
                let name = stmt.columnText(at: 1) ?? ""
                let platform = stmt.columnText(at: 2) ?? "macos"
                let isLocal = stmt.columnInt64(at: 3) == 1
                let firstSeen = stmt.columnText(at: 4) ?? ""
                let lastImportStr = stmt.columnText(at: 5)
                let lastFileStr = stmt.columnText(at: 6)
                let state = stmt.columnText(at: 7) ?? "active"

                var lastImportUtc: Int64? = nil
                if let s = lastImportStr, let d = iso.date(from: s) {
                    lastImportUtc = Int64(d.timeIntervalSince1970)
                }
                var lastFileUtc: Int64? = nil
                if let s = lastFileStr, let d = iso.date(from: s) {
                    lastFileUtc = Int64(d.timeIntervalSince1970)
                }

                let appCount = Int((try? db.scalarInt("SELECT COUNT(*) FROM apps WHERE device_id = '\(id)' AND canonical_id IS NULL AND state = 'tracked';")) ?? 0)
                let totalSecs = (try? db.scalarInt("SELECT COALESCE(SUM(seconds), 0) FROM daily_usage WHERE app_id IN (SELECT id FROM apps WHERE device_id = '\(id)');")) ?? 0
                let hours = totalSecs / 3600

                list.append(DeviceRow(
                    deviceId: id,
                    name: name,
                    platform: platform,
                    isLocal: isLocal,
                    firstSeenUtc: firstSeen,
                    lastImportUtc: lastImportUtc,
                    lastFileUtc: lastFileUtc,
                    state: state,
                    appCount: appCount,
                    totalHours: hours
                ))
            }
        } catch {}
        return list
    }

    public func renameDevice(deviceId: String, newName: String) {
        let trimmed = newName.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return }
        do {
            let stmt = try db.prepare("UPDATE devices SET name = ? WHERE device_id = ?;")
            try stmt.bind(text: trimmed, at: 1)
            try stmt.bind(text: deviceId, at: 2)
            _ = try stmt.step()

            if deviceId == localDeviceId {
                self.localDeviceName = trimmed
                try? db.exec("INSERT INTO settings (key, value) VALUES ('device.name', '\(trimmed)') ON CONFLICT(key) DO UPDATE SET value = excluded.value;")
            } else if deviceId == remoteDeviceId {
                self.remoteDeviceName = trimmed
            }
            refresh()
        } catch {}
    }

    public func forgetDevice(deviceId: String) {
        do {
            let stmt = try db.prepare("UPDATE devices SET state = 'forgotten' WHERE device_id = ?;")
            try stmt.bind(text: deviceId, at: 1)
            _ = try stmt.step()
            loadDeviceInfo()
            refresh()
        } catch {}
    }

    public func deleteDeviceHistory(deviceId: String) {
        do {
            try db.transaction {
                try db.exec("DELETE FROM sessions WHERE app_id IN (SELECT id FROM apps WHERE device_id = '\(deviceId)');")
                try db.exec("DELETE FROM daily_usage WHERE app_id IN (SELECT id FROM apps WHERE device_id = '\(deviceId)');")
                try db.exec("DELETE FROM icons WHERE app_id IN (SELECT id FROM apps WHERE device_id = '\(deviceId)');")
                try db.exec("DELETE FROM apps WHERE device_id = '\(deviceId)';")
                try db.exec("DELETE FROM devices WHERE device_id = '\(deviceId)';")
            }
            let acc = UsageAccumulator(store: SQLiteSessionStore(db: db), timeZone: .current)
            try acc.rebuildAll()
            loadDeviceInfo()
            refresh()
        } catch {}
    }

    public func findMatchingCrossDeviceApps() -> [String] {
        var names: [String] = []
        do {
            let stmt = try db.prepare("""
                SELECT display_name FROM apps
                WHERE canonical_id IS NULL AND state = 'tracked'
                GROUP BY display_name
                HAVING COUNT(DISTINCT device_id) > 1;
            """)
            while try stmt.step() {
                if let n = stmt.columnText(at: 0), !n.isEmpty {
                    names.append(n)
                }
            }
        } catch {}
        return names
    }

    public func mergeMatchingCrossDeviceApps() {
        let matchingNames = findMatchingCrossDeviceApps()
        guard !matchingNames.isEmpty else { return }

        do {
            try db.transaction {
                for name in matchingNames {
                    struct AppCandidate {
                        let id: Int64
                        let deviceId: String
                        let addedUtc: Int64
                    }
                    var apps: [AppCandidate] = []
                    let fetch = try db.prepare("""
                        SELECT id, COALESCE(device_id, ''), added_utc
                        FROM apps
                        WHERE display_name = ?1 AND canonical_id IS NULL AND state = 'tracked'
                        ORDER BY id ASC;
                    """)
                    try fetch.bind(text: name, at: 1)
                    while try fetch.step() {
                        let id = fetch.columnInt64(at: 0)
                        let dev = fetch.columnText(at: 1) ?? ""
                        let added = fetch.columnInt64(at: 2)
                        apps.append(AppCandidate(id: id, deviceId: dev, addedUtc: added))
                    }

                    guard apps.count > 1 else { continue }

                    // Pick canonical app: local device if present, else earliest added_utc
                    let canonicalApp: AppCandidate
                    if let local = apps.first(where: { isLocalDevice($0.deviceId) }) {
                        canonicalApp = local
                    } else {
                        canonicalApp = apps.min(by: { $0.addedUtc < $1.addedUtc }) ?? apps[0]
                    }

                    for candidate in apps where candidate.id != canonicalApp.id {
                        let up = try db.prepare("UPDATE apps SET canonical_id = ?1 WHERE id = ?2 OR canonical_id = ?2;")
                        try up.bind(int64: canonicalApp.id, at: 1)
                        try up.bind(int64: candidate.id, at: 2)
                        _ = try up.step()
                    }
                }
            }

            triggerSyncNow()
            loadDeviceInfo()
            refresh()
            objectWillChange.send()
        } catch {
            NSLog("Failed to merge cross-device apps: \(error)")
        }
    }


    public func triggerSyncNow() {
        guard syncEnabled, !syncFolder.isEmpty, FileManager.default.fileExists(atPath: syncFolder) else { return }
        guard !isSyncing else { return }
        isSyncing = true
        let start = Date()

        Task { @MainActor [weak self] in
            guard let self = self else { return }
            if let host = self.syncHost {
                host.syncNow()
                let elapsed = Date().timeIntervalSince(start)
                if elapsed < 0.6 {
                    try? await Task.sleep(nanoseconds: UInt64((0.6 - elapsed) * 1_000_000_000))
                }
                self.isSyncing = false
            } else {
                do {
                    _ = try SyncWriter.write(db: self.db, syncFolder: self.syncFolder)
                    let files = try FileManager.default.contentsOfDirectory(atPath: self.syncFolder)
                    for f in files where f.hasSuffix(SyncWriter.extension) && !f.hasSuffix(".tmp") {
                        let fullPath = (self.syncFolder as NSString).appendingPathComponent(f)
                        let base = (f as NSString).deletingPathExtension
                        if base.caseInsensitiveCompare(self.localDeviceId) == .orderedSame { continue }
                        if SyncReader.isConflictCopy(filePath: fullPath) { continue }
                        if SyncReader.isPlaceholder(filePath: fullPath) { continue }
                        _ = try SyncReader.import(syncFile: fullPath, db: self.db, timeZone: .current)
                    }
                    self.handleSyncCompleted()
                } catch {
                    NSLog("Manual sync error: \(error)")
                }
                let elapsed = Date().timeIntervalSince(start)
                if elapsed < 0.6 {
                    try? await Task.sleep(nanoseconds: UInt64((0.6 - elapsed) * 1_000_000_000))
                }
                self.isSyncing = false
            }
        }
    }

    public func handleSyncCompleted() {
        self.isSyncing = false
        loadSettings()
        loadDeviceInfo()
        refresh()
    }

    // MARK: - Backup and Data Lifecycle

    public var defaultBackupFolder: String {
        let appSupport = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first!
        let folder = appSupport.appendingPathComponent("TimeSink/backups").path
        try? FileManager.default.createDirectory(atPath: folder, withIntermediateDirectories: true)
        return folder
    }

    public func backupNow() -> (success: Bool, error: String?) {
        let df = DateFormatter()
        df.dateFormat = "yyyyMMdd-HHmmss"
        df.timeZone = TimeZone(secondsFromGMT: 0)
        let now = Date()
        let backupFileName = "timesink-backup-\(df.string(from: now)).timesink"
        let backupPath = (defaultBackupFolder as NSString).appendingPathComponent(backupFileName)
        do {
            try BackupService.create(dbPath: db.path, destFile: backupPath, now: now)
            let nowUnix = Int64(now.timeIntervalSince1970)
            try? db.exec("INSERT INTO settings (key, value) VALUES ('backup.last_utc', '\(nowUnix)') ON CONFLICT(key) DO UPDATE SET value = excluded.value;")
            loadSettings()
            return (true, nil)
        } catch {
            return (false, error.localizedDescription)
        }
    }

    public func restore(from backupFile: String, replace: Bool) throws -> String {
        if replace {
            let result = try BackupService.replace(backupFile: backupFile, dbPath: db.path, safetyFolder: defaultBackupFolder, now: Date(), zone: .current)
            loadDeviceInfo()
            loadSettings()
            refresh()
            if let sc = result.safetyCopy {
                return "Replaced database from backup. Safety copy saved to \(URL(fileURLWithPath: sc).lastPathComponent)."
            }
            return "Replaced database from backup."
        } else {
            let result = try BackupService.merge(backupFile: backupFile, dbPath: db.path, zone: .current)
            loadDeviceInfo()
            loadSettings()
            refresh()
            return "Merged \(result.sessionsAdded) sessions (\(result.sessionsSkipped) already existed) and \(result.appsAdded) apps."
        }
    }

    public func exportCsv(folder: String) throws -> (sessionsCsv: String, dailyCsv: String) {
        return try DataExport.exportCsv(dbPath: db.path, folder: folder)
    }

    public func exportJson(file: String) throws {
        try DataExport.exportJson(dbPath: db.path, file: file)
    }

    public func exportMonthCsv() {
        let panel = NSSavePanel()
        panel.nameFieldStringValue = "timesink-month-\(monthResult?.year ?? 2026)-\(monthResult?.month ?? 10).csv"
        if panel.runModal() == .OK, let url = panel.url {
            _ = try? exportCsv(folder: url.deletingLastPathComponent().path)
        }
    }

    public func exportAppCsv(appId: Int64) {
        guard let result = appDetailResult else { return }
        let panel = NSSavePanel()
        let cleanName = result.name.lowercased().replacingOccurrences(of: " ", with: "-")
        panel.nameFieldStringValue = "timesink-\(cleanName).csv"
        if panel.runModal() == .OK, let url = panel.url {
            var csv = "date,app,seconds\r\n"
            let appField = result.name.contains(",") ? "\"\(result.name)\"" : result.name
            if let daySeconds = result.daySeconds {
                for (d, s) in daySeconds.sorted(by: { $0.key < $1.key }) {
                    if s > 0 {
                        csv += "\(d),\(appField),\(s)\r\n"
                    }
                }
            } else {
                for day in result.days {
                    if day.seconds > 0 {
                        csv += "\(day.date),\(appField),\(day.seconds)\r\n"
                    }
                }
            }
            try? csv.write(to: url, atomically: true, encoding: .utf8)
        }
    }

    @discardableResult
    public func saveWrappedImage() -> Bool {
        guard let result = wrappedResult else { return false }
        let panel = NSSavePanel()
        panel.nameFieldStringValue = "timesink-\(result.year)-wrapped-\(wrappedCardIndex + 1).png"
        guard panel.runModal() == .OK, let url = panel.url else { return false }
        let renderer = ImageRenderer(content: WrappedCardView(state: self, result: result, index: wrappedCardIndex).frame(width: 368, height: 470))
        renderer.scale = 2
        if let cg = renderer.cgImage,
           let png = NSBitmapImageRep(cgImage: cg).representation(using: .png, properties: [:]) {
            try? png.write(to: url)
            return true
        }
        return false
    }

    public func deleteAllHistory(confirmation: String) throws {
        try DataExport.deleteAllHistory(db: db, confirmation: confirmation)
        let acc = UsageAccumulator(store: SQLiteSessionStore(db: db), timeZone: .current)
        try acc.rebuildAll()
        loadDeviceInfo()
        loadSettings()
        healUntypedGachaApps()
        refresh()
    }
}

public struct DeviceRow: Identifiable {
    public var id: String { deviceId }
    public let deviceId: String
    public let name: String
    public let platform: String
    public let isLocal: Bool
    public let firstSeenUtc: String
    public let lastImportUtc: Int64?
    public let lastFileUtc: Int64?
    public let state: String
    public let appCount: Int
    public let totalHours: Int64

    // MARK: - Keyboard Navigation & ESC Hierarchy

    public func handleEscape() {
        // 1. Text field focus (AppKit)
        if checkAndHandleTextFieldEscape() {
            return
        }

        handleGoBack()
    }

    public func handleGoBack() {
        // 2. Inner sheets and modals
        if showingKeyboardGuide {
            showingKeyboardGuide = false
            return
        }
        if showingAppIconPicker {
            showingAppIconPicker = false
            return
        }
        if showingRenameMacSheet {
            showingRenameMacSheet = false
            return
        }
        if showingDeleteAllSheet {
            showingDeleteAllSheet = false
            return
        }
        if showingExportSheet {
            showingExportSheet = false
            return
        }
        if showingRestoreSheet {
            showingRestoreSheet = false
            return
        }
        if showingDevicesSync {
            showingDevicesSync = false
            return
        }
        if appSettingsShowingMerge {
            appSettingsShowingMerge = false
            return
        }
        if appSettingsShowingClearConfirm {
            appSettingsShowingClearConfirm = false
            return
        }
        if appSettingsShowingStopConfirm {
            appSettingsShowingStopConfirm = false
            return
        }
        if addAppEditingAppId != nil {
            addAppEditingAppId = nil
            return
        }
        if appDetailShowingSettings {
            appDetailShowingSettings = false
            return
        }

        // 3. Full overlays
        if showingSettings {
            showingSettings = false
            return
        }
        if showingAddApp {
            showingAddApp = false
            if returnToDevicesSyncOnDismiss {
                returnToDevicesSyncOnDismiss = false
                showingSettings = true
                showingDevicesSync = true
            }
            return
        }
        if selectedAppDetailId != nil {
            closeAppDetail()
            return
        }

        // 4. Tab drilldown
        if selectedTab == .today && daysBack > 0 {
            daysBack = 0
            refresh()
            return
        }
        if selectedTab == .week && weeksBack > 0 {
            weeksBack = 0
            refresh()
            return
        }
        if selectedTab == .month && monthsBack > 0 {
            monthsBack = 0
            refresh()
            return
        }
        if selectedTab == .year && yearOffset != 0 {
            yearOffset = 0
            refresh()
            return
        }

        // 5. Dismiss popover
        onClosePopover?()
    }

    public func switchToTab(_ tab: AppTab) {
        showingSettings = false
        showingAddApp = false
        selectedAppDetailId = nil
        selectedTab = tab
    }

    public func cycleTab(forward: Bool) {
        let tabs = AppTab.allCases
        guard let currentIndex = tabs.firstIndex(of: selectedTab) else { return }
        let nextIndex = forward ? (currentIndex + 1) % tabs.count : (currentIndex + tabs.count - 1) % tabs.count
        switchToTab(tabs[nextIndex])
    }

    public func toggleKeyboardGuide() {
        if showingKeyboardGuide {
            showingKeyboardGuide = false
        } else {
            showingAddApp = false
            selectedAppDetailId = nil
            showingSettings = true
            showingKeyboardGuide = true
        }
    }

    public func toggleSettings() {
        if showingSettings {
            showingSettings = false
        } else {
            showingAddApp = false
            selectedAppDetailId = nil
            showingSettings = true
        }
    }

    public func showAddApp() {
        showingSettings = false
        selectedAppDetailId = nil
        showingAddApp = true
    }

    public func navigateDate(step: Int) {
        switch selectedTab {
        case .today:
            if step < 0 { previousDay() } else { nextDay() }
        case .week:
            if step < 0 { previousWeek() } else { nextWeek() }
        case .month:
            if step < 0 { previousMonth() } else { nextMonth() }
        case .year:
            if step < 0 { previousYear() } else { nextYear() }
        case .all:
            break
        }
    }

    public var isDrilldownOrOverlay: Bool {
        showingSettings || showingAddApp || selectedAppDetailId != nil || showingKeyboardGuide ||
        showingDevicesSync || showingAppIconPicker || showingRestoreSheet ||
        showingExportSheet || showingDeleteAllSheet || showingRenameMacSheet ||
        addAppEditingAppId != nil || appSettingsShowingMerge ||
        appSettingsShowingClearConfirm || appSettingsShowingStopConfirm ||
        appDetailShowingSettings ||
        (selectedTab == .today && daysBack > 0) ||
        (selectedTab == .week && weeksBack > 0) ||
        (selectedTab == .month && monthsBack > 0) ||
        (selectedTab == .year && yearOffset != 0)
    }

    private func checkAndHandleTextFieldEscape() -> Bool {
        guard let firstResponder = NSApp.keyWindow?.firstResponder else { return false }
        if let textView = firstResponder as? NSTextView {
            let text = textView.string
            if !text.isEmpty {
                textView.string = ""
                textView.didChangeText()
                return true
            } else {
                NSApp.keyWindow?.makeFirstResponder(nil)
                return true
            }
        } else if let textField = firstResponder as? NSTextField {
            if !textField.stringValue.isEmpty {
                textField.stringValue = ""
                return true
            } else {
                NSApp.keyWindow?.makeFirstResponder(nil)
                return true
            }
        }
        return false
    }

}
