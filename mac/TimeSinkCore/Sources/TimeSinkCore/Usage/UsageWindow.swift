import Foundation

public final class UsageWindow: @unchecked Sendable {
    public let db: SQLiteDatabase
    public let context: UsageContext
    public var Context: UsageContext { context }
    private let shouldCloseDb: Bool

    private var _apps: AppGroups?
    private var _allApps: AppGroups?
    private var _rawSessions: [(id: Int64, appId: Int64, startUtc: Int64, endUtc: Int64?, lastSeenUtc: Int64, endReason: String?)]?
    private var _rawFrom: Int64 = Int64.max

    public init(db: SQLiteDatabase, context: UsageContext, shouldCloseDb: Bool = false) {
        self.db = db
        self.context = context
        self.shouldCloseDb = shouldCloseDb
    }

    deinit {
        if shouldCloseDb {
            db.close()
        }
    }

    public var nowUtc: Int64 {
        Int64(context.now.timeIntervalSince1970)
    }

    public var todayStr: String {
        LocalDays.dayString(for: nowUtc, timeZone: context.zone)
    }

    public var localDeviceId: String? {
        if let id = context.localDeviceId, !id.isEmpty { return id }
        return try? db.scalarText("SELECT COALESCE((SELECT value FROM settings WHERE key = 'device.id'), (SELECT device_id FROM devices WHERE is_local = 1))")
    }

    public var apps: AppGroups {
        if let cached = _apps { return cached }
        let loaded = (try? AppGroups.load(db: db, includeRetired: false, gamesOnly: context.gamesOnly, deviceScope: context.deviceScope)) ?? AppGroups(apps: [:])
        _apps = loaded
        return loaded
    }

    public var allApps: AppGroups {
        if let cached = _allApps { return cached }
        let loaded = (try? AppGroups.load(db: db, includeRetired: true, gamesOnly: context.gamesOnly, deviceScope: context.deviceScope)) ?? AppGroups(apps: [:])
        _allApps = loaded
        return loaded
    }

    public static func over<T>(context: UsageContext, _ read: (UsageWindow) throws -> T) throws -> T {
        if let existingDb = context.db {
            let window = UsageWindow(db: existingDb, context: context, shouldCloseDb: false)
            return try read(window)
        } else if let path = context.dbPath {
            let db = try SQLiteDatabase(path: path)
            let window = UsageWindow(db: db, context: context, shouldCloseDb: true)
            return try read(window)
        } else {
            throw SQLiteError.generic("The context has no database.")
        }
    }

    public func segments(from: Int64, to: Int64? = nil, includeRetired: Bool = false) -> [UsageSeg] {
        if _rawSessions == nil || from < _rawFrom {
            loadRawSessions(from: from)
            _rawFrom = from
        }

        let appGroupList = includeRetired ? allApps : apps
        var segs: [UsageSeg] = []

        for s in _rawSessions ?? [] {
            if let end = s.endUtc, end < from { continue }
            if let to = to, s.startUtc >= to { continue }
            guard let app = appGroupList[s.appId] else { continue }

            let open = (s.endUtc == nil)
            var endUtc: Int64 = s.endUtc ?? s.lastSeenUtc
            var isOpen = open

            if open {
                if context.running == nil || context.running!.contains(s.appId) {
                    endUtc = max(nowUtc, s.startUtc)
                } else {
                    endUtc = min(max(s.lastSeenUtc, s.startUtc), nowUtc)
                    isOpen = false
                }
            }

            let sleep = s.endReason == "sleep" && !isOpen
            let off = s.endReason == "shutdown" && !isOpen
            segs.append(UsageSeg(
                group: app.group,
                appId: s.appId,
                start: s.startUtc,
                end: endUtc,
                open: isOpen,
                sleep: sleep,
                deviceId: app.deviceId,
                off: off
            ))
        }

        return segs
    }

    private func loadRawSessions(from: Int64) {
        let sql = """
        SELECT id, app_id, start_utc, end_utc, last_seen_utc, end_reason
        FROM sessions
        WHERE (end_utc IS NULL OR end_utc >= ?1)
        ORDER BY id;
        """
        guard let stmt = try? db.prepare(sql) else { return }
        try? stmt.bind(int64: from, at: 1)

        var list: [(id: Int64, appId: Int64, startUtc: Int64, endUtc: Int64?, lastSeenUtc: Int64, endReason: String?)] = []
        while (try? stmt.step()) == true {
            let id = stmt.columnInt64(at: 0)
            let appId = stmt.columnInt64(at: 1)
            let start = stmt.columnInt64(at: 2)
            let end = stmt.columnIsNull(at: 3) ? nil : stmt.columnInt64(at: 3)
            let lastSeen = stmt.columnInt64(at: 4)
            let reason = stmt.columnIsNull(at: 5) ? nil : stmt.columnText(at: 5)
            list.append((id: id, appId: appId, startUtc: start, endUtc: end, lastSeenUtc: lastSeen, endReason: reason))
        }
        self._rawSessions = list
    }
}
