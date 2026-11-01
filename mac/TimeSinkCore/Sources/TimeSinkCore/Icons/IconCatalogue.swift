import Foundation
import AppKit

public enum IconStyle: String, Sendable, CaseIterable {
    case clean = "Clean"
    case comic = "Comic"
    case misprint = "Misprint"
}

public struct IconEntry: Sendable, Identifiable, Equatable {
    public let id: String
    public let style: IconStyle
    public let name: String

    public init(id: String, style: IconStyle, name: String) {
        self.id = id
        self.style = style
        self.name = name
    }
}

public enum IconCatalogue {
    public static let defaultId = "clean.clock"

    public static let all: [IconEntry] = [
        IconEntry(id: "clean.clock", style: .clean, name: "Clock"),
        IconEntry(id: "clean.bars", style: .clean, name: "Bars"),
        IconEntry(id: "clean.gauge", style: .clean, name: "Gauge"),
        IconEntry(id: "clean.funnel", style: .clean, name: "Funnel"),
        IconEntry(id: "clean.marker", style: .clean, name: "Marker"),
        IconEntry(id: "clean.ticks", style: .clean, name: "Ticks"),
        IconEntry(id: "clean.dashed", style: .clean, name: "Dashed"),
        IconEntry(id: "clean.layers", style: .clean, name: "Layers"),
        IconEntry(id: "clean.ripple", style: .clean, name: "Ripple"),

        IconEntry(id: "comic.ticks", style: .comic, name: "Ticks"),

        IconEntry(id: "misprint.ticks", style: .misprint, name: "Ticks"),
        IconEntry(id: "misprint.t", style: .misprint, name: "T"),
        IconEntry(id: "misprint.web", style: .misprint, name: "Web"),
        IconEntry(id: "misprint.cursor", style: .misprint, name: "Cursor"),
        IconEntry(id: "misprint.film", style: .misprint, name: "Film strip"),
        IconEntry(id: "misprint.rings", style: .misprint, name: "Rings"),
        IconEntry(id: "misprint.burst", style: .misprint, name: "Burst"),
    ]

    public static func inStyle(_ style: IconStyle) -> [IconEntry] {
        all.filter { $0.style == style }
    }

    public static func find(_ id: String) -> IconEntry? {
        all.first { $0.id == id }
    }

    public static func resolve(_ stored: String?) -> String {
        if let stored, all.contains(where: { $0.id == stored }) {
            return stored
        }
        return defaultId
    }

    public static func openIconData(id: String) -> Data? {
        if let url = Bundle.module.url(forResource: id, withExtension: "ico", subdirectory: "Resources/Icons") ??
                     Bundle.module.url(forResource: id, withExtension: "ico") {
            return try? Data(contentsOf: url)
        }
        return nil
    }

    public static func readFrameData(id: String, size: Int) -> Data? {
        guard let data = openIconData(id: id) else { return nil }
        guard data.count >= 6 else { return nil }
        let count = Int(data[4]) | (Int(data[5]) << 8)
        var best: (size: Int, length: Int, offset: Int)?
        for i in 0..<count {
            let e = 6 + i * 16
            guard e + 16 <= data.count else { break }
            let rawW = Int(data[e])
            let frameSize = rawW == 0 ? 256 : rawW
            let length = Int(data[e + 8]) | (Int(data[e + 9]) << 8) | (Int(data[e + 10]) << 16) | (Int(data[e + 11]) << 24)
            let offset = Int(data[e + 12]) | (Int(data[e + 13]) << 8) | (Int(data[e + 14]) << 16) | (Int(data[e + 15]) << 24)
            let frame = (size: frameSize, length: length, offset: offset)

            if let currentBest = best {
                if currentBest.size < size {
                    if frame.size > currentBest.size {
                        best = frame
                    }
                } else if frame.size >= size && frame.size < currentBest.size {
                    best = frame
                }
            } else {
                best = frame
            }
        }
        guard let chosen = best, chosen.offset + chosen.length <= data.count else { return nil }
        return data.subdata(in: chosen.offset..<(chosen.offset + chosen.length))
    }

    public static func image(for id: String, size: Int, accentColor: NSColor = NSColor.systemGreen) -> NSImage? {
        if id == "clean.clock" {
            return drawClockImage(size: CGFloat(size), accentColor: accentColor)
        }
        guard let pngData = readFrameData(id: id, size: size) else { return nil }
        guard let img = NSImage(data: pngData) else { return nil }
        img.size = NSSize(width: size, height: size)
        img.isTemplate = false
        return img
    }

    public static func drawClockImage(size: CGFloat, accentColor: NSColor) -> NSImage {
        let img = NSImage(size: NSSize(width: size, height: size), flipped: true) { _ in
            let scale = size / 12.0
            accentColor.setStroke()
            let ring = NSBezierPath(ovalIn: NSRect(x: 1.5 * scale, y: 1.5 * scale, width: 9 * scale, height: 9 * scale))
            ring.lineWidth = 1.6 * scale
            ring.stroke()

            let hand = NSBezierPath()
            hand.move(to: NSPoint(x: 6 * scale, y: 3.5 * scale))
            hand.line(to: NSPoint(x: 6 * scale, y: 6 * scale))
            hand.line(to: NSPoint(x: 7.8 * scale, y: 7 * scale))
            hand.lineWidth = 1.4 * scale
            hand.lineCapStyle = .round
            hand.stroke()
            return true
        }
        img.isTemplate = false
        return img
    }
}

