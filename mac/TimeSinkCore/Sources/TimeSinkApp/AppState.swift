import Foundation
import SwiftUI
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

public enum AllSortOption: String, CaseIterable, Identifiable {
    case time = "Hours"
    case name = "Name"
    case lastOpened = "Last opened"

    public var id: String { rawValue }
}

@MainActor
public final class AppState: ObservableObject {
    public let db: SQLiteDatabase
    public let iconCache: MacIconCache

    @Published public var selectedTab: AppTab = .today
    @Published public var deviceScope: DeviceScope = .this
    @Published public var gamesOnly: Bool = false

    @Published public var hasMultipleDevices: Bool = false
    @Published public var localDeviceId: String = ""
    @Published public var localDeviceName: String = "Mac"
    @Published public var remoteDeviceId: String?
    @Published public var remoteDeviceName: String? = "PC"

    // Navigation and sub-views
    @Published public var weeksBack: Int = 0
    @Published public var monthsBack: Int = 0
    @Published public var yearOffset: Int = 0
    @Published public var yearSubView: YearSubView = .pace
    @Published public var wrappedCardIndex: Int = 0

    @Published public var selectedAppDetailId: Int64?
    @Published public var showingAddApp: Bool = false
    @Published public var showingSettings: Bool = false

    // All tab search and sort
    @Published public var allSearchQuery: String = ""
    @Published public var allSort: AllSortOption = .time

    // Cached query results
    @Published public var todayResult: TodayResult?
    @Published public var weekResult: WeekResult?
    @Published public var monthResult: MonthResult?
    @Published public var yearResult: YearResult?
    @Published public var matrixResult: MatrixResult?
    @Published public var wrappedResult: WrappedResult?
    @Published public var allResult: AllResult?
    @Published public var appDetailResult: AppDetailResult?

    public init(db: SQLiteDatabase) {
        self.db = db
        self.iconCache = MacIconCache(db: db)
        loadDeviceInfo()
        refresh()
    }

    public func loadDeviceInfo() {
        do {
            let devId = try db.scalarText("SELECT value FROM settings WHERE key = 'device.id';") ?? ""
            let devName = try db.scalarText("SELECT value FROM settings WHERE key = 'device.name';") ?? "Mac"
            self.localDeviceId = devId
            self.localDeviceName = devName

            // Query devices table
            let stmt = try db.prepare("SELECT device_id, name, is_local FROM devices WHERE state = 'active' ORDER BY is_local DESC;")
            var remoteFound = false
            while try stmt.step() {
                let id = stmt.columnText(at: 0)
                let name = stmt.columnText(at: 1)
                let isLocal = stmt.columnInt64(at: 2) == 1
                if !isLocal && !remoteFound {
                    self.remoteDeviceId = id
                    self.remoteDeviceName = name
                    remoteFound = true
                }
            }
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

    public func makeUsageContext() -> UsageContext {
        let now = Date()
        let runningPids = Set<Int64>() // Populated live or empty
        return UsageContext(
            now: now,
            zone: .current,
            running: runningPids,
            signInUtc: nil,
            weekStart: .mon,
            gamesOnly: gamesOnly,
            db: db,
            deviceScope: deviceScope,
            localDeviceId: localDeviceId
        )
    }

    public func refresh() {
        let ctx = makeUsageContext()
        do {
            switch selectedTab {
            case .today:
                todayResult = try TodayQuery.run(ctx)
            case .week:
                weekResult = try WeekQuery.run(ctx, weeksBack: weeksBack)
            case .month:
                let cal = Calendar.current
                let now = Date()
                let targetDate = cal.date(byAdding: .month, value: -monthsBack, to: now) ?? now
                let y = cal.component(.year, from: targetDate)
                let m = cal.component(.month, from: targetDate)
                monthResult = try MonthQuery.run(ctx, year: y, month: m)
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
                let currentYear = Calendar.current.component(.year, from: Date())
                appDetailResult = AppDetailQuery.run(context: ctx, appId: appId, year: currentYear)
            }
        } catch {
            NSLog("Error executing query for \(selectedTab): \(error)")
        }
    }

    public func openAppDetail(appId: Int64) {
        self.selectedAppDetailId = appId
        let ctx = makeUsageContext()
        let currentYear = Calendar.current.component(.year, from: Date())
        self.appDetailResult = AppDetailQuery.run(context: ctx, appId: appId, year: currentYear)
    }

    public func closeAppDetail() {
        self.selectedAppDetailId = nil
        self.appDetailResult = nil
        refresh()
    }
}
