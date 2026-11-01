import Foundation

/// The Tracked tab's search box.
public enum TrackedSearch {
    /// Fuzzy match on an app's name, the names of the apps merged into it, and its publisher,
    /// best match first and equal matches by name; a blank query returns every app in list order.
    public static func apply(
        _ apps: [TrackedAppItem],
        query: String?,
        publisherOf: (String) -> String? = { AppVendor.name(executable: $0) }
    ) -> [TrackedAppItem] {
        guard let query = query, !query.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            return apps
        }
        var scored: [(app: TrackedAppItem, score: Int)] = []
        for app in apps {
            var names = [app.displayName]
            names.append(contentsOf: app.mergedNames)
            let pub = publisherOf(app.exePath)
            if let score = FuzzySearch.score(query: query, names: names, publisher: pub) {
                scored.append((app, score))
            }
        }
        return scored
            .sorted { (a, b) in
                if a.score != b.score {
                    return a.score > b.score
                }
                return a.app.displayName.localizedCaseInsensitiveCompare(b.app.displayName) == .orderedAscending
            }
            .map(\.app)
    }
}

