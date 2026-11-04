import Foundation

/// Best available publisher for an app, read from its bundle.
public enum AppVendor {
    private static let lock = NSLock()
    nonisolated(unsafe) private static var cache: [String: String] = [:]

    public static func name(executable: String) -> String {
        lock.lock()
        if let cached = cache[executable] {
            lock.unlock()
            return cached
        }
        lock.unlock()

        let result = extractName(executable: executable)

        lock.lock()
        cache[executable] = result
        lock.unlock()

        return result
    }

    private static func extractName(executable: String) -> String {
        guard let bundlePath = MacIconCache.resolveBundlePath(from: executable),
              let bundle = Bundle(path: bundlePath) else { return "" }
        if bundle.bundleIdentifier?.hasPrefix("com.apple.") == true { return "Apple" }
        guard let raw = bundle.object(forInfoDictionaryKey: "NSHumanReadableCopyright") as? String else { return "" }
        let cleaned = raw
            .replacingOccurrences(of: "(?i)copyright|©|\\(c\\)|all rights reserved\\.?|[0-9]{4}(\\s*[-–]\\s*[0-9]{4})?", with: "", options: .regularExpression)
            .trimmingCharacters(in: CharacterSet(charactersIn: " ,.\n\t"))
        return cleaned.count <= 28 ? cleaned : ""
    }
}

