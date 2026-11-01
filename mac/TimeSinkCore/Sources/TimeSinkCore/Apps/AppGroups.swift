import Foundation

public enum AppState: String, Sendable, Codable {
    case tracked
    case retired
}

public struct GroupedApp: Sendable, Equatable {
    public let id: Int64
    public var group: Int64
    public let name: String
    public let addedUtc: Int64
    public let background: Bool
    public let kind: AppKind
    public let state: AppState
    public let retiredUtc: Int64
    public let exePath: String
    public let deviceId: String

    public var isRetired: Bool { state == .retired }

    public init(
        id: Int64,
        group: Int64,
        name: String,
        addedUtc: Int64,
        background: Bool = false,
        kind: AppKind = .other,
        state: AppState = .tracked,
        retiredUtc: Int64 = 0,
        exePath: String = "",
        deviceId: String = ""
    ) {
        self.id = id
        self.group = group
        self.name = name
        self.addedUtc = addedUtc
        self.background = background
        self.kind = kind
        self.state = state
        self.retiredUtc = retiredUtc
        self.exePath = exePath
        self.deviceId = deviceId
    }
}

public final class AppGroups: @unchecked Sendable {
    private var apps: [Int64: GroupedApp]

    public init(apps: [Int64: GroupedApp]) {
        self.apps = apps
    }

    public var count: Int { apps.count }
    public var values: Dictionary<Int64, GroupedApp>.Values { apps.values }
    public subscript(id: Int64) -> GroupedApp? { apps[id] }
    public func containsKey(_ id: Int64) -> Bool { apps[id] != nil }
    public func tryGetValue(_ id: Int64) -> GroupedApp? { apps[id] }
    public func groupOf(_ id: Int64) -> Int64 { apps[id]?.group ?? id }

    public func members(group: Int64) -> [GroupedApp] {
        apps.values
            .filter { $0.group == group }
            .sorted { (a, b) in
                if (a.id == group) != (b.id == group) {
                    return a.id == group
                }
                return a.id < b.id
            }
    }

    public static func from(rows: [GroupedApp]) -> AppGroups {
        var dict: [Int64: GroupedApp] = [:]
        for a in rows { dict[a.id] = a }
        for a in dict.values {
            if a.group != a.id {
                if let main = dict[a.group] {
                    if main.isRetired && !a.isRetired {
                        var updated = a
                        updated.group = a.id
                        dict[a.id] = updated
                    }
                } else {
                    var updated = a
                    updated.group = a.id
                    dict[a.id] = updated
                }
            }
        }
        return AppGroups(apps: dict)
    }

    public static func load(
        db: SQLiteDatabase,
        includeRetired: Bool = false,
        gamesOnly: Bool = false,
        deviceScope: DeviceScope = .this
    ) throws -> AppGroups {
        let sql = """
        SELECT id, COALESCE(canonical_id, id), display_name, added_utc, is_background, exe_path, kind, state, retired_utc, device_id
        FROM apps
        \(includeRetired ? "" : "WHERE state = 'tracked'")
        """
        let stmt = try db.prepare(sql)
        var rows: [GroupedApp] = []
        while try stmt.step() {
            let id = stmt.columnInt64(at: 0)
            let grp = stmt.columnInt64(at: 1)
            let name = stmt.columnText(at: 2) ?? ""
            let addedUtc = stmt.columnInt64(at: 3)
            let bg = stmt.columnInt64(at: 4) != 0
            let path = stmt.columnText(at: 5) ?? ""
            let kindVal = stmt.columnIsNull(at: 6) ? nil : stmt.columnInt64(at: 6)
            let stateStr = stmt.columnText(at: 7) ?? "tracked"
            let retUtc = stmt.columnIsNull(at: 8) ? 0 : stmt.columnInt64(at: 8)
            let devId = stmt.columnIsNull(at: 9) ? "" : (stmt.columnText(at: 9) ?? "")
            let kind = AppKinds.resolve(kind: kindVal, exePath: path, displayName: name)
            let state = (stateStr.lowercased() == "retired") ? AppState.retired : AppState.tracked
            rows.append(GroupedApp(
                id: id,
                group: grp,
                name: name,
                addedUtc: addedUtc,
                background: bg,
                kind: kind,
                state: state,
                retiredUtc: retUtc,
                exePath: path,
                deviceId: devId
            ))
        }

        var groups = from(rows: rows)
        if deviceScope == .this {
            let localDevId = try? db.scalarText("SELECT COALESCE((SELECT value FROM settings WHERE key = 'device.id'), (SELECT device_id FROM devices WHERE is_local = 1))")
            if let devId = localDevId, !devId.isEmpty {
                groups = groups.onlyDevice(devId)
            }
        }
        return gamesOnly ? groups.onlyGames() : groups
    }

    public func onlyDevice(_ deviceId: String) -> AppGroups {
        let filtered = apps.filter { $0.value.deviceId.isEmpty || $0.value.deviceId == deviceId }
        return AppGroups(apps: Dictionary(uniqueKeysWithValues: filtered.map { ($0.key, $0.value) }))
    }

    public func onlyGames() -> AppGroups {
        let filtered = apps.filter {
            let main = apps[$0.value.group] ?? $0.value
            return main.kind != .other
        }
        return AppGroups(apps: Dictionary(uniqueKeysWithValues: filtered.map { ($0.key, $0.value) }))
    }
}

