import Foundation

public enum AppKind: Int64, Sendable, Codable {
    case other = 0
    case game = 1
    case gacha = 2
}

public enum AppKinds {
    public static func resolve(kind: Int64?, exePath: String, displayName: String) -> AppKind {
        if let k = kind, let resolved = AppKind(rawValue: k) {
            return resolved
        }
        return .other
    }
}

