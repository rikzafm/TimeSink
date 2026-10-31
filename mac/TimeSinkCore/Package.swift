// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "TimeSinkCore",
    platforms: [
        .macOS(.v15)
    ],
    products: [
        .library(
            name: "TimeSinkCore",
            targets: ["TimeSinkCore"]
        )
    ],
    dependencies: [],
    targets: [
        .target(
            name: "TimeSinkCore",
            dependencies: [],
            path: "Sources/TimeSinkCore"
        ),
        .testTarget(
            name: "TimeSinkCoreTests",
            dependencies: ["TimeSinkCore"],
            path: "Tests/TimeSinkCoreTests"
        )
    ]
)
