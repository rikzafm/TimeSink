import Foundation

/// Fuzzy matching for the app search boxes: every word of the query must hit somewhere,
/// as a substring, a typo-tolerant word match, initials, or an in-order subsequence.
public enum FuzzySearch {
    /// Match score (higher is better), or nil when the query does not match. Names outrank the publisher; a blank query matches everything with 0.
    public static func score(query: String?, names: [String], publisher: String? = nil) -> Int? {
        let foldedQuery = fold(query ?? "")
        let tokens = foldedQuery
            .split(separator: " ", omittingEmptySubsequences: true)
            .map(String.init)
        if tokens.isEmpty { return 0 }

        let nameFields = names.map { fold($0) }
        let pub: String?
        if let publisher = publisher, !publisher.isEmpty {
            pub = fold(publisher)
        } else {
            pub = nil
        }

        var total = 0
        for t in tokens {
            var best = -1
            for n in nameFields {
                best = max(best, tokenScore(token: t, field: n))
            }
            if let pub = pub {
                let p = tokenScore(token: t, field: pub)
                if p >= 0 {
                    best = max(best, p / 2)
                }
            }
            if best < 0 { return nil }
            total += best
        }
        return total
    }

    public static func score(query: String?, name: String, publisher: String? = nil) -> Int? {
        score(query: query, names: [name], publisher: publisher)
    }

    private static func tokenScore(token: String, field: String) -> Int {
        if let range = field.range(of: token) {
            let at = field.distance(from: field.startIndex, to: range.lowerBound)
            let wordStart: Bool
            if at == 0 {
                wordStart = true
            } else {
                let prevIdx = field.index(field.startIndex, offsetBy: at - 1)
                wordStart = !isLetterOrDigit(field[prevIdx])
            }
            return (at == 0 ? 1000 : wordStart ? 900 : 700) - min(field.count, 99)
        }

        var best = -1
        let tokenCount = token.count
        if tokenCount >= 4 {
            let allowed = tokenCount >= 8 ? 2 : 1
            let words = field.split(separator: " ", omittingEmptySubsequences: true)
            for wordSub in words {
                let word = String(wordSub)
                let prefixLen = min(word.count, tokenCount)
                let prefix = String(word.prefix(prefixLen))
                let d = min(distance(token, word), distance(token, prefix))
                if d <= allowed {
                    best = max(best, 500 - d * 50)
                }
            }
        }

        let tokenChars = Array(token)
        if tokenCount >= 2 && best < 0 && isInitials(token: tokenChars, field: field) {
            best = 400
        }

        if tokenCount >= 2 && best < 0 {
            let fieldChars = Array(field)
            let span = subsequenceSpan(token: tokenChars, field: fieldChars)
            if span > 0 && span <= tokenCount * 3 {
                best = 300 - (span - tokenCount)
            }
        }

        return best
    }

    /// True when the token's letters are the first letters of successive words of the field.
    private static func isInitials(token: [Character], field: String) -> Bool {
        var i = 0
        let words = field.split(separator: " ", omittingEmptySubsequences: true)
        for word in words {
            guard let first = word.first else { continue }
            if i < token.count && first == token[i] {
                i += 1
            }
        }
        return i == token.count
    }

    /// Length of the shortest window that holds the token's letters in order (greedy from each start), or 0 when there is none.
    private static func subsequenceSpan(token: [Character], field: [Character]) -> Int {
        guard let first = token.first else { return 0 }
        var best = 0
        var start = 0
        while start < field.count {
            guard let foundStart = field[start...].firstIndex(of: first) else { break }
            var pos = foundStart
            var ok = true
            for i in 1..<token.count {
                let nextTarget = token[i]
                let searchStart = pos + 1
                if searchStart >= field.count {
                    ok = false
                    break
                }
                guard let foundNext = field[searchStart...].firstIndex(of: nextTarget) else {
                    ok = false
                    break
                }
                pos = foundNext
            }
            if !ok {
                break // later starts have less text left, so they cannot match either
            }
            let span = pos - foundStart + 1
            if best == 0 || span < best {
                best = span
            }
            start = foundStart + 1
        }
        return best
    }

    /// Damerau-Levenshtein distance (adjacent swaps count as one edit).
    private static func distance(_ aStr: String, _ bStr: String) -> Int {
        let a = Array(aStr)
        let b = Array(bStr)
        let n = a.count
        let m = b.count
        var d = Array(repeating: Array(repeating: 0, count: m + 1), count: n + 1)
        for i in 0...n { d[i][0] = i }
        for j in 0...m { d[0][j] = j }
        if n == 0 || m == 0 { return max(n, m) }
        for i in 1...n {
            for j in 1...m {
                let cost = a[i - 1] == b[j - 1] ? 0 : 1
                d[i][j] = min(min(d[i - 1][j] + 1, d[i][j - 1] + 1), d[i - 1][j - 1] + cost)
                if i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1] {
                    d[i][j] = min(d[i][j], d[i - 2][j - 2] + 1)
                }
            }
        }
        return d[n][m]
    }

    private static func isLetterOrDigit(_ ch: Character) -> Bool {
        guard let scalar = ch.unicodeScalars.first, ch.unicodeScalars.count == 1 else {
            return false
        }
        switch scalar.properties.generalCategory {
        case .uppercaseLetter, .lowercaseLetter, .titlecaseLetter, .modifierLetter, .otherLetter, .decimalNumber:
            return true
        default:
            return false
        }
    }

    /// Lower-case with diacritics removed and punctuation turned into spaces.
    public static func fold(_ s: String) -> String {
        let decomposed = s.decomposedStringWithCanonicalMapping
        var scalars: [Unicode.Scalar] = []
        scalars.reserveCapacity(decomposed.unicodeScalars.count)
        for scalar in decomposed.unicodeScalars {
            if scalar.properties.generalCategory == .nonspacingMark {
                continue
            }
            switch scalar.properties.generalCategory {
            case .uppercaseLetter, .lowercaseLetter, .titlecaseLetter, .modifierLetter, .otherLetter, .decimalNumber:
                let lowerStr = String(scalar).lowercased()
                for c in lowerStr.unicodeScalars {
                    scalars.append(c)
                }
            default:
                scalars.append(" ")
            }
        }
        let folded = String(String.UnicodeScalarView(scalars))
        return folded
            .split(separator: " ", omittingEmptySubsequences: true)
            .joined(separator: " ")
    }
}

