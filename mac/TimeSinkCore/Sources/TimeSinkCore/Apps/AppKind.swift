import Foundation

public enum AppKind: Int64, Sendable, Codable, CaseIterable {
    case other = 0
    case game = 1
    case gacha = 2
    case socialMedia = 3
    case browsing = 4
    case coding = 5
    case productivity = 6
    case entertainment = 7

    public var displayName: String {
        switch self {
        case .other: return "Other"
        case .game: return "Game"
        case .gacha: return "Gacha"
        case .socialMedia: return "Social Media"
        case .browsing: return "Browsing"
        case .coding: return "Coding"
        case .productivity: return "Productivity"
        case .entertainment: return "Entertainment"
        }
    }
}

public enum AppKinds {
    public static let gachaNames: [String] = [
        "genshin", "yuanshen", "starrail", "star rail", "zenless", "zzz", "wuthering", "wuwa", "arknights", "endfield",
        "bluearchive", "blue archive", "nikke", "fate/grand", "fategrand", "fgo", "punishing", "reverse1999", "reverse 1999",
        "honkai", "azur lane", "azurlane", "girls' frontline", "girlsfrontline", "counterside", "epic seven", "epicseven",
        "uma musume", "umamusume", "snowbreak", "limbus", "dislyte", "tower of fantasy", "neverness", "onmyoji",
        "genshin impact", "love and deepspace", "infinity nikki", "granblue", "princess connect", "pokemon masters",
    ]

    public static let steamFolders: [String] = [
        "/steamapps/common/", "\\steamapps\\common\\", "steamapps/common",
    ]

    public static func detect(exePath: String, displayName: String) -> AppKind {
        let file = ((exePath as NSString).lastPathComponent as NSString).deletingPathExtension
        let nameLower = displayName.lowercased()
        let fileLower = file.lowercased()
        for n in gachaNames {
            if nameLower.contains(n) || fileLower.contains(n) {
                return .gacha
            }
        }
        let pathLower = exePath.lowercased()
        for f in steamFolders {
            if pathLower.contains(f) {
                return .game
            }
        }
        return .other
    }

    public static func resolve(kind: Int64?, exePath: String, displayName: String) -> AppKind {
        if let k = kind, let resolved = AppKind(rawValue: k) {
            return resolved
        }
        return detect(exePath: exePath, displayName: displayName)
    }

    public static func isGame(_ kind: AppKind) -> Bool {
        kind == .game || kind == .gacha
    }

    public static func next(_ kind: AppKind) -> AppKind {
        switch kind {
        case .other: return .game
        case .game: return .gacha
        case .gacha: return .socialMedia
        case .socialMedia: return .browsing
        case .browsing: return .coding
        case .coding: return .productivity
        case .productivity: return .entertainment
        case .entertainment: return .other
        }
    }
}

