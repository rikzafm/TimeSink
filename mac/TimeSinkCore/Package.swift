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
        ),
        .executable(
            name: "TimeSinkApp",
            targets: ["TimeSinkApp"]
        )
    ],
    dependencies: [],
    targets: [
        .target(
            name: "TimeSinkCore",
            dependencies: [],
            path: "Sources/TimeSinkCore"
        ),
        .executableTarget(
            name: "TimeSinkApp",
            dependencies: ["TimeSinkCore"],
            path: "Sources/TimeSinkApp"
        ),
        .testTarget(
            name: "TimeSinkCoreTests",
            dependencies: ["TimeSinkCore"],
            path: "Tests/TimeSinkCoreTests"
        )
    ]
)
