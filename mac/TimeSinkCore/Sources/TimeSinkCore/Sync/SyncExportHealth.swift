import Foundation

/// Small pure helpers for reporting the health of this device's own sync export.
public enum SyncExportHealth {
    /// Our own export is considered stale after 15 minutes (the periodic timer runs every 5).
    public static let staleAfterSeconds: Int64 = 15 * 60

    /// `lastExportUtc == 0` means "never exported", which is not reported as stale.
    public static func isStale(lastExportUtc: Int64, now: Int64) -> Bool {
        lastExportUtc > 0 && now - lastExportUtc > staleAfterSeconds
    }

    /// Short text for `sync.last_error`: error domain and code only, so it never contains
    /// file paths or quote characters (it is written into the settings table and shown in the UI).
    public static func shortErrorText(_ error: Error) -> String {
        let ns = error as NSError
        let domain = ns.domain.filter { $0.isLetter || $0.isNumber || $0 == "." || $0 == "_" }
        return "\(domain) \(ns.code)"
    }
}
