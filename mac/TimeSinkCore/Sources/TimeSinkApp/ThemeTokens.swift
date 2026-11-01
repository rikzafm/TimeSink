import SwiftUI
import AppKit

@MainActor
public final class ThemeManager: ObservableObject {
    public static let shared = ThemeManager()

    @Published public var theme: String = "System"
    @Published public var accent: String = "Lime"

    private init() {}

    public func update(theme: String, accent: String) {
        self.theme = theme
        self.accent = accent
    }

    public var effectiveIsDark: Bool {
        switch theme.lowercased() {
        case "light":
            return false
        case "dark":
            return true
        default: // "system"
            let name = NSApp?.effectiveAppearance.name ?? .darkAqua
            return name != .aqua && name != .vibrantLight
        }
    }

    public var nsAppearance: NSAppearance? {
        switch theme.lowercased() {
        case "light":
            return NSAppearance(named: .aqua)
        case "dark":
            return NSAppearance(named: .darkAqua)
        default:
            return nil
        }
    }
}

@MainActor
public enum ThemeColors {
    // Window & Card Backgrounds
    public static var background: Color {
        ThemeManager.shared.effectiveIsDark
            ? Color(red: 0.067, green: 0.078, blue: 0.059) // #11140f
            : Color(hex: 0xf6f7f2)
    }
    public static var canvasBg: Color {
        ThemeManager.shared.effectiveIsDark
            ? Color(red: 0.039, green: 0.047, blue: 0.031) // #0a0c08
            : Color(hex: 0xeceee5)
    }
    public static var surface: Color {
        ThemeManager.shared.effectiveIsDark
            ? Color(red: 0.078, green: 0.098, blue: 0.063) // #141910
            : Color(hex: 0xffffff)
    }
    public static var surfaceRaised: Color {
        ThemeManager.shared.effectiveIsDark
            ? Color(red: 0.169, green: 0.192, blue: 0.145) // #2b3125
            : Color(hex: 0xe3e6d9)
    }
    public static var surfaceActive: Color {
        ThemeManager.shared.effectiveIsDark
            ? Color(red: 0.086, green: 0.106, blue: 0.067) // #161b11
            : Color(hex: 0xebefe3)
    }

    // Borders
    public static var border: Color {
        ThemeManager.shared.effectiveIsDark
            ? Color(red: 0.169, green: 0.192, blue: 0.145) // #2b3125
            : Color(hex: 0xd5d9c8)
    }
    public static var borderSubtle: Color {
        ThemeManager.shared.effectiveIsDark
            ? Color(red: 0.137, green: 0.165, blue: 0.114) // #232a1d
            : Color(hex: 0xe3e6d9)
    }

    // Typography
    public static var textPrimary: Color {
        ThemeManager.shared.effectiveIsDark
            ? Color(red: 0.933, green: 0.949, blue: 0.902) // #eef2e6
            : Color(hex: 0x1a1f14)
    }
    public static var textMuted: Color {
        ThemeManager.shared.effectiveIsDark
            ? Color(red: 0.604, green: 0.639, blue: 0.569) // #9aa391
            : Color(hex: 0x5f6855)
    }
    public static var textDim: Color {
        ThemeManager.shared.effectiveIsDark
            ? Color(red: 0.812, green: 0.839, blue: 0.769) // #cfd6c4
            : Color(hex: 0x3b4330)
    }
    public static var textDisabled: Color {
        ThemeManager.shared.effectiveIsDark
            ? Color(red: 0.290, green: 0.333, blue: 0.278) // #4a5547
            : Color(hex: 0x9aa391)
    }

    // Accents & Devices
    public static var accent: Color {
        switch ThemeManager.shared.accent.lowercased() {
        case "blue":
            return Color(hex: 0x5fa8ff)
        case "pink":
            return Color(hex: 0xff9fb4)
        default: // "lime"
            return ThemeManager.shared.effectiveIsDark ? Color(hex: 0xc6f26b) : Color(hex: 0x4f7a0a)
        }
    }
    public static var onAccent: Color {
        ThemeManager.shared.effectiveIsDark ? Color(red: 0.067, green: 0.078, blue: 0.059) : Color.white
    }
    public static var accentDark: Color {
        switch ThemeManager.shared.accent.lowercased() {
        case "blue":
            return Color(hex: 0x3f7fcc)
        case "pink":
            return Color(hex: 0xc43c62)
        default: // "lime"
            return Color(hex: 0x75a826)
        }
    }
    public static let greenDark = Color(red: 0.459, green: 0.659, blue: 0.149)   // #75a826
    public static let greenMid = Color(red: 0.275, green: 0.412, blue: 0.122)    // #46691f
    public static let greenLow = Color(red: 0.176, green: 0.239, blue: 0.110)    // #2d3d1c
    public static let greenTrack = Color(red: 0.106, green: 0.129, blue: 0.086)  // #1b2116

    public static var deviceA: Color { accent }
    public static let deviceB = Color(red: 0.373, green: 0.659, blue: 1.000)     // PC: Blue #5fa8ff
    public static let deviceBDark = Color(red: 0.247, green: 0.498, blue: 0.800) // PC Bar: #3f7fcc

    public static var barBackground: Color {
        ThemeManager.shared.effectiveIsDark ? Color(hex: 0x1b2116) : Color(hex: 0xe6e9dc)
    }
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
