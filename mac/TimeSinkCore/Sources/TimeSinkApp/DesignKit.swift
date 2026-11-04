import SwiftUI
import AppKit
import TimeSinkCore

// Shared pieces of the design boards in design/mac. Sizes are the board's own points.

public extension Color {
    init(hex: UInt32) {
        self.init(
            red: Double((hex >> 16) & 0xff) / 255,
            green: Double((hex >> 8) & 0xff) / 255,
            blue: Double(hex & 0xff) / 255
        )
    }
}

public enum Tokens {
    public static let bar = Color(hex: 0x161912)
    public static let segmentBg = Color(hex: 0x181d13)
    public static let well = Color(hex: 0x0e110b)
    public static let fieldBorder = Color(hex: 0x3a4434)
    public static let dangerBorder = Color(hex: 0x4a2a26)
    public static let warnBg = Color(hex: 0x1a1810)
    public static let warnBorder = Color(hex: 0x3a3320)
    public static let track = Color(hex: 0x232a1d)
    public static let winBlue = Color(hex: 0x8fd3ff)
    public static let badge = Color(hex: 0x6fc26f)
    public static let typical = Color(hex: 0x46514a)
    public static let ramp: [Color] = [
        Color(hex: 0x1b2116), Color(hex: 0x2d3d1c), Color(hex: 0x46691f), Color(hex: 0x75a826), Color(hex: 0xc6f26b)
    ]
    public static let appPalette: [Color] = [
        Color(hex: 0x8fd3ff), Color(hex: 0xffe08a), Color(hex: 0xd3a6ff), Color(hex: 0xffb86b),
        Color(hex: 0xff9fb4), Color(hex: 0xc6f26b), Color(hex: 0x5fa8ff)
    ]

    /// Stable per-app colour, so one app keeps its colour on every screen.
    public static func appColor(_ name: String) -> Color {
        appPalette[name.unicodeScalars.reduce(0) { ($0 &* 31 &+ Int($1.value)) & 0xffff } % appPalette.count]
    }

    public static func heat(_ level: Int) -> Color {
        ramp[max(0, min(ramp.count - 1, level))]
    }
}

public extension Formatters {
    /// Whole hours with a thin space, as the boards print them: "283 h".
    static func hours(_ seconds: Int64) -> String {
        "\(grouped((seconds + 1800) / 3600)) h"
    }

    /// Formats hours with one decimal place under 10 ("1.5"), or whole hours without decimal ("14", "10", "2").
    static func decimalHours(_ seconds: Int64) -> String {
        let h = Double(max(0, seconds)) / 3600.0
        if h < 9.95 {
            let rounded = (h * 10).rounded() / 10
            if rounded.truncatingRemainder(dividingBy: 1) == 0 {
                return "\(Int(rounded))"
            } else {
                return String(format: "%.1f", rounded)
            }
        }
        return "\(Int(round(h)))"
    }

    static func grouped(_ value: Int64) -> String {
        let f = NumberFormatter()
        f.numberStyle = .decimal
        f.groupingSeparator = ","
        return f.string(from: NSNumber(value: value)) ?? "\(value)"
    }

    /// "2h 08m": minutes keep two digits once there are hours.
    static func paddedHM(_ seconds: Int64) -> String {
        let h = seconds / 3600
        let m = (seconds % 3600) / 60
        return h > 0 ? "\(h)h \(String(format: "%02d", m))m" : "\(m)m"
    }

    static func clock12(_ utc: Int64) -> String {
        let df = DateFormatter()
        df.dateFormat = "h:mm a"
        return df.string(from: Date(timeIntervalSince1970: TimeInterval(utc))).lowercased()
    }

    static func timeAgo(_ utc: Int64) -> String {
        guard utc > 0 else { return "never" }
        let span = Date().timeIntervalSince1970 - Double(utc)
        if span < 60 { return "just now" }
        if span < 3600 { return "\(Int(span / 60)) min ago" }
        if span < 86400 { return "\(Int(span / 3600)) h ago" }
        let days = Int(span / 86400)
        return "\(days) d ago"
    }

    /// Parses a local "yyyy-MM-dd" day string.
    static func day(_ text: String) -> Date? {
        let df = DateFormatter()
        df.dateFormat = "yyyy-MM-dd"
        return df.date(from: String(text.prefix(10)))
    }

    static func dayText(_ text: String?, format: String) -> String {
        guard let text, let d = day(text) else { return "" }
        let df = DateFormatter()
        df.dateFormat = format
        return df.string(from: d)
    }

    static func monthName(_ month: Int, short: Bool = false) -> String {
        let df = DateFormatter()
        let list: [String] = short ? df.shortMonthSymbols : df.monthSymbols
        return list[max(0, min(11, month - 1))]
    }

    static func hour12(_ hour: Int) -> String {
        let h = (hour % 24 + 24) % 24
        let mod = h % 12
        let num = mod == 0 ? 12 : mod
        let ampm = h < 12 ? "am" : "pm"
        return "\(num) \(ampm)"
    }

    static func hourRange(_ hour: Int) -> String {
        let h1 = hour12(hour)
        let h2 = hour12(hour + 1)
        return "\(h1) – \(h2)"
    }

    static func hour24Range(_ hour: Int) -> String {
        let h1 = (hour % 24 + 24) % 24
        let h2 = ((hour + 1) % 24 + 24) % 24
        return String(format: "%02d:00 – %02d:00", h1, h2)
    }

    static func clock24(_ utc: Int64) -> String {
        let df = DateFormatter()
        df.dateFormat = "HH:mm"
        return df.string(from: Date(timeIntervalSince1970: TimeInterval(utc)))
    }
}

public struct FlowLayout: Layout {
    public var spacing: CGFloat
    public var lineSpacing: CGFloat

    public init(spacing: CGFloat = 6, lineSpacing: CGFloat = 6) {
        self.spacing = spacing
        self.lineSpacing = lineSpacing
    }

    public func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let maxW = proposal.width ?? .infinity
        var currentX: CGFloat = 0
        var currentY: CGFloat = 0
        var lineHeight: CGFloat = 0
        var maxWidth: CGFloat = 0

        for subview in subviews {
            let size = subview.sizeThatFits(.unspecified)
            if currentX + size.width > maxW, currentX > 0 {
                currentX = 0
                currentY += lineHeight + lineSpacing
                lineHeight = 0
            }
            lineHeight = max(lineHeight, size.height)
            currentX += size.width + spacing
            maxWidth = max(maxWidth, currentX - spacing)
        }
        return CGSize(width: maxWidth, height: currentY + lineHeight)
    }

    public func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        var currentX = bounds.minX
        var currentY = bounds.minY
        var lineHeight: CGFloat = 0

        for subview in subviews {
            let size = subview.sizeThatFits(.unspecified)
            if currentX + size.width > bounds.maxX, currentX > bounds.minX {
                currentX = bounds.minX
                currentY += lineHeight + lineSpacing
                lineHeight = 0
            }
            subview.place(at: CGPoint(x: currentX, y: currentY), proposal: ProposedViewSize(size))
            lineHeight = max(lineHeight, size.height)
            currentX += size.width + spacing
        }
    }
}

// MARK: - Icons (the boards' SVG paths)

public enum TSIcon {
    case gamepad, gear, gearLarge, eye, eyeSlash, lashes, layers, back, close, search, searchSmall
    case folder, upload, clock, history, tableOptions, triangleLeft, triangleRight, sync, sliders, chartBars
    case sparkle, socialMedia, browsing, coding, productivity, entertainment, appsGrid

    var box: CGFloat {
        switch self {
        case .gamepad, .gear, .gearLarge, .sync, .sparkle, .socialMedia, .browsing, .coding, .productivity, .entertainment, .appsGrid: return 24
        case .eye, .eyeSlash, .layers: return 18
        case .lashes: return 20
        case .close: return 14
        case .upload: return 28
        case .clock, .history: return 12
        case .triangleLeft, .triangleRight: return 10
        default: return 16
        }
    }

    var filled: Bool { self == .triangleLeft || self == .triangleRight }

    var path: Path {
        var p = Path()
        func line(_ a: CGPoint, _ b: CGPoint) { p.move(to: a); p.addLine(to: b) }
        func pt(_ x: CGFloat, _ y: CGFloat) -> CGPoint { CGPoint(x: x, y: y) }
        func dot(_ x: CGFloat, _ y: CGFloat, _ r: CGFloat) {
            p.addEllipse(in: CGRect(x: x - r, y: y - r, width: r * 2, height: r * 2))
        }
        func eyeOutline() {
            p.move(to: pt(1.5, 9))
            p.addCurve(to: pt(9, 4), control1: pt(1.5, 9), control2: pt(4.5, 4))
            p.addCurve(to: pt(16.5, 9), control1: pt(13.5, 4), control2: pt(16.5, 9))
            p.addCurve(to: pt(9, 14), control1: pt(16.5, 9), control2: pt(13.5, 14))
            p.addCurve(to: pt(1.5, 9), control1: pt(4.5, 14), control2: pt(1.5, 9))
            p.closeSubpath()
            dot(9, 9, 2.2)
        }
        switch self {
        case .gamepad:
            p = SVGPath.parse("M6 11h4M8 9v4M15 12h.01M18 10h.01M17.3 5H6.7a4 4 0 0 0-3.9 3.2l-1.5 7A3 3 0 0 0 4.2 19c.9 0 1.6-.5 2.1-1.2l1.1-1.8h9.2l1.1 1.8c.5.7 1.2 1.2 2.1 1.2a3 3 0 0 0 2.9-3.8l-1.5-7A4 4 0 0 0 17.3 5z")
        case .gear, .gearLarge:
            p = SVGPath.parse("M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z")
            dot(12, 12, 3)
        case .eye:
            eyeOutline()
        case .eyeSlash:
            eyeOutline()
            line(pt(3, 15), pt(15, 3))
        case .lashes:
            p.move(to: pt(2.5, 8.5))
            p.addCurve(to: pt(10, 13.5), control1: pt(4.5, 12.1), control2: pt(6.9, 13.5))
            p.addCurve(to: pt(17.5, 8.5), control1: pt(13.1, 13.5), control2: pt(15.5, 12.1))
            line(pt(5, 12), pt(3.5, 14.2)); line(pt(8, 13.6), pt(7.4, 16.2)); line(pt(12, 13.6), pt(12.6, 16.2)); line(pt(15, 12), pt(16.5, 14.2))
        case .layers:
            p.move(to: pt(9, 2.5)); p.addLine(to: pt(15.5, 5.8)); p.addLine(to: pt(9, 9)); p.addLine(to: pt(2.5, 5.8)); p.closeSubpath()
            p.move(to: pt(2.5, 9)); p.addLine(to: pt(9, 12.2)); p.addLine(to: pt(15.5, 9))
            p.move(to: pt(2.5, 12.2)); p.addLine(to: pt(9, 15.5)); p.addLine(to: pt(15.5, 12.2))
        case .back:
            p.move(to: pt(10, 3)); p.addLine(to: pt(5, 8)); p.addLine(to: pt(10, 13))
        case .close:
            line(pt(2, 2), pt(12, 12)); line(pt(12, 2), pt(2, 12))
        case .search:
            dot(7, 7, 4.8); line(pt(10.7, 10.7), pt(14, 14))
        case .searchSmall:
            dot(7, 7, 4.5); line(pt(10.5, 10.5), pt(14, 14))
        case .folder:
            p.move(to: pt(1.5, 4.5))
            p.addQuadCurve(to: pt(2.5, 3.5), control: pt(1.5, 3.5))
            p.addLine(to: pt(6, 3.5)); p.addLine(to: pt(7.5, 5)); p.addLine(to: pt(13.5, 5))
            p.addQuadCurve(to: pt(14.5, 6), control: pt(14.5, 5))
            p.addLine(to: pt(14.5, 12.5))
            p.addQuadCurve(to: pt(13.5, 13.5), control: pt(14.5, 13.5))
            p.addLine(to: pt(2.5, 13.5))
            p.addQuadCurve(to: pt(1.5, 12.5), control: pt(1.5, 13.5))
            p.closeSubpath()
        case .upload:
            line(pt(14, 18), pt(14, 5))
            p.move(to: pt(8.5, 10.5)); p.addLine(to: pt(14, 5)); p.addLine(to: pt(19.5, 10.5))
            p.move(to: pt(4, 17)); p.addLine(to: pt(4, 21))
            p.addQuadCurve(to: pt(6, 23), control: pt(4, 23))
            p.addLine(to: pt(22, 23))
            p.addQuadCurve(to: pt(24, 21), control: pt(24, 23))
            p.addLine(to: pt(24, 17))
        case .clock:
            dot(6, 6, 4.8)
            p.move(to: pt(6, 3.5)); p.addLine(to: pt(6, 6)); p.addLine(to: pt(7.8, 7.2))
        case .history:
            p.addArc(center: pt(6, 6), radius: 4, startAngle: .degrees(180), endAngle: .degrees(-132), clockwise: true)
            p.move(to: pt(2, 1.8)); p.addLine(to: pt(2, 4)); p.addLine(to: pt(4.2, 4))
        case .tableOptions:
            p.addRoundedRect(in: CGRect(x: 1.5, y: 2.5, width: 4, height: 4), cornerSize: CGSize(width: 1, height: 1))
            p.addRoundedRect(in: CGRect(x: 1.5, y: 9.5, width: 4, height: 4), cornerSize: CGSize(width: 1, height: 1))
            line(pt(8, 4.5), pt(14.5, 4.5)); line(pt(8, 11.5), pt(14.5, 11.5))
        case .triangleLeft:
            p.move(to: pt(6, 1)); p.addLine(to: pt(1, 5)); p.addLine(to: pt(6, 9)); p.closeSubpath()
        case .triangleRight:
            p.move(to: pt(1, 1)); p.addLine(to: pt(6, 5)); p.addLine(to: pt(1, 9)); p.closeSubpath()
        case .sync:
            p = SVGPath.parse("M21.5 2v6h-6M2.5 22v-6h6M2 11.5a10 10 0 0 1 18.8-4.3L21.5 8M22 12.5a10 10 0 0 1-18.8 4.2L2.5 16")
        case .sliders:
            line(pt(2, 5.5), pt(9, 5.5))
            line(pt(13, 5.5), pt(15, 5.5))
            dot(11, 5.5, 2)
            line(pt(2, 10.5), pt(3.5, 10.5))
            line(pt(7.5, 10.5), pt(15, 10.5))
            dot(5.5, 10.5, 2)
        case .chartBars:
            line(pt(4, 12.5), pt(4, 8.5))
            line(pt(8, 12.5), pt(8, 3.5))
            line(pt(12, 12.5), pt(12, 6))
        case .appsGrid:
            p = SVGPath.parse("M5 5h5v5H5zM14 5h5v5h-5zM5 14h5v5H5zM14 14h5v5h-5z")
        case .sparkle:
            p = SVGPath.parse("M12 3l2 7 7 2-7 2-2 7-2-7-7-2 7-2z")
        case .socialMedia:
            p = SVGPath.parse("M15.5 2H3.5A2.5 2.5 0 0 0 1 4.5V15l3 4.5 3.5-4.5h7.5a2.5 2.5 0 0 0 2.5-2.5v-8A2.5 2.5 0 0 0 15.5 2zM18 7h3a2 2 0 0 1 2 2v7.5l-3 5-3.5-4.5H12")
        case .browsing:
            p = SVGPath.parse("M12 1a11 11 0 1 0 11 11A11 11 0 0 0 12 1zm4.2 6.4l-1.6 5.6-7.2 3.2 1.6-5.6z")
        case .coding:
            p = SVGPath.parse("M7 4.5L0.8 11.2 7 18M17 4.5l6.2 6.7L17 18M14.2 2L9.8 20.5")
        case .productivity:
            p = SVGPath.parse("M6 1.5h12a4 4 0 0 1 4 4v13a4 4 0 0 1-4 4H6a4 4 0 0 1-4-4v-13a4 4 0 0 1 4-4zm.5 7l2 2 3.5-3.5M14 9h4M6.5 15.5l2 2 3.5-3.5M14 16h4")
        case .entertainment:
            p = SVGPath.parse("M1 9h22v10a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2zm2.8-6.8l18.4 3.6-.6 2.7H1.5zm3.7.8L6.2 8.5M14 4.5l-1 4M9.5 12.5l5.5 3-5.5 3z")
        }
        return p
    }
}

extension AppKind {
    public var icon: TSIcon {
        switch self {
        case .game: return .gamepad
        case .gacha: return .sparkle
        case .socialMedia: return .socialMedia
        case .browsing: return .browsing
        case .coding: return .coding
        case .productivity: return .productivity
        case .entertainment: return .entertainment
        case .other: return .appsGrid
        }
    }
}

public struct IconView: View {
    let icon: TSIcon
    let size: CGFloat
    let weight: CGFloat
    let color: Color

    public init(_ icon: TSIcon, size: CGFloat, weight: CGFloat = 1.4, color: Color) {
        self.icon = icon
        self.size = size
        self.weight = weight
        self.color = color
    }

    public var body: some View {
        Canvas { ctx, canvas in
            let k = canvas.width / icon.box
            let p = icon.path.applying(CGAffineTransform(scaleX: k, y: k))
            if icon.filled {
                ctx.fill(p, with: .color(color))
            } else {
                ctx.stroke(p, with: .color(color), style: StrokeStyle(lineWidth: weight * k, lineCap: .round, lineJoin: .round))
            }
        }
        .frame(width: size, height: size)
    }
}

// MARK: - Controls

public struct PeriodArrow: View {
    let left: Bool
    let enabled: Bool
    var box: CGFloat = 16
    var iconSize: CGFloat = 9
    let action: () -> Void

    public var body: some View {
        Button(action: action) {
            IconView(left ? .triangleLeft : .triangleRight, size: iconSize, color: enabled ? ThemeColors.textDim : ThemeColors.textDisabled)
                .frame(width: box, height: box)
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .disabled(!enabled)
    }
}

/// The boards' segmented pill: one raised option in a bordered capsule.
public struct Segmented<T: Hashable>: View {
    let options: [(T, String)]
    @Binding var selection: T
    var hPad: CGFloat = 8
    var vPad: CGFloat = 3
    var radius: CGFloat = 8
    var fill: Color = .clear
    var border: Color = ThemeColors.border

    public var body: some View {
        HStack(spacing: 2) {
            ForEach(options, id: \.0) { value, label in
                Button(action: { selection = value }) {
                    Text(label)
                        .font(.system(size: 11, weight: selection == value ? .semibold : .regular))
                        .foregroundColor(selection == value ? ThemeColors.textPrimary : ThemeColors.textMuted)
                        .fixedSize()
                        .padding(.horizontal, hPad)
                        .padding(.vertical, vPad)
                        .background(RoundedRectangle(cornerRadius: radius - 2).fill(selection == value ? ThemeColors.surfaceRaised : .clear))
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
            }
        }
        .padding(2)
        .background(RoundedRectangle(cornerRadius: radius).fill(fill))
        .overlay(RoundedRectangle(cornerRadius: radius).strokeBorder(border, lineWidth: 1))
    }
}

public struct TSSwitch: View {
    @Binding var isOn: Bool

    public var body: some View {
        Button(action: { isOn.toggle() }) {
            ZStack(alignment: isOn ? .trailing : .leading) {
                Capsule().fill(isOn ? ThemeColors.accentDark : ThemeColors.surfaceRaised)
                Circle().fill(ThemeColors.textPrimary).frame(width: 16, height: 16).padding(2)
            }
            .frame(width: 34, height: 20)
        }
        .buttonStyle(.plain)
    }
}

@MainActor
private enum AppTileImageCache {
    static let cache = NSCache<NSData, NSImage>()
}

/// App tile: the real icon when there is one, otherwise a coloured letter.
public struct AppTile: View {
    let name: String
    let png: Data?
    var size: CGFloat = 30
    var radius: CGFloat = 8
    var fontSize: CGFloat = 14
    var fill: Color = ThemeColors.surface
    var border: Color = ThemeColors.borderSubtle

    private var iconImage: NSImage? {
        guard let png = png else { return nil }
        if let cached = AppTileImageCache.cache.object(forKey: png as NSData) {
            return cached
        }
        if let img = NSImage(data: png) {
            AppTileImageCache.cache.setObject(img, forKey: png as NSData)
            return img
        }
        return nil
    }

    public var body: some View {
        ZStack {
            if let img = iconImage {
                Image(nsImage: img).resizable().scaledToFit()
            } else {
                RoundedRectangle(cornerRadius: radius).fill(fill)
                RoundedRectangle(cornerRadius: radius).strokeBorder(border, lineWidth: 1)
                Text(String(name.prefix(1)).uppercased())
                    .font(.system(size: fontSize, weight: .bold))
                    .foregroundColor(Tokens.appColor(name))
            }
        }
        .frame(width: size, height: size)
    }
}

public struct Hatch: View {
    var color: Color
    var line: CGFloat = 2
    var gap: CGFloat = 2

    public var body: some View {
        Canvas { ctx, size in
            let step = (line + gap) * 1.414
            var x = -size.height
            var p = Path()
            while x < size.width {
                p.move(to: CGPoint(x: x, y: size.height))
                p.addLine(to: CGPoint(x: x + size.height, y: 0))
                x += step
            }
            ctx.stroke(p, with: .color(color), lineWidth: line)
        }
    }
}

public extension View {
    func card(fill: Color = ThemeColors.surface, border: Color = ThemeColors.borderSubtle, radius: CGFloat = 10) -> some View {
        background(RoundedRectangle(cornerRadius: radius).fill(fill))
            .overlay(RoundedRectangle(cornerRadius: radius).strokeBorder(border, lineWidth: 1))
    }

    func dashedCard(color: Color, radius: CGFloat) -> some View {
        overlay(RoundedRectangle(cornerRadius: radius).strokeBorder(color, style: StrokeStyle(lineWidth: 1, dash: [4, 3])))
    }

    func topRule(_ color: Color = ThemeColors.borderSubtle) -> some View {
        overlay(Rectangle().fill(color).frame(height: 1), alignment: .top)
    }

    func bottomRule(_ color: Color = ThemeColors.borderSubtle) -> some View {
        overlay(Rectangle().fill(color).frame(height: 1), alignment: .bottom)
    }

    /// Lets one view be tracked at the board's tracking value.
    func caps(_ size: CGFloat, tracking: CGFloat = 0.6, weight: Font.Weight = .regular, color: Color = ThemeColors.textMuted) -> some View {
        font(.system(size: size, weight: weight)).tracking(tracking).foregroundColor(color)
    }
}

/// "● Mac 28h 42m  ■ PC 20h 36m  10h 58m on both"
public struct DeviceLegend: View {
    let mac: String
    let pc: String
    let both: String

    public var body: some View {
        HStack(spacing: 12) {
            HStack(spacing: 5) { DeviceMarker(isLocal: true, size: 7); Text(mac) }
            HStack(spacing: 5) {
                RoundedRectangle(cornerRadius: 2).fill(ThemeColors.deviceB).frame(width: 7, height: 7)
                Text(pc)
            }
            Text(both).foregroundColor(ThemeColors.textMuted)
        }
        .font(.system(size: 11))
        .foregroundColor(ThemeColors.textDim)
        .lineLimit(1)
    }
}

/// Mac and PC bars drawn the way the boards overlap them: two thirds wide, shorter one in front.
public struct OverlapBar: View {
    let mac: CGFloat
    let pc: CGFloat
    let macColor: Color
    let pcColor: Color
    var macSleep: CGFloat = 0
    var pcSleep: CGFloat = 0
    var radius: CGFloat = 3

    public init(mac: CGFloat, pc: CGFloat, macColor: Color, pcColor: Color, macSleep: CGFloat = 0, pcSleep: CGFloat = 0, radius: CGFloat = 3) {
        self.mac = mac
        self.pc = pc
        self.macColor = macColor
        self.pcColor = pcColor
        self.macSleep = macSleep
        self.pcSleep = pcSleep
        self.radius = radius
    }

    public var body: some View {
        GeometryReader { geo in
            let totalMac = mac + macSleep
            let totalPc = pc + pcSleep
            let both = totalMac > 0 && totalPc > 0
            let w = geo.size.width * (both ? 0.66 : 1)

            let macBar = VStack(spacing: 0) {
                if macSleep > 0 {
                    Hatch(color: macColor)
                        .frame(width: w, height: macSleep)
                        .clipShape(UnevenRoundedRectangle(topLeadingRadius: radius, topTrailingRadius: radius))
                }
                if mac > 0 {
                    UnevenRoundedRectangle(
                        topLeadingRadius: macSleep > 0 ? 0 : radius,
                        topTrailingRadius: macSleep > 0 ? 0 : radius
                    )
                    .fill(macColor)
                    .frame(width: w, height: mac)
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .bottomLeading)

            let pcBar = VStack(spacing: 0) {
                if pcSleep > 0 {
                    Hatch(color: pcColor)
                        .frame(width: w, height: pcSleep)
                        .clipShape(UnevenRoundedRectangle(topLeadingRadius: radius, topTrailingRadius: radius))
                }
                if pc > 0 {
                    UnevenRoundedRectangle(
                        topLeadingRadius: pcSleep > 0 ? 0 : radius,
                        topTrailingRadius: pcSleep > 0 ? 0 : radius
                    )
                    .fill(pcColor)
                    .frame(width: w, height: pc)
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .bottomTrailing)

            ZStack {
                if both {
                    if totalMac <= totalPc { pcBar; macBar } else { macBar; pcBar }
                } else if totalMac > 0 {
                    macBar
                } else if totalPc > 0 {
                    pcBar
                }
            }
        }
    }
}

/// Ranked app list shown under the Month and Year Pace cards.
struct RankedAppList: View {
    @ObservedObject var state: AppState
    let rows: [(appId: Int64, name: String, seconds: Int64)]
    var range: AppDetailRange = .month
    var anchor: String? = nil

    var body: some View {
        let top = max(1, rows.first?.seconds ?? 1)
        VStack(spacing: 0) {
            ForEach(rows, id: \.appId) { row in
                RankedAppRow(state: state, row: row, top: top, range: range, anchor: anchor)
            }
        }
    }
}

private struct RankedAppRow: View {
    @ObservedObject var state: AppState
    let row: (appId: Int64, name: String, seconds: Int64)
    let top: Int64
    let range: AppDetailRange
    let anchor: String?
    @State private var isHovered = false

    var body: some View {
        HStack(spacing: 10) {
            AppTile(name: row.name, png: state.iconCache.getCachedIcon(forAppId: row.appId))
            VStack(alignment: .leading, spacing: 5) {
                Text(row.name)
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundColor(ThemeColors.textPrimary)
                    .lineLimit(1)
                GeometryReader { geo in
                    Capsule().fill(ThemeColors.barBackground)
                        .overlay(alignment: .leading) {
                            Capsule()
                                .fill(Tokens.appColor(row.name))
                                .frame(width: geo.size.width * CGFloat(row.seconds) / CGFloat(top))
                        }
                }
                .frame(height: 4)
            }
            Text(Formatters.paddedHM(row.seconds))
                .font(.system(size: 13, weight: .semibold))
                .foregroundColor(ThemeColors.textPrimary)
                .frame(width: 62, alignment: .trailing)
        }
        .padding(.horizontal, 6)
        .frame(height: 46)
        .background(RoundedRectangle(cornerRadius: 8).fill(isHovered ? ThemeColors.surfaceActive : Color.clear))
        .contentShape(Rectangle())
        .onHover { h in isHovered = h }
        .onTapGesture(count: 2) {
            state.openAppDetail(appId: row.appId, range: range, anchor: anchor)
        }
    }
}

/// Minimal SVG path-data parser (M L H V C A Z, absolute and relative) so icons can share the Windows glyph data.
enum SVGPath {
    static func parse(_ d: String) -> Path {
        var nums: [CGFloat] = []
        var tokens: [(Character, [CGFloat])] = []
        var cmd: Character = "M"
        var buf = ""
        func flush() {
            guard !buf.isEmpty else { return }
            if let v = Double(buf) { nums.append(CGFloat(v)) }
            buf = ""
        }
        func close() { flush(); tokens.append((cmd, nums)); nums = [] }
        for ch in d {
            if ch.isLetter {
                if cmd != "M" || !nums.isEmpty || !tokens.isEmpty { close() }
                cmd = ch
            } else if ch == "-" {
                if !buf.isEmpty && buf.last != "e" { flush() }
                buf.append(ch)
            } else if ch == "." {
                if buf.contains(".") { flush() }
                buf.append(ch)
            } else if ch == " " || ch == "," {
                flush()
            } else {
                buf.append(ch)
            }
        }
        close()

        var p = Path()
        var cur = CGPoint.zero
        var start = CGPoint.zero
        for (c, n) in tokens {
            let rel = c.isLowercase
            let arity: Int
            switch c.uppercased() {
            case "M", "L": arity = 2
            case "H", "V": arity = 1
            case "C": arity = 6
            case "A": arity = 7
            default: arity = 0
            }
            if arity == 0 { if c.uppercased() == "Z" { p.closeSubpath(); cur = start }; continue }
            var i = 0
            var first = true
            while i + arity <= n.count {
                let a = Array(n[i..<i + arity])
                i += arity
                let ox = rel ? cur.x : 0, oy = rel ? cur.y : 0
                switch c.uppercased() {
                case "M":
                    cur = CGPoint(x: a[0] + ox, y: a[1] + oy)
                    if first { p.move(to: cur); start = cur } else { p.addLine(to: cur) }
                case "L":
                    cur = CGPoint(x: a[0] + ox, y: a[1] + oy); p.addLine(to: cur)
                case "H":
                    cur = CGPoint(x: a[0] + ox, y: cur.y); p.addLine(to: cur)
                case "V":
                    cur = CGPoint(x: cur.x, y: a[0] + oy); p.addLine(to: cur)
                case "C":
                    let end = CGPoint(x: a[4] + ox, y: a[5] + oy)
                    p.addCurve(to: end, control1: CGPoint(x: a[0] + ox, y: a[1] + oy), control2: CGPoint(x: a[2] + ox, y: a[3] + oy))
                    cur = end
                default:
                    let end = CGPoint(x: a[5] + ox, y: a[6] + oy)
                    arc(&p, from: cur, rx: a[0], ry: a[1], large: a[3] != 0, sweep: a[4] != 0, to: end)
                    cur = end
                }
                first = false
            }
        }
        return p
    }

    private static func arc(_ p: inout Path, from a: CGPoint, rx rxIn: CGFloat, ry ryIn: CGFloat, large: Bool, sweep: Bool, to b: CGPoint) {
        var rx = abs(rxIn), ry = abs(ryIn)
        guard rx > 0, ry > 0, a != b else { p.addLine(to: b); return }
        let dx = (a.x - b.x) / 2, dy = (a.y - b.y) / 2
        let lam = (dx * dx) / (rx * rx) + (dy * dy) / (ry * ry)
        if lam > 1 { rx *= sqrt(lam); ry *= sqrt(lam) }
        let sign: CGFloat = large == sweep ? -1 : 1
        let num = rx * rx * ry * ry - rx * rx * dy * dy - ry * ry * dx * dx
        let den = rx * rx * dy * dy + ry * ry * dx * dx
        let co = sign * sqrt(max(0, num / den))
        let cxp = co * rx * dy / ry, cyp = -co * ry * dx / rx
        let c = CGPoint(x: cxp + (a.x + b.x) / 2, y: cyp + (a.y + b.y) / 2)
        let t1 = atan2((dy - cyp) / ry, (dx - cxp) / rx)
        var dt = atan2((-dy - cyp) / ry, (-dx - cxp) / rx) - t1
        if sweep && dt < 0 { dt += 2 * .pi }
        if !sweep && dt > 0 { dt -= 2 * .pi }
        let segs = max(1, Int(ceil(abs(dt) / (.pi / 2))))
        let step = dt / CGFloat(segs)
        let k = 4.0 / 3.0 * tan(step / 4)
        var t = t1
        for _ in 0..<segs {
            let c1 = CGPoint(x: cos(t), y: sin(t)), c2 = CGPoint(x: cos(t + step), y: sin(t + step))
            func m(_ q: CGPoint) -> CGPoint { CGPoint(x: c.x + rx * q.x, y: c.y + ry * q.y) }
            p.addCurve(
                to: m(c2),
                control1: m(CGPoint(x: c1.x - k * c1.y, y: c1.y + k * c1.x)),
                control2: m(CGPoint(x: c2.x + k * c2.y, y: c2.y - k * c2.x))
            )
            t += step
        }
    }
}
