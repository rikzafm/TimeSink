import Foundation

public final class UsageContext: @unchecked Sendable {
    public let dbPath: String?
    public let db: SQLiteDatabase?
    public let now: Date
    public let zone: TimeZone
    public let gamesOnly: Bool
    public let deviceScope: DeviceScope
    public let localDeviceId: String?
    public let running: Set<Int64>?
    public let signInUtc: Int64?
    public let weekStart: WeekStart

    public init(
        now: Date,
        zone: TimeZone,
        running: Set<Int64>? = nil,
        signInUtc: Int64? = nil,
        weekStart: WeekStart = .mon,
        gamesOnly: Bool = false,
        dbPath: String? = nil,
        db: SQLiteDatabase? = nil,
        deviceScope: DeviceScope = .this,
        localDeviceId: String? = nil
    ) {
        self.now = now
        self.zone = zone
        self.running = running
        self.signInUtc = signInUtc
        self.weekStart = weekStart
        self.gamesOnly = gamesOnly
        self.dbPath = dbPath
        self.db = db
        self.deviceScope = deviceScope
        self.localDeviceId = localDeviceId
    }
}

