import Foundation

public enum AllColumn: String, Sendable, CaseIterable, Identifiable {
    case hours = "hours"
    case app = "app"
    case avgSession = "avg session"
    case lastOpened = "last opened"

    public var id: String { rawValue }

    public var displayName: String { rawValue }
}

public struct AllSort: Sendable, Equatable {
    public var column: AllColumn
    public var descending: Bool

    public static let `default` = AllSort(column: .hours, descending: true)

    private static let cycle: [AllColumn] = [.hours, .app, .avgSession, .lastOpened]

    public init(column: AllColumn = .hours, descending: Bool = true) {
        self.column = column
        self.descending = descending
    }

    public mutating func next() {
        let idx = Self.cycle.firstIndex(of: column) ?? -1
        let nextCol = Self.cycle[(idx + 1) % Self.cycle.count]
        self.column = nextCol
        self.descending = (nextCol != .app)
    }

    public mutating func tap(_ col: AllColumn) {
        if column == col {
            descending.toggle()
        } else {
            column = col
            descending = (col != .app)
        }
    }

    public var arrow: String {
        descending ? "↓" : "↑"
    }
}

public enum AllSorting {
    public static func apply(rows: [AllAppRow], sort: AllSort) -> [AllAppRow] {
        if sort == .default {
            return rows.sorted { a, b in
                let aRank = a.seconds <= 0 ? 2 : (a.background ? 1 : 0)
                let bRank = b.seconds <= 0 ? 2 : (b.background ? 1 : 0)
                if aRank != bRank { return aRank < bRank }
                if a.seconds != b.seconds { return a.seconds > b.seconds }
                return a.name.localizedCaseInsensitiveCompare(b.name) == .orderedAscending
            }
        }

        return rows.sorted { a, b in
            let comp: ComparisonResult
            switch sort.column {
            case .hours:
                if a.seconds != b.seconds {
                    comp = a.seconds < b.seconds ? .orderedAscending : .orderedDescending
                } else {
                    comp = .orderedSame
                }
            case .app:
                comp = a.name.localizedCaseInsensitiveCompare(b.name)
            case .avgSession:
                if a.averageSessionSeconds != b.averageSessionSeconds {
                    comp = a.averageSessionSeconds < b.averageSessionSeconds ? .orderedAscending : .orderedDescending
                } else {
                    comp = .orderedSame
                }
            case .lastOpened:
                if a.lastUsedUtc != b.lastUsedUtc {
                    comp = a.lastUsedUtc < b.lastUsedUtc ? .orderedAscending : .orderedDescending
                } else {
                    comp = .orderedSame
                }
            }

            if comp != .orderedSame {
                return sort.descending ? (comp == .orderedDescending) : (comp == .orderedAscending)
            }
            return a.name.localizedCaseInsensitiveCompare(b.name) == .orderedAscending
        }
    }
}

