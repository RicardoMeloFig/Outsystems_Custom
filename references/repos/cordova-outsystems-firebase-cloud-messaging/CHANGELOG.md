# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

The changes documented here do not include those from the original repository.

## [2.6.1]

### Chores

- chore: update dependency to OSFirebaseMessagingLib-Android (#139)

## [2.6.0]

### Features

- (ios) Support for Swift Package Manager (https://outsystemsrd.atlassian.net/browse/RMET-5137)

### Fixes

- (android) Avoid overwriting other plugins' `ext.postBuildExtras` Gradle configuration (https://outsystemsrd.atlassian.net/browse/RMET-5211)

## [2.5.6]

### Fixes

- (android) Handle errors in `RegisterDevice` instead of hanging indefinitely (https://outsystemsrd.atlassian.net/browse/RMET-4953)

## [2.5.5]

### Fixes

- (android) Remove unnecessary dependencies to `oscore` and `oscordova` (https://outsystemsrd.atlassian.net/browse/RMET-4900)

## [2.5.4]

### Fixes

- (android) Fix capacitor builds by removing kapt dependency (https://outsystemsrd.atlassian.net/browse/RMET-4862)

## [2.5.3]

### Fixes

- (android) Fixes missing strings.xml on target project (https://outsystemsrd.atlassian.net/browse/RMET-4774)

## [2.5.2]

### Fixes

- (android) MABS 12 ::: Update build action to add Kotlin Gradle Plugin to app's build.gradle file (https://outsystemsrd.atlassian.net/browse/RMET-4515)

## [2.5.1]

### Fix

- (android) Include notification extras in click event even if there is no deeplink (https://outsystemsrd.atlassian.net/browse/RMET-4397).
- (ios) Updates hook to avoid duplicates in `.plist` (https://outsystemsrd.atlassian.net/browse/RMET-4277).

## [Version 2.5.0]

### Feature

- Support for Capacitor, including build actions and new hooks (https://outsystemsrd.atlassian.net/browse/RMET-3639).

### Fix
- (android) Updates hook to avoid duplicates in `strings.xml` (https://outsystemsrd.atlassian.net/browse/RMET-4025).

### Chore

- Chore: Remove depenency on `cordova-outsystems-firebase-core` (https://outsystemsrd.atlassian.net/browse/RMET-3639).

## [Version 2.4.0]

### Features
- (android) Subscribes and unsubscribes from `appGeneralTopic` - `appIdentifier-general-topic-android` when registering and unregistering the device. (https://outsystemsrd.atlassian.net/browse/RMET-2948).
- (ios) Subscribes and unsubscribes from `appGeneralTopic` - `appIdentifier-general-topic-ios` when registering and unregistering the device. (https://outsystemsrd.atlassian.net/browse/RMET-2947).
- (android) Handle icon and color parameters in setupNotificationBuilder for Android 12+ compatibility.

### Fix
- (ios) `NotificationClickedV2` not being triggered when deep link property is not set (https://outsystemsrd.atlassian.net/browse/RMET-3695).

## [Version 2.3.2]

### Features
- Feat: Android | Update dependency to Firebase Cloud Messaging Android library (https://outsystemsrd.atlassian.net/browse/RMET-3608).

## [Version 2.3.1]

### Fixes
- Fixes notification click when notification is delievered while the app is in foreground (https://outsystemsrd.atlassian.net/browse/RMET-3559).
- Removes protected/reserverd keys from being sent as deeplink parameters data when clicking notification, and also when receiving in-app notifications. (https://outsystemsrd.atlassian.net/browse/RMET-3559).

### Fixes
- Remove return by callbacks, eliminating the disalignment with the threads where the methods are being called (https://outsystemsrd.atlassian.net/browse/RMET-3554).

### Chores:
- Update `firebaseMessaging` iOS pod to version `10.29.0`.

## [Version 2.3.0]

### Features
- Enable message delivery metrics exportation to BigQuery (https://outsystemsrd.atlassian.net/browse/RMET-3511).

### Fixes
- Remove the main thread blocking (https://outsystemsrd.atlassian.net/browse/RMET-3508).
- Update `oscordova` dependency (https://outsystemsrd.atlassian.net/browse/RMET-3540)

## [Version 2.2.1]

### 03-06-2024
- Fix: Fix for in-app notification with deeplink and for silent notifications (https://outsystemsrd.atlassian.net/browse/RMET-3454)

## [Version 2.2.0]

### 15-05-2024
- Feat: Update dependency to OSFirebaseMessagingLib-Android, making `FirebaseMessagingReceiveService` an open class so it can be extended (https://outsystemsrd.atlassian.net/browse/RMET-3407).

### 17-04-2024
- Fix: Properly serialize data objects with annotation (https://outsystemsrd.atlassian.net/browse/RMET-3109).

- Feat: Deal with `sounds` as a resource in ODC. (https://outsystemsrd.atlassian.net/browse/RMET-3315)

## [Version 2.1.0]

- Fix: Make `cleanUp` hook only run after all sound files are copied successfully. (https://outsystemsrd.atlassian.net/browse/RMET-3326)
- Chore: Update cordova hooks with new OutSystems specific errors. (https://outsystemsrd.atlassian.net/browse/RMET-3302)
- Chore: Update `FirebaseMessaging` iOS pod to version `10.23.0`. This includes the Privacy Manifest (https://outsystemsrd.atlassian.net/browse/RMET-3274).

## [Version 2.0.1]
- Fix: Only deal with intents from the plugin  (https://outsystemsrd.atlassian.net/browse/RMET-3013)

## [Version 2.0.0]
- Feat: update sound hook to unzip sound files, for both iOS and Android (https://outsystemsrd.atlassian.net/browse/RMET-2464).
- Feat: update firebase core version (https://outsystemsrd.atlassian.net/browse/RMET-2451).

## [Version 1.2.0]

### 23-05-2023
- Feat: Update Error Codes Messages (https://outsystemsrd.atlassian.net/browse/RMET-2555).

### 10-05-2023
- Feat: [iOS] React to the App Route Event (https://outsystemsrd.atlassian.net/browse/RMET-2392).

### 08-05-2023
- Feat: [iOS] External App Authorisation (https://outsystemsrd.atlassian.net/browse/RMET-2390).
- Feat: [iOS] React to the Web Route Event (https://outsystemsrd.atlassian.net/browse/RMET-2394).
- Feat: [Android] Fix hook for sounds (https://outsystemsrd.atlassian.net/browse/RMET-2215).

### 03-05-2023
- Feat: [iOS] React to the Internal Route Event (https://outsystemsrd.atlassian.net/browse/RMET-2388).

### 02-05-2023
- Feat: [Android] Add action buttons (https://outsystemsrd.atlassian.net/browse/RMET-2387).

### 28-04-2023
- Feat: [ıOS] React to a notification action click (https://outsystemsrd.atlassian.net/browse/RMET-2383).

### 24-04-2023
- Feat: [iOS] React to a triggered notification with a custom action configured (https://outsystemsrd.atlassian.net/browse/RMET-2382).

### 06-04-2023
- Fix: [Android] Fix crash on notification click (https://outsystemsrd.atlassian.net/browse/RMET-2421).

### 04-04-2023
- Feat: [Android] Add hook to copy sound files (https://outsystemsrd.atlassian.net/browse/RMET-2378).

### 31-03-2023
- Feat: [iOS] React to a triggered notification with custom sound enabled (https://outsystemsrd.atlassian.net/browse/RMET-2381).

### 07-03-2023
- Feat: [Android] Use `OSLocalNotificationsLib` to trigger a local notification immediately (https://outsystemsrd.atlassian.net/browse/RMET-2310).

### 03-03-2023
- Feat: [iOS] Use `OSLocalNotificationsLib` to trigger a local notification immediately (https://outsystemsrd.atlassian.net/browse/RMET-2311).

## [Version 1.1.3]

### 10-04-2023
- Fix: [Android] - Added POST_NOTIFICATION permission for Android >= 13. (https://outsystemsrd.atlassian.net/browse/RMET-2424)

### 29-03-2023
- Fix: [Android] - Removed allowBackup property from android lib manifest. (https://outsystemsrd.atlassian.net/browse/RMET-2406)

### 23-02-2023
- Fix: [Android] - Add a guard to deal with cases when there's no data in the extras map (https://outsystemsrd.atlassian.net/browse/RMET-2312)

### 10-02-2023
- Feat: [iOS] Make library available as `xcframework` (https://outsystemsrd.atlassian.net/browse/RMET-2280).

### 26-01-2023
- Fix: [iOS] Silent Notifications not being triggered. (https://outsystemsrd.atlassian.net/browse/RPM-3590).

## [Version 1.1.0]

### 21-12-2022
- Fix: [Android] Fixes clicking a notification without deeplink, when app in foreground (https://outsystemsrd.atlassian.net/browse/RMET-2114)

### 16-12-2022
- Feat: [iOS] Update iOS lib files in order to include the new Get APNs Token method (https://outsystemsrd.atlassian.net/browse/RMET-2054).
- Replaced jcenter with more up to date mavenCentral [RMET-2036](https://outsystemsrd.atlassian.net/browse/RMET-2036)

### 10-11-2022
- Use fixed versions (https://outsystemsrd.atlassian.net/browse/RMET-2045).

## [Version 1.0.5]

### 08-11-2022
- Fix: [iOS] Replace the old `OSCore` framework for the new `OSCommonPluginLib` pod.

## [Version 1.0.4]

### 12-10-2022
- Fix: [iOS] Rename the swizzled `appDelegate:didFinishLaunchingWithOptions:` method to something unique (https://outsystemsrd.atlassian.net/jira/software/c/projects/RMET/boards/893?selectedIssue=RPM-3153).

## [Version 1.0.3]

### 20-09-2022
- Added permission request to receive notifications on Android API >= 33. (https://outsystemsrd.atlassian.net/browse/RMET-1709)

## [Version 1.0.2]

### 28-07-2022
- Renamed classes to avoid incompatibility. (https://outsystemsrd.atlassian.net/browse/RMET-1743)

### 26-07-2022
- Added dependency on external libs. Changed methods to sync. (https://outsystemsrd.atlassian.net/browse/RMET-1480)

## [Version 1.0.1]

### 29-06-2022
- Removed hook that adds swift support and added the plugin as dependency. (https://outsystemsrd.atlassian.net/browse/RMET-1680)

## [Version 1.0.0]

### 24-06-2022
- Update for multiple callback ids.

### 23-06-2022
- Update error codes and messages.

### 22-06-2022
- Include image in local notifications. (https://outsystemsrd.atlassian.net/browse/RMET-1676).

### 21-06-2022
- Allow default values for channel name and description (https://outsystemsrd.atlassian.net/browse/RMET-1612).

### 20-06-2022
- Process deep link received in Notification (https://outsystemsrd.atlassian.net/browse/RMET-1605).

### 15-06-2022
- Implements an event which is triggered when a notification is received.(https://outsystemsrd.atlassian.net/browse/RMET-1610)
- Receive and trigger a Dialog Notification (https://outsystemsrd.atlassian.net/browse/RMET-1609).

### 08-06-2022
- Save silent notification in DB when app is not on foreground - android (https://outsystemsrd.atlassian.net/browse/RMET-1604).
- Silent notifications to send event when app is on foreground - android (https://outsystemsrd.atlassian.net/browse/RMET-1595).

### 31-05-2022
- Added get pending notifications to both JS and bridge layers.

### 30-05-2022
- Added library with Room databse.
- Added library with CoreData databse.

### 02-06-2022
- Get Pending Notificaitons implemented (https://outsystemsrd.atlassian.net/browse/RMET-1596 ).
- Save Silent Notification in a database when the app is not opened (https://outsystemsrd.atlassian.net/browse/RMET-1603).
- Receive Silent Notification and Trigger Notify Event (https://outsystemsrd.atlassian.net/browse/RMET-1589).
- Implemented silent notification app on foreground event on Android (https://outsystemsrd.atlassian.net/browse/RMET-1540, https://outsystemsrd.atlassian.net/browse/

### 31-05-2022
- Added methods for event handling in JS layer (https://outsystemsrd.atlassian.net/browse/RMET-1595, https://outsystemsrd.atlassian.net/browse/RMET-1589)

### 27-05-2022
- Updated lib to contain GetToken and OnReceivedNotification.

### 24-05-2022
- Includes register and unregister for Android (https://outsystemsrd.atlassian.net/browse/RMET-1540, https://outsystemsrd.atlassian.net/browse/RMET-1541)

### 23-05-2022
- Includes channel info in sendLocalNotification for Android (https://outsystemsrd.atlassian.net/browse/RMET-1561)

### 20-05-2022
- Implements sendLocalNotification (that used to be setBadgeNumber)

### 18-05-2022
- Implements setBadge, getBadge and clearNotifications for Android (https://outsystemsrd.atlassian.net/browse/RMET-1561, https://outsystemsrd.atlassian.net/browse/RMET-1576, https://outsystemsrd.atlassian.net/browse/RMET-1557)
