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
        return "\(days) day\(days == 1 ? "" : "s") ago"
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
}

// MARK: - Icons (the boards' SVG paths)

public enum TSIcon {
    case gamepad, gear, gearLarge, eye, eyeSlash, lashes, layers, back, close, search, searchSmall
    case folder, upload, clock, history, tableOptions, triangleLeft, triangleRight

    var box: CGFloat {
        switch self {
        case .gamepad, .eye, .eyeSlash, .layers: return 18
        case .gearLarge, .lashes: return 20
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
            p.addRoundedRect(in: CGRect(x: 2, y: 5.5, width: 14, height: 7.5), cornerSize: CGSize(width: 3.5, height: 3.5))
            line(pt(6, 7.5), pt(6, 11)); line(pt(4.25, 9.25), pt(7.75, 9.25))
            dot(11.5, 8.5, 0.6); dot(13, 10, 0.6)
        case .gear:
            dot(8, 8, 2.2)
            line(pt(8, 1.5), pt(8, 3.5)); line(pt(8, 12.5), pt(8, 14.5)); line(pt(1.5, 8), pt(3.5, 8)); line(pt(12.5, 8), pt(14.5, 8))
            line(pt(3.4, 3.4), pt(4.8, 4.8)); line(pt(11.2, 11.2), pt(12.6, 12.6)); line(pt(3.4, 12.6), pt(4.8, 11.2)); line(pt(11.2, 4.8), pt(12.6, 3.4))
        case .gearLarge:
            dot(10, 10, 2.6)
            line(pt(10, 2), pt(10, 4.2)); line(pt(10, 15.8), pt(10, 18)); line(pt(2, 10), pt(4.2, 10)); line(pt(15.8, 10), pt(18, 10))
            line(pt(4.3, 4.3), pt(5.9, 5.9)); line(pt(14.1, 14.1), pt(15.7, 15.7)); line(pt(4.3, 15.7), pt(5.9, 14.1)); line(pt(14.1, 5.9), pt(15.7, 4.3))
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
        }
        return p
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

/// App tile: the real icon when there is one, otherwise a coloured letter.
public struct AppTile: View {
    let name: String
    let png: Data?
    var size: CGFloat = 30
    var radius: CGFloat = 8
    var fontSize: CGFloat = 14
    var fill: Color = ThemeColors.surface
    var border: Color = ThemeColors.borderSubtle

    public var body: some View {
        ZStack {
            RoundedRectangle(cornerRadius: radius).fill(fill)
            RoundedRectangle(cornerRadius: radius).strokeBorder(border, lineWidth: 1)
            if let png, let img = NSImage(data: png) {
                Image(nsImage: img).resizable().padding(size * 0.12)
            } else {
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
    var radius: CGFloat = 3

    public var body: some View {
        GeometryReader { geo in
            let both = mac > 0 && pc > 0
            let w = geo.size.width * (both ? 0.66 : 1)
            let macBar = UnevenRoundedRectangle(topLeadingRadius: radius, topTrailingRadius: radius).fill(macColor)
                .frame(width: w, height: mac)
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .bottomLeading)
            let pcBar = UnevenRoundedRectangle(topLeadingRadius: radius, topTrailingRadius: radius).fill(pcColor)
                .frame(width: w, height: pc)
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .bottomTrailing)
            ZStack {
                if mac <= pc { pcBar; macBar } else { macBar; pcBar }
            }
        }
    }
}
