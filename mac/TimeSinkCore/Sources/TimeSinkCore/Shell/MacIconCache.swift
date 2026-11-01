import Foundation
import AppKit

/// Extracts application icons via NSWorkspace and caches them in memory and the SQLite `icons` table as PNG data.
public final class MacIconCache: @unchecked Sendable {
    private let db: SQLiteDatabase
    private let lock = NSLock()
    private var memoryCache: [String: Data] = [:]

    public init(db: SQLiteDatabase) {
        self.db = db
    }

    /// Extracts an app icon for the given file path, returns PNG data and caches it in memory.
    public func iconData(forFilePath path: String, targetSize: CGFloat = 64) -> Data? {
        lock.lock()
        if let cached = memoryCache[path] {
            lock.unlock()
            return cached
        }
        lock.unlock()

        guard FileManager.default.fileExists(atPath: path) else { return nil }
        let image = NSWorkspace.shared.icon(forFile: path)
        guard let png = image.pngData(size: NSSize(width: targetSize, height: targetSize)) else { return nil }

        lock.lock()
        memoryCache[path] = png
        lock.unlock()
        return png
    }

    public func getCachedIcon(forAppId appId: Int64) -> Data? {
        do {
            let stmt = try db.prepare("SELECT png FROM icons WHERE app_id = ?;")
            try stmt.bind(int64: appId, at: 1)
            if try stmt.step() {
                return stmt.columnBlob(at: 0)
            }
        } catch {}
        return nil
    }

    public func saveIcon(forAppId appId: Int64, hash: String, png: Data) {
        do {
            let stmt = try db.prepare("""
                INSERT INTO icons (app_id, png, hash)
                VALUES (?, ?, ?)
                ON CONFLICT(app_id) DO UPDATE SET png = excluded.png, hash = excluded.hash;
                """)
            try stmt.bind(int64: appId, at: 1)
            try stmt.bind(blob: png, at: 2)
            try stmt.bind(text: hash, at: 3)
            _ = try stmt.step()
        } catch {}
    }
}

extension NSImage {
    public func pngData(size: NSSize) -> Data? {
        let rep = NSBitmapImageRep(
            bitmapDataPlanes: nil,
            pixelsWide: Int(size.width),
            pixelsHigh: Int(size.height),
            bitsPerSample: 8,
            samplesPerPixel: 4,
            hasAlpha: true,
            isPlanar: false,
            colorSpaceName: .deviceRGB,
            bytesPerRow: 0,
            bitsPerPixel: 0
        )
        guard let rep else { return nil }
        rep.size = size

        NSGraphicsContext.saveGraphicsState()
        let context = NSGraphicsContext(bitmapImageRep: rep)
        NSGraphicsContext.current = context

        draw(in: NSRect(origin: .zero, size: size), from: NSRect(origin: .zero, size: self.size), operation: .copy, fraction: 1.0)

        context?.flushGraphics()
        NSGraphicsContext.restoreGraphicsState()

        return rep.representation(using: .png, properties: [:])
    }
}
