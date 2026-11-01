import Foundation

public enum CandidateKind: String, Sendable {
    case tracked
    case merged
    case retired
    case installed
}

public struct MergeCandidate: Sendable, Equatable {
    public let key: String
    public let kind: CandidateKind
    public let name: String
    public let exePath: String
    public let appId: Int64
    public let seconds: Int64
    public let bundleId: String?
    public let png: Data?
    public let deviceId: String
    public let publisher: String?

    public var isTracked: Bool { kind == .tracked || kind == .merged }

    public init(
        key: String,
        kind: CandidateKind,
        name: String,
        exePath: String,
        appId: Int64,
        seconds: Int64,
        bundleId: String? = nil,
        png: Data? = nil,
        deviceId: String = "",
        publisher: String? = nil
    ) {
        self.key = key
        self.kind = kind
        self.name = name
        self.exePath = exePath
        self.appId = appId
        self.seconds = seconds
        self.bundleId = bundleId
        self.png = png
        self.deviceId = deviceId
        self.publisher = publisher
    }
}

public enum MergeFilter: String, Sendable {
    case tracked
    case untracked
    case all
}

public struct MergeSummary: Sendable, Equatable {
    public let adds: Int
    public let removes: Int
    public let startsTracking: Bool

    public var isChanged: Bool { adds + removes > 0 }

    public init(adds: Int, removes: Int, startsTracking: Bool) {
        self.adds = adds
        self.removes = removes
        self.startsTracking = startsTracking
    }
}

public final class MergePlan: @unchecked Sendable {
    public let mainId: Int64
    public let mainName: String
    public let mainDeviceId: String
    public let candidates: [MergeCandidate]
    private var ticked: Set<String> = []

    public var filter: MergeFilter = .tracked
    public var deviceFilter: String? = nil
    public var query: String = ""

    public init(
        mainId: Int64,
        mainName: String,
        candidates: [MergeCandidate],
        mainDeviceId: String = ""
    ) {
        self.mainId = mainId
        self.mainName = mainName
        self.mainDeviceId = mainDeviceId
        self.candidates = candidates.sorted { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }
        for c in self.candidates where c.kind == .merged {
            self.ticked.insert(c.key)
        }
    }

    public var devices: [String] {
        var set = Set(candidates.map(\.deviceId).filter { !$0.isEmpty })
        if !mainDeviceId.isEmpty { set.insert(mainDeviceId) }
        return Array(set).sorted()
    }

    public var suggestedMatches: [MergeCandidate] {
        candidates.filter {
            $0.kind != .merged
            && !$0.deviceId.isEmpty
            && $0.deviceId.caseInsensitiveCompare(mainDeviceId) != .orderedSame
            && $0.name.trimmingCharacters(in: .whitespacesAndNewlines).caseInsensitiveCompare(mainName.trimmingCharacters(in: .whitespacesAndNewlines)) == .orderedSame
        }
    }

    public func isTicked(_ key: String) -> Bool {
        ticked.contains(key)
    }

    public func toggle(_ key: String) {
        if ticked.contains(key) { ticked.remove(key) }
        else { ticked.insert(key) }
    }

    public func tickedCountForDevice(_ deviceId: String) -> Int {
        candidates.filter { $0.deviceId.caseInsensitiveCompare(deviceId) == .orderedSame && isTicked($0.key) }.count
    }

    public func countOf(_ filter: MergeFilter) -> Int {
        candidates.filter { matches(filter: filter, candidate: $0) && matchesDevice(candidate: $0) }.count
    }

    public func shown() -> [MergeCandidate] {
        let inFilter = candidates.filter { matches(filter: filter, candidate: $0) && matchesDevice(candidate: $0) }
        let q = query.trimmingCharacters(in: .whitespacesAndNewlines)
        if q.isEmpty { return inFilter }
        var scored: [(candidate: MergeCandidate, score: Int)] = []
        for c in inFilter {
            let pub = c.publisher ?? (c.exePath.isEmpty ? nil : AppVendor.name(executable: c.exePath))
            if let score = FuzzySearch.score(query: q, name: c.name, publisher: pub) {
                scored.append((c, score))
            }
        }
        return scored.enumerated()
            .sorted { a, b in
                if a.element.score != b.element.score {
                    return a.element.score > b.element.score
                }
                return a.offset < b.offset
            }
            .map(\.element.candidate)
    }

    private func matches(filter: MergeFilter, candidate: MergeCandidate) -> Bool {
        switch filter {
        case .tracked: return candidate.isTracked
        case .untracked: return !candidate.isTracked
        case .all: return true
        }
    }

    private func matchesDevice(candidate: MergeCandidate) -> Bool {
        guard let dev = deviceFilter, !dev.isEmpty else { return true }
        return candidate.deviceId.caseInsensitiveCompare(dev) == .orderedSame
    }
}

