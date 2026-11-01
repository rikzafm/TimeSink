import XCTest
@testable import TimeSinkCore

final class FuzzySearchTests: XCTestCase {
    func testMatchesSubsequencesTyposAndWordOrder() {
        XCTAssertNotNil(FuzzySearch.score(query: "vsc", name: "Visual Studio Code"))
        XCTAssertNotNil(FuzzySearch.score(query: "chorme", name: "Chrome"))
        XCTAssertNotNil(FuzzySearch.score(query: "photoshp", name: "Adobe Photoshop"))
        XCTAssertNotNil(FuzzySearch.score(query: "studio visual", name: "Visual Studio Code"))
    }

    func testRejectsUnrelatedNames() {
        XCTAssertNil(FuzzySearch.score(query: "zzz", name: "Chrome"))
        XCTAssertNil(FuzzySearch.score(query: "firefox", name: "Chrome"))
    }

    func testNameMatchOutranksPublisherMatchAndExactOutranksFuzzy() throws {
        let nameMatchScore = try XCTUnwrap(FuzzySearch.score(query: "adobe", name: "Adobe Reader", publisher: nil))
        let pubMatchScore = try XCTUnwrap(FuzzySearch.score(query: "adobe", name: "Photoshop", publisher: "Adobe"))
        XCTAssertGreaterThan(nameMatchScore, pubMatchScore)

        let exactScore = try XCTUnwrap(FuzzySearch.score(query: "chrome", name: "Chrome"))
        let typoScore = try XCTUnwrap(FuzzySearch.score(query: "chorme", name: "Chrome"))
        XCTAssertGreaterThan(exactScore, typoScore)
    }

    func testPublisherIsSearched() {
        XCTAssertNotNil(FuzzySearch.score(query: "serif", name: "Affinity Photo", publisher: "Serif"))
    }

    func testBlankOrWhitespaceQueryReturnsZero() {
        XCTAssertEqual(FuzzySearch.score(query: "", name: "Chrome"), 0)
        XCTAssertEqual(FuzzySearch.score(query: "   ", name: "Chrome"), 0)
        XCTAssertEqual(FuzzySearch.score(query: nil, name: "Chrome"), 0)
    }

    func testDiacriticsAndNormalization() {
        XCTAssertNotNil(FuzzySearch.score(query: "abaco", name: "ábaco"))
        XCTAssertNotNil(FuzzySearch.score(query: "ÁBACO", name: "abaco"))
    }

    // MARK: - TrackedSearch Tests

    func testTrackedSearchWithBlankQueryKeepsListOrder() {
        let app1 = TrackedAppItem(
            id: 1, canonicalId: nil, exePath: "/Zed", displayName: "Zed", category: "Dev",
            state: "tracked", addedUtc: 100, isBackground: false, kind: .other, deviceId: "mac",
            mergedCount: 0, totalSeconds: 10
        )
        let app2 = TrackedAppItem(
            id: 2, canonicalId: nil, exePath: "/Alpha", displayName: "Alpha", category: "Dev",
            state: "tracked", addedUtc: 100, isBackground: false, kind: .other, deviceId: "mac",
            mergedCount: 0, totalSeconds: 10
        )

        let result1 = TrackedSearch.apply([app1, app2], query: "  ", publisherOf: { _ in nil })
        XCTAssertEqual(result1.map(\.displayName), ["Zed", "Alpha"])

        let result2 = TrackedSearch.apply([app1, app2], query: nil, publisherOf: { _ in nil })
        XCTAssertEqual(result2.map(\.displayName), ["Zed", "Alpha"])
    }

    func testTrackedSearchMatchesMergedNamesAndPublisher() {
        let app1 = TrackedAppItem(
            id: 1, canonicalId: nil, exePath: "/Browser", displayName: "Browser", category: "Web",
            state: "tracked", addedUtc: 100, isBackground: false, kind: .other, deviceId: "mac",
            mergedCount: 1, totalSeconds: 10, mergedNames: ["Chromium"]
        )
        let app2 = TrackedAppItem(
            id: 2, canonicalId: nil, exePath: "/Editor", displayName: "Editor", category: "Dev",
            state: "tracked", addedUtc: 100, isBackground: false, kind: .other, deviceId: "mac",
            mergedCount: 0, totalSeconds: 10
        )
        let app3 = TrackedAppItem(
            id: 3, canonicalId: nil, exePath: "/Mail", displayName: "Mail", category: "Email",
            state: "tracked", addedUtc: 100, isBackground: false, kind: .other, deviceId: "mac",
            mergedCount: 0, totalSeconds: 10
        )

        let resultMerged = TrackedSearch.apply([app1, app2, app3], query: "chromium", publisherOf: { _ in nil })
        XCTAssertEqual(resultMerged.map(\.displayName), ["Browser"])

        let resultPub = TrackedSearch.apply([app1, app2, app3], query: "acme", publisherOf: { path in
            path.contains("Editor") ? "Acme Corp" : nil
        })
        XCTAssertEqual(resultPub.map(\.displayName), ["Editor"])
    }

    func testTrackedSearchOrdersEqualMatchesByName() {
        let app1 = TrackedAppItem(
            id: 1, canonicalId: nil, exePath: "/ToolB", displayName: "Tool B", category: "Util",
            state: "tracked", addedUtc: 100, isBackground: false, kind: .other, deviceId: "mac",
            mergedCount: 0, totalSeconds: 10
        )
        let app2 = TrackedAppItem(
            id: 2, canonicalId: nil, exePath: "/ToolA", displayName: "Tool A", category: "Util",
            state: "tracked", addedUtc: 100, isBackground: false, kind: .other, deviceId: "mac",
            mergedCount: 0, totalSeconds: 10
        )

        let result = TrackedSearch.apply([app1, app2], query: "tool", publisherOf: { _ in nil })
        XCTAssertEqual(result.map(\.displayName), ["Tool A", "Tool B"])
    }

    // MARK: - InstalledSearch Tests

    func testInstalledSearchMatchesNameOrPublisherAndBlankReturnsAll() {
        let app1 = InstalledAppItem(name: "Affinity Photo", bundleUrl: URL(fileURLWithPath: "/Affinity"), executablePath: "/Affinity", bundleId: nil, publisher: "Serif")
        let app2 = InstalledAppItem(name: "OBS Studio", bundleUrl: URL(fileURLWithPath: "/OBS"), executablePath: "/OBS", bundleId: nil, publisher: "OBS Project")
        let app3 = InstalledAppItem(name: "Obsidian", bundleUrl: URL(fileURLWithPath: "/Obsidian"), executablePath: "/Obsidian", bundleId: nil, publisher: "Dynalist")

        let all = [app1, app2, app3]

        XCTAssertEqual(InstalledSearch.search(all, query: "").count, 3)
        XCTAssertEqual(InstalledSearch.search(all, query: "serif").map(\.name), ["Affinity Photo"])
        XCTAssertTrue(InstalledSearch.search(all, query: "nothing like this").isEmpty)
    }

    // MARK: - MergePlan Fuzzy Search Tests

    func testMergePlanSearchIsFuzzyAndBestMatchComesFirst() {
        let c1 = MergeCandidate(key: "1", kind: .tracked, name: "Discord PTB", exePath: "/PTB", appId: 1, seconds: 10)
        let c2 = MergeCandidate(key: "2", kind: .tracked, name: "Discord", exePath: "/Discord", appId: 2, seconds: 20)
        let c3 = MergeCandidate(key: "3", kind: .tracked, name: "Notepad", exePath: "/Notepad", appId: 3, seconds: 30)

        let plan = MergePlan(mainId: 99, mainName: "Main", candidates: [c1, c2, c3])
        plan.filter = .all
        plan.query = "discrod"

        let shown = plan.shown()
        XCTAssertEqual(shown.map(\.name), ["Discord", "Discord PTB"])
    }

    func testMergePlanInstalledCandidateMatchesPublisher() {
        let c1 = MergeCandidate(key: "1", kind: .installed, name: "Photoshop", exePath: "/Photoshop", appId: 1, seconds: 0, publisher: "Adobe")
        let c2 = MergeCandidate(key: "2", kind: .installed, name: "Paint", exePath: "/Paint", appId: 2, seconds: 0, publisher: "Microsoft")

        let plan = MergePlan(mainId: 99, mainName: "Main", candidates: [c1, c2])
        plan.filter = .untracked
        plan.query = "adobe"

        XCTAssertEqual(plan.shown().map(\.name), ["Photoshop"])
    }
}
