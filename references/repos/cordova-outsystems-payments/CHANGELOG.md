# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

The changes documented here do not include those from the original repository.

## 1.2.17

### Chores

- chore(ios): minor hook corrections (#78)

## 1.2.16

### Chores

- chore: update dependency to OSPaymentsLib-Android (#77)

## 1.2.15

### Fixes

- (iOS) Correct hook to work on Cordova iOS 8 (https://outsystemsrd.atlassian.net/browse/RMET-5120)

## 1.2.14

### Chores

- (android) remove unecessary dependency to `oscore` and `oscordova` on Android (https://outsystemsrd.atlassian.net/browse/RMET-4900)

## 1.2.13

### Chores

- (ios) remove unecessary dependency to OSCommonPluginLib on iOS (https://outsystemsrd.atlassian.net/browse/RMET-4899)

## 1.2.12

### Fixes

- Align handling of optional payment config properties in Cordova / Capacitor hooks. Fixes build error for iOS when missing properties like `payment_supported_card_countries`.

## 1.2.11

### Fixes
- (android) Fixes missing strings.xml on target project (https://outsystemsrd.atlassian.net/browse/RMET-4779)
- (android) include all directory possibilities for json-config/PaymentsPluginConfiguration.json config file (https://outsystemsrd.atlassian.net/browse/RMET-4779)
- (ios) include all directory possibilities for json-config/PaymentsPluginConfiguration.json config file (https://outsystemsrd.atlassian.net/browse/RMET-4779)

## 1.2.10

### Fixes
- (android) Removes the `kotlin-kapt` plugin that was being added, fixing builds for Capacitor apps (https://outsystemsrd.atlassian.net/browse/RMET-4515)

## 1.2.9

### Fixes
- (android,ios) Add fix to `iOSCopyPreferences.js` hook to correctly locate `PaymentsPluginConfiguration.json` on MOCA builds. (https://outsystemsrd.atlassian.net/browse/RMET-4469)

## 1.2.8

### Fixes
- (android) Migrate back button navigation while payment is being processed by updating dependency to `OSPaymentsLib-Android` (https://outsystemsrd.atlassian.net/browse/RMET-4338)

## 1.2.7

### Features
- (android) Updates hooks to avoid duplicates in `strings.xml` (https://outsystemsrd.atlassian.net/browse/RMET-4025).

## 1.2.6

### Fixes
- (android) Add missing Gson gradle dependency

## 1.2.5

### Fixes
- (android) Release builds With Capacitor failing.

## 1.2.4

### Features
- (android,ios) Add support for Capacitor (https://outsystemsrd.atlassian.net/browse/RMET-3649).

## 1.2.3

### Chores
- (ios) Replace `cordova-plugin-add-swift-support` plugin with the `SwiftVersion` preference (https://outsystemsrd.atlassian.net/browse/RMET-4037).

## 1.2.2

### Fixes
- Fix: Update dependency to `OSPaymentsLib-Android` that adds `@SerializableName` annotation to avoid issues when using code obfuscation (https://outsystemsrd.atlassian.net/browse/RMET-3387).

## 1.2.1

### Fixes
- Fix: [Android] Updates dependency to OSCordova (https://outsystemsrd.atlassian.net/browse/RPM-5093)

## 1.2.0

### Features
- Update the iOS framework. This adds the Privacy Manifest file (https://outsystemsrd.atlassian.net/browse/RMET-3283).

### Chores
- Update cordova hooks with new OutSystems specific errors. (https://outsystemsrd.atlassian.net/browse/RMET-3311)

## [Version 1.1.2]

### 21-12-2023
- Fix: [Android] Updates dependency to OSCore and OSCordova (https://outsystemsrd.atlassian.net/browse/RMET-2993).

## [Version 1.1.1]

### 03-10-2023
- Fix: [iOS] Fixes path, removing duplicate string (https://outsystemsrd.atlassian.net/browse/RMET-2855).

### 13-07-2023
- Feat: Update hook to consider new resources paths (https://outsystemsrd.atlassian.net/browse/RMET-2477).

- Chore: Update cordova hooks with new OutSystems specific errors. (https://outsystemsrd.atlassian.net/browse/RMET-3311)

## [Version 1.1.2]

### 21-12-2023
- Fix: [Android] Updates dependency to OSCore and OSCordova (https://outsystemsrd.atlassian.net/browse/RMET-2993).

## [Version 1.1.1]

### 03-10-2023
- Fix: [iOS] Fixes path, removing duplicate string (https://outsystemsrd.atlassian.net/browse/RMET-2855).

### 13-07-2023
- Feat: Update hook to consider new resources paths (https://outsystemsrd.atlassian.net/browse/RMET-2477).

## [Version 1.1.0]

### 10-02-2023
- Feat: [iOS] Make library available as `xcframework` (https://outsystemsrd.atlassian.net/browse/RMET-2280).

## 06-01-2023
- Feat: [iOS] Add access token to Full Payment Process (https://outsystemsrd.atlassian.net/browse/RMET-2147).

## 04-01-2023
Android - Add extra parameter for accessToken (https://outsystemsrd.atlassian.net/browse/RMET-2089)

## 28-12-2022
Android - update dependency to PaymentsLib-Android (https://outsystemsrd.atlassian.net/browse/RMET-2120)

## 16-12-2022
Android - remove dependency to jcenter (https://outsystemsrd.atlassian.net/browse/RMET-2036)

## 07-12-2022
Android - implemented payment processing using Stripe (https://outsystemsrd.atlassian.net/browse/RMET-2079)

- Fix: [Android] Use fixed versions instead of dynamic ones. (https://outsystemsrd.atlassian.net/browse/RMET-2045)

### 2022-12-02
- Chore: [iOS] Remove all the `OSPaymentsLib` files and replace them by the new `OSPaymentsPluginLib` pod.
- Feat: [iOS] Update hook so that it checks if Stripe's is configured as the Payment Service Provider and update `plist` file accordingly (https://outsystemsrd.atlassian.net/browse/RMET-2078).

- Fix: [Android] Use fixed versions instead of dynamic ones. (https://outsystemsrd.atlassian.net/browse/RMET-2045)

## [Version 1.0.1]

### 2022-11-08
- Fix: [iOS] Replace the old `OSCore` framework for the new `OSCommonPluginLib` pod.

## [Version 1.0.0]

### 2022-08-17
- Android - implement setDetails (triggerPayment) for Android, using Google Pay (https://outsystemsrd.atlassian.net/browse/RMET-1009)

### 2022-08-09
- Android - implemented isReadyToPay for Google Pay (https://outsystemsrd.atlassian.net/browse/RMET-790)

### 2022-08-08
- Android - added field verification to fail build if fields are missing in JSON config file (https://outsystemsrd.atlassian.net/browse/RMET-1721)

### 2022-08-03
- Android - implementated setupConfiguration method for Android (https://outsystemsrd.atlassian.net/browse/RMET-1721)
- Feat: Set Payment Details (https://outsystemsrd.atlassian.net/browse/RMET-1723).

### 2022-08-02
- Feat: Check Wallet Availability for Payment (https://outsystemsrd.atlassian.net/browse/RMET-1695).

### 2022-08-01
- Android - implemented hook to copy configuration data from JSON resource file. (https://outsystemsrd.atlassian.net/browse/RMET-1721)
- Feat: Setup Apple Pay Configurations (https://outsystemsrd.atlassian.net/browse/RMET-1722).
