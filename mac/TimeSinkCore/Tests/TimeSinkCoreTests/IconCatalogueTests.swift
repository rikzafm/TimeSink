import XCTest
import AppKit
@testable import TimeSinkCore

final class IconCatalogueTests: XCTestCase {
    func testDefaultIconIsCleanClock() {
        XCTAssertEqual(IconCatalogue.defaultId, "clean.clock")
        let entry = IconCatalogue.find(IconCatalogue.defaultId)
        XCTAssertNotNil(entry)
        XCTAssertEqual(entry?.name, "Clock")
        XCTAssertEqual(entry?.style, .clean)
    }

    func testResolveUnknownStoredIdReturnsDefault() {
        XCTAssertEqual(IconCatalogue.resolve(nil), "clean.clock")
        XCTAssertEqual(IconCatalogue.resolve(""), "clean.clock")
        XCTAssertEqual(IconCatalogue.resolve("unknown.icon"), "clean.clock")
        XCTAssertEqual(IconCatalogue.resolve("CLEAN.CLOCK"), "clean.clock")
    }

    func testResolveKnownStoredIdReturnsItself() {
        XCTAssertEqual(IconCatalogue.resolve("clean.clock"), "clean.clock")
        XCTAssertEqual(IconCatalogue.resolve("misprint.burst"), "misprint.burst")
        XCTAssertEqual(IconCatalogue.resolve("comic.ticks"), "comic.ticks")
        XCTAssertEqual(IconCatalogue.resolve("clean.bars"), "clean.bars")
    }

    func testCatalogueCounts() {
        XCTAssertEqual(IconCatalogue.all.count, 17)
        XCTAssertEqual(IconCatalogue.inStyle(.clean).count, 9)
        XCTAssertEqual(IconCatalogue.inStyle(.comic).count, 1)
        XCTAssertEqual(IconCatalogue.inStyle(.misprint).count, 7)
    }

    func testEveryFileBasedIconHasValidIcoAndFrames() {
        let fileBasedIcons = IconCatalogue.all.filter { $0.id != "clean.clock" }
        XCTAssertEqual(fileBasedIcons.count, 16)

        for icon in fileBasedIcons {
            let data = IconCatalogue.openIconData(id: icon.id)
            XCTAssertNotNil(data, "Icon asset missing for \(icon.id)")
            guard let d = data, d.count >= 6 else {
                XCTFail("Invalid ICO header for \(icon.id)")
                continue
            }
            // ICO header: reserved = 0, type = 1
            let reserved = Int(d[0]) | (Int(d[1]) << 8)
            let type = Int(d[2]) | (Int(d[3]) << 8)
            let count = Int(d[4]) | (Int(d[5]) << 8)
            XCTAssertEqual(reserved, 0)
            XCTAssertEqual(type, 1)
            XCTAssertGreaterThanOrEqual(count, 6)

            var frameSizes: [Int] = []
            for i in 0..<count {
                let e = 6 + i * 16
                let rawW = Int(d[e])
                frameSizes.append(rawW == 0 ? 256 : rawW)
            }
            for requiredSize in [16, 20, 24, 32, 48, 256] {
                XCTAssertTrue(frameSizes.contains(requiredSize), "Missing size \(requiredSize) in \(icon.id)")
            }
        }
    }

    func testReadFrameReturnsPngData() {
        guard let frame = IconCatalogue.readFrameData(id: "comic.ticks", size: 24) else {
            XCTFail("Failed to read frame")
            return
        }
        XCTAssertGreaterThanOrEqual(frame.count, 8)
        let pngHeader = [UInt8](frame.prefix(4))
        XCTAssertEqual(pngHeader, [0x89, 0x50, 0x4E, 0x47])
    }

    func testImageGenerationForEveryIcon() {
        for icon in IconCatalogue.all {
            for size in [16, 24, 32, 48, 72, 256] {
                let img = IconCatalogue.image(for: icon.id, size: size, accentColor: .systemGreen)
                XCTAssertNotNil(img, "Failed to create image for \(icon.id) at size \(size)")
                XCTAssertEqual(img?.size.width, CGFloat(size))
                XCTAssertEqual(img?.size.height, CGFloat(size))
            }
        }
    }

    func testClockIconDrawsAtSpecifiedSize() {
        let clock12 = IconCatalogue.drawClockImage(size: 12, accentColor: .systemGreen)
        XCTAssertEqual(clock12.size.width, 12)
        XCTAssertEqual(clock12.size.height, 12)

        let clock48 = IconCatalogue.drawClockImage(size: 48, accentColor: .systemBlue)
        XCTAssertEqual(clock48.size.width, 48)
        XCTAssertEqual(clock48.size.height, 48)
    }
}

