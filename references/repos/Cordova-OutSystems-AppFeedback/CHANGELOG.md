# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/) and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [Unreleased]
### Changes
- Migrates to AndroidX [RNMT-6236](https://outsystemsrd.atlassian.net/browse/RNMT-6236)
- Adds support for iOS 26 [RDMR-951](https://outsystemsrd.atlassian.net/browse/RDMR-951)

### Removals
- **BREAKING:** Drops support for apps targeting SDK 28 (Android 9) or lower [RNMT-6236](https://outsystemsrd.atlassian.net/browse/RNMT-6236)

## [2.6.0]
### Additions
- Adds granular media permissions for Android 13 [RNMT-5590](https://outsystemsrd.atlassian.net/browse/RNMT-5590)

## [2.5.0]
## Changes
- Update gradle to stop using the recently removed keyword 'compile' [RNMT-5055](https://outsystemsrd.atlassian.net/browse/RNMT-5055)

## [2.4.5]
### Fixes
- Update hooks to be compatible with Cordova CLI 10 [RNMT-4364](https://outsystemsrd.atlassian.net/browse/RNMT-4364)

## [2.4.4]
### Fixes
- Fixes an issue that was changing the application orientation lock on closing AppFeedback [RNMT-4290](https://outsystemsrd.atlassian.net/browse/RNMT-4290)

## [2.4.3]
### Fixes
- Fixes and issue that was preventing audio recording in Android 10 devices [RNMT-3763](https://outsystemsrd.atlassian.net/browse/RNMT-3763)

## [2.4.2]
### Fixes
- Navigation bar is now correctly positioned in Android devices with notch when using status bar overlay [RNMT-3663](https://outsystemsrd.atlassian.net/browse/RNMT-3663)
- Navigation bar no longer has an incorrect size in Android 4.4 devices when using status bar overlay [RNMT-3663](https://outsystemsrd.atlassian.net/browse/RNMT-3663)
- View is now correctly panned/resized in Android when keyboard is visible [RNMT-3664](https://outsystemsrd.atlassian.net/browse/RNMT-3664)

## [2.4.1]
### Fixes
- Fixes and issue that was preventing successfull builds with older versions than MABS 6.0 [RNMT-3696](https://outsystemsrd.atlassian.net/browse/RNMT-3696)

## [2.4.0]
### Fixes
- Register/unregister receiver in proper lifecycle callbacks [RNMT-3515](https://outsystemsrd.atlassian.net/browse/RNMT-3515)

### Additions
- Add support for DarkMode [RNMT-3629](https://outsystemsrd.atlassian.net/browse/RNMT-3629)
- Opens ECT through broadcast gestures when targeting Android 10 and above [RNMT-3515](https://outsystemsrd.atlassian.net/browse/RNMT-3515)

## [2.3.0]
### Additions
- Adds support for WKWebView [RNMT-2573](https://outsystemsrd.atlassian.net/browse/RNMT-2573)

[Unreleased]: https://github.com/OutSystems/Cordova-OutSystems-AppFeedback/compare/2.6.0...HEAD
[2.6.0]: https://github.com/OutSystems/Cordova-OutSystems-AppFeedback/compare/2.5.0...2.6.0
[2.5.0]: https://github.com/OutSystems/Cordova-OutSystems-AppFeedback/compare/2.4.5...2.5.0
[2.4.5]: https://github.com/OutSystems/Cordova-OutSystems-AppFeedback/compare/2.4.4...2.4.5
[2.4.4]: https://github.com/OutSystems/Cordova-OutSystems-AppFeedback/compare/2.4.3...2.4.4
[2.4.3]: https://github.com/OutSystems/Cordova-OutSystems-AppFeedback/compare/2.4.2...2.4.3
[2.4.2]: https://github.com/OutSystems/Cordova-OutSystems-AppFeedback/compare/2.4.1...2.4.2
[2.4.1]: https://github.com/OutSystems/Cordova-OutSystems-AppFeedback/compare/2.4.0...2.4.1
[2.4.0]: https://github.com/OutSystems/Cordova-OutSystems-AppFeedback/compare/2.3.0...2.4.0
[2.3.0]: https://github.com/OutSystems/Cordova-OutSystems-AppFeedback/compare/2.2.1...2.3.0
