import Foundation

/// The Installed tab's search box.
public enum InstalledSearch {
    /// Fuzzy match on name or publisher / byline, best match first; a blank query returns everything in list order.
    public static func search(
        _ apps: [InstalledAppItem],
        query: String?,
        publisherOf: (String) -> String? = { AppVendor.name(executable: $0) }
    ) -> [InstalledAppItem] {
        guard let query = query, !query.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            return apps
        }
        var scored: [(app: InstalledAppItem, score: Int)] = []
        for app in apps {
            let pub = app.publisher ?? publisherOf(app.executablePath)
            if let score = FuzzySearch.score(query: query, name: app.name, publisher: pub) {
                scored.append((app, score))
            }
        }
        return scored
            .sorted { (a, b) in
                if a.score != b.score {
                    return a.score > b.score
                }
                return a.app.name.localizedCaseInsensitiveCompare(b.app.name) == .orderedAscending
            }
            .map(\.app)
    }
}

