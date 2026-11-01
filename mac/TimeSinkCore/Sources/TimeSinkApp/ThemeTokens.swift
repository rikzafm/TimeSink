import SwiftUI
import AppKit

public enum ThemeColors {
    // Window & Card Backgrounds
    public static let background = Color(red: 0.067, green: 0.078, blue: 0.059) // #11140f
    public static let canvasBg = Color(red: 0.039, green: 0.047, blue: 0.031)   // #0a0c08
    public static let surface = Color(red: 0.078, green: 0.098, blue: 0.063)    // #141910
    public static let surfaceRaised = Color(red: 0.169, green: 0.192, blue: 0.145) // #2b3125
    public static let surfaceActive = Color(red: 0.086, green: 0.106, blue: 0.067) // #161b11

    // Borders
    public static let border = Color(red: 0.169, green: 0.192, blue: 0.145)     // #2b3125
    public static let borderSubtle = Color(red: 0.137, green: 0.165, blue: 0.114) // #232a1d

    // Typography
    public static let textPrimary = Color(red: 0.933, green: 0.949, blue: 0.902) // #eef2e6
    public static let textMuted = Color(red: 0.604, green: 0.639, blue: 0.569)   // #9aa391
    public static let textDim = Color(red: 0.812, green: 0.839, blue: 0.769)     // #cfd6c4
    public static let textDisabled = Color(red: 0.290, green: 0.333, blue: 0.278) // #4a5547

    // Accents & Devices
    public static let accent = Color(red: 0.776, green: 0.949, blue: 0.420)      // #c6f26b Lime
    public static let accentDark = Color(red: 0.459, green: 0.659, blue: 0.149)   // #75a826
    public static let greenDark = Color(red: 0.459, green: 0.659, blue: 0.149)   // #75a826
    public static let greenMid = Color(red: 0.275, green: 0.412, blue: 0.122)    // #46691f
    public static let greenLow = Color(red: 0.176, green: 0.239, blue: 0.110)    // #2d3d1c
    public static let greenTrack = Color(red: 0.106, green: 0.129, blue: 0.086)  // #1b2116

    public static let deviceA = Color(red: 0.776, green: 0.949, blue: 0.420)     // Mac: Lime #c6f26b
    public static let deviceB = Color(red: 0.373, green: 0.659, blue: 1.000)     // PC: Blue #5fa8ff
    public static let deviceBDark = Color(red: 0.247, green: 0.498, blue: 0.800) // PC Bar: #3f7fcc

    public static let barBackground = Color(red: 0.106, green: 0.129, blue: 0.086) // #1b2116
    public static let warning = Color(red: 1.000, green: 0.722, blue: 0.420)     // #ffb86b
    public static let danger = Color(red: 1.000, green: 0.541, blue: 0.478)      // #ff8a7a
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

