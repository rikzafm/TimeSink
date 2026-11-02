import Foundation

public enum AppKind: Int64, Sendable, Codable {
    case other = 0
    case game = 1
    case gacha = 2
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

    public static func next(_ kind: AppKind) -> AppKind {
        switch kind {
        case .other: return .game
        case .game: return .gacha
        case .gacha: return .other
        }
    }
}

