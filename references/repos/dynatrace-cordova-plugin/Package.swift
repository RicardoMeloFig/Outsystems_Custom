// swift-tools-version: 5.9

import PackageDescription

let package = Package(
    name: "DynatraceCordovaPlugin",
    platforms: [.iOS(.v15)],
    products: [
        .library(
            name: "DynatraceCordovaPlugin",
            targets: ["DynatraceCordovaPlugin"]
        )
    ],
    dependencies: [
        .package(url: "https://github.com/ionic-team/capacitor-swift-pm.git", from: "8.0.1")
    ],
    targets: [
        .target(
            name: "DynatraceCordovaPlugin",
            dependencies: [
                .product(name: "Cordova", package: "capacitor-swift-pm"),
                "Dynatrace"
            ],
            path: "ios",
            publicHeadersPath: "."
        ),
        .binaryTarget(
            name: "Dynatrace",
            path: "files/iOS/Dynatrace.xcframework"
        )
    ]
)