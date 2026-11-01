import SwiftUI
import AppKit

public enum ThemeColors {
    public static let background = Color(red: 0.075, green: 0.082, blue: 0.063) // #131510
    public static let surface = Color(red: 0.110, green: 0.122, blue: 0.090)    // #1c1f17
    public static let surfaceRaised = Color(red: 0.137, green: 0.153, blue: 0.114) // #23271d
    public static let border = Color(red: 0.169, green: 0.188, blue: 0.141)     // #2b3024
    public static let borderSubtle = Color(red: 0.122, green: 0.137, blue: 0.102)

    public static let textPrimary = Color(red: 0.929, green: 0.949, blue: 0.910) // #edf2e8
    public static let textMuted = Color(red: 0.604, green: 0.639, blue: 0.573)   // #9aa392
    public static let textDim = Color(red: 0.384, green: 0.412, blue: 0.361)     // #62695c

    public static let accent = Color(red: 0.784, green: 0.953, blue: 0.416)      // #c8f36a Lime
    public static let accentDark = Color(red: 0.58, green: 0.75, blue: 0.25)
    public static let deviceA = Color(red: 0.784, green: 0.953, blue: 0.416)     // Mac (Lime)
    public static let deviceB = Color(red: 0.220, green: 0.741, blue: 0.973)     // Remote PC (Sky Blue #38bdf8)

    public static let barBackground = Color(red: 0.141, green: 0.157, blue: 0.118) // #24281e
    public static let warning = Color(red: 0.96, green: 0.62, blue: 0.24)
    public static let danger = Color(red: 0.94, green: 0.35, blue: 0.35)
}

public enum Formatters {
    public static func hoursMinutes(seconds: Int64) -> String {
        let h = seconds / 3600
        let m = (seconds % 3600) / 60
        if h > 0 {
            return "\(h)h \(m)m"
        }
        return "\(m)m"
    }

    public static func hoursOrMinutes(seconds: Int64) -> String {
        let h = seconds / 3600
        let m = (seconds % 3600) / 60
        if h > 0 {
            return "\(h)h"
        }
        return "\(m)m"
    }

    public static func formatClock(utcSeconds: Int64, timeZone: TimeZone = .current) -> String {
        let date = Date(timeIntervalSince1970: TimeInterval(utcSeconds))
        let df = DateFormatter()
        df.timeZone = timeZone
        df.dateFormat = "HH:mm"
        return df.string(from: date)
    }
}
