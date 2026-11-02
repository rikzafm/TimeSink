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
    dependencies: [
        .package(url: "https://github.com/sqlcipher/SQLCipher.swift.git", from: "4.10.0")
    ],
    targets: [
        .target(
            name: "TimeSinkCore",
            dependencies: [
                .product(name: "SQLCipher", package: "SQLCipher.swift")
            ],
            path: "Sources/TimeSinkCore",
            resources: [.copy("Resources")],
            cSettings: [
                .define("SQLITE_HAS_CODEC")
            ],
            swiftSettings: [
                .define("SQLITE_HAS_CODEC")
            ]
        ),
        .executableTarget(
            name: "TimeSinkApp",
            dependencies: [
                "TimeSinkCore",
                .product(name: "SQLCipher", package: "SQLCipher.swift")
            ],
            path: "Sources/TimeSinkApp",
            cSettings: [
                .define("SQLITE_HAS_CODEC")
            ],
            swiftSettings: [
                .define("SQLITE_HAS_CODEC")
            ]
        ),
        .testTarget(
            name: "TimeSinkCoreTests",
            dependencies: [
                "TimeSinkCore",
                .product(name: "SQLCipher", package: "SQLCipher.swift")
            ],
            path: "Tests/TimeSinkCoreTests",
            cSettings: [
                .define("SQLITE_HAS_CODEC")
            ],
            swiftSettings: [
                .define("SQLITE_HAS_CODEC")
            ]
        )
    ]
)
