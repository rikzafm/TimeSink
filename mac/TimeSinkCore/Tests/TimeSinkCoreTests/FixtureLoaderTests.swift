import XCTest
import Foundation
@testable import TimeSinkCore

final class FixtureLoaderTests: XCTestCase {
    static func findFixturesDirectory() -> URL? {
        var current = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
        for _ in 0..<10 {
            let candidate = current.appendingPathComponent("shared/fixtures")
            if FileManager.default.fileExists(atPath: candidate.path) {
                return candidate
            }
            current = current.deletingLastPathComponent()
        }
        return nil
    }

    func testLoadSharedFixturesFromDisk() throws {
        guard let fixturesDir = Self.findFixturesDirectory() else {
            XCTFail("shared/fixtures directory not found")
            return
        }

        let expectedFiles = [
            "single-session.json",
            "midnight-and-dst.json",
            "suspend-resume-and-power.json",
            "lock-and-unlock.json",
            "crash-recovery.json",
            "min-session-and-clock.json",
            "accumulator-rebuild.json",
            "backup-and-restore.json",
            "device-overlap.json",
            "sync-import.json"
        ]

        for file in expectedFiles {
            let fileUrl = fixturesDir.appendingPathComponent(file)
            XCTAssertTrue(FileManager.default.fileExists(atPath: fileUrl.path), "Missing fixture \(file)")

            let data = try Data(contentsOf: fileUrl)
            let json = try JSONSerialization.jsonObject(with: data) as? [String: Any]
            XCTAssertNotNil(json, "Invalid JSON in \(file)")
            XCTAssertNotNil(json?["name"], "Fixture missing name in \(file)")
        }
    }
}
