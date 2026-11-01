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

    public static func resolveBundlePath(from path: String) -> String? {
        guard !path.isEmpty else { return nil }
        var url = URL(fileURLWithPath: path)
        while url.path != "/" && url.pathComponents.count > 1 {
            if url.pathExtension == "app" {
                return url.path
            }
            url.deleteLastPathComponent()
        }
        return nil
    }

    /// Extracts an app icon for the given file path, returns PNG data and caches it in memory.
    public func iconData(forFilePath path: String, bundleId: String? = nil, targetSize: CGFloat = 64) -> Data? {
        let cacheKey = "\(path)|\(bundleId ?? "")|\(Int(targetSize))"
        lock.lock()
        if let cached = memoryCache[cacheKey] {
            lock.unlock()
            return cached
        }
        if let cached = memoryCache[path] {
            lock.unlock()
            return cached
        }
        lock.unlock()

        var iconImage: NSImage?

        // 1. If path is inside an .app bundle, get the bundle's icon
        if let bundlePath = Self.resolveBundlePath(from: path), FileManager.default.fileExists(atPath: bundlePath) {
            iconImage = NSWorkspace.shared.icon(forFile: bundlePath)
        }

        // 2. If no icon yet and bundleId is provided, try locating the app bundle by bundleId
        if iconImage == nil, let bundleId = bundleId, !bundleId.isEmpty {
            if let appUrl = NSWorkspace.shared.urlForApplication(withBundleIdentifier: bundleId) {
                iconImage = NSWorkspace.shared.icon(forFile: appUrl.path)
            }
        }

        // 3. Try finding a running application for this path or bundleId
        if iconImage == nil {
            if let running = NSWorkspace.shared.runningApplications.first(where: {
                (bundleId != nil && !bundleId!.isEmpty && $0.bundleIdentifier == bundleId) ||
                ($0.executableURL?.path == path)
            }) {
                iconImage = running.icon
            }
        }

        // 4. Fall back to icon for the exact file path if it exists
        if iconImage == nil && FileManager.default.fileExists(atPath: path) {
            iconImage = NSWorkspace.shared.icon(forFile: path)
        }

        guard let image = iconImage,
              let png = image.pngData(size: NSSize(width: targetSize, height: targetSize)) else { return nil }

        lock.lock()
        memoryCache[cacheKey] = png
        memoryCache[path] = png
        lock.unlock()
        return png
    }

    public func getCachedIcon(forAppId appId: Int64, fallbackExePath: String? = nil) -> Data? {
        do {
            let stmt = try db.prepare("SELECT png FROM icons WHERE app_id = ?;")
            try stmt.bind(int64: appId, at: 1)
            if try stmt.step(), let blob = stmt.columnBlob(at: 0) {
                return blob
            }
        } catch {}

        if let exePath = fallbackExePath, let png = iconData(forFilePath: exePath, targetSize: 64) {
            saveIcon(forAppId: appId, hash: "extracted:\(exePath)", png: png)
            return png
        }

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
