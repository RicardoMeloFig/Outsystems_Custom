// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "com.outsystems.firebase.cloudmessaging",
    platforms: [.iOS(.v15)],
    products: [
        .library(
            name: "com.outsystems.firebase.cloudmessaging",
            targets: ["com.outsystems.firebase.cloudmessaging"])
    ],
    dependencies: [
        .package(url: "https://github.com/apache/cordova-ios.git", branch: "master"),
        .package(url: "https://github.com/firebase/firebase-ios-sdk.git", from: "10.29.0")
    ],
    targets: [
        .binaryTarget(
            name: "OSFirebaseMessagingLib",
            path: "src/ios/frameworks/OSFirebaseMessagingLib.xcframework"
        ),
        .binaryTarget(
            name: "OSLocalNotificationsLib",
            path: "src/ios/frameworks/OSLocalNotificationsLib.xcframework"
        ),
        .target(name: "OSCloudMessagingObjectiveC",
                dependencies: [
                    .product(name: "Cordova", package: "cordova-ios"),
                    .product(name: "FirebaseMessaging", package: "firebase-ios-sdk"),
                    .target(name: "OSFirebaseMessagingLib"),
                    .target(name: "OSLocalNotificationsLib")
                ],
                path: "src/ios",
                exclude: [
                    "frameworks/OSFirebaseMessagingLib.xcframework",
                    "frameworks/OSLocalNotificationsLib.xcframework",
                    "OSFCMEventExtensions.swift",
                    "OSFirebaseCloudMessaging.swift",
                    "NotificationsModel.momd"
                ],
                publicHeadersPath: "."),
        .target(
            name: "com.outsystems.firebase.cloudmessaging",
            dependencies: [
                .product(name: "Cordova", package: "cordova-ios"),
                .product(name: "FirebaseMessaging", package: "firebase-ios-sdk"),
                .target(name: "OSFirebaseMessagingLib"),
                .target(name: "OSLocalNotificationsLib"),
                .target(name: "OSCloudMessagingObjectiveC")
            ],
            path: "src/ios",
            exclude: [
                "frameworks/OSFirebaseMessagingLib.xcframework",
                "frameworks/OSLocalNotificationsLib.xcframework",
                "AppDelegate+OSFirebaseCloudMessaging.h",
                "AppDelegate+OSFirebaseCloudMessaging.m"
            ],
            resources: [
                .copy("NotificationsModel.momd")
            ],
            publicHeadersPath: ".")
    ]
)
