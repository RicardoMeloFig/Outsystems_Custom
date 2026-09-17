// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "OutsystemsCapacitorOutsystemsSslpinning",
    platforms: [.iOS(.v15)],
    products: [
        .library(
            name: "OutsystemsCapacitorOutsystemsSslpinning",
            targets: ["OutSystemsSSLPinningPlugin"])
    ],
    dependencies: [
        .package(url: "https://github.com/ionic-team/capacitor-swift-pm.git", from: "8.0.0")
    ],
    targets: [
        .binaryTarget(
            name: "TrustKit",
            path: "ios/TrustKit.xcframework" // or relative path
        ),
        .target(
            name: "OutSystemsSSLPinningPlugin",
            dependencies: [
                .product(name: "Capacitor", package: "capacitor-swift-pm"),
                .product(name: "Cordova", package: "capacitor-swift-pm"),
                "TrustKit"
            ],
            path: "ios/Sources/OutSystemsSSLPinningPlugin",
            publicHeadersPath: "include")
    ]
)
