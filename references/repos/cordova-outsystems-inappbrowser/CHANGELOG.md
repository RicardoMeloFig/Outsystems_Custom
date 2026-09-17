# [2.1.0](https://github.com/OutSystems/cordova-outsystems-inappbrowser/compare/2.0.3...2.1.0) (2026-09-09)


### Features

* **ios:** add Swift Package Manager support ([#77](https://github.com/OutSystems/cordova-outsystems-inappbrowser/issues/77)) ([0b7aef8](https://github.com/OutSystems/cordova-outsystems-inappbrowser/commit/0b7aef81a9106dbae82c8e0a2c148d8e887f7174))

## [2.0.3](https://github.com/OutSystems/cordova-outsystems-inappbrowser/compare/2.0.2...2.0.3) (2026-08-07)


### Bug Fixes

* **android:** bump ioninappbrowser-android to 2.0.2 for background close fix ([#75](https://github.com/OutSystems/cordova-outsystems-inappbrowser/issues/75)) ([005054a](https://github.com/OutSystems/cordova-outsystems-inappbrowser/commit/005054a415a898d8c2cec834640a8ce9d8b219e0))

## [2.0.2](https://github.com/OutSystems/cordova-outsystems-inappbrowser/compare/2.0.1...2.0.2) (2026-07-08)


### Bug Fixes

* **android:** bump ioninappbrowser-android to 2.0.1 for Android 18 URI grant fix ([#74](https://github.com/OutSystems/cordova-outsystems-inappbrowser/issues/74)) ([0373fbd](https://github.com/OutSystems/cordova-outsystems-inappbrowser/commit/0373fbd148f228a38e98a6ba5855620bdf39ace7))

## [2.0.1](https://github.com/OutSystems/cordova-outsystems-inappbrowser/compare/2.0.0...2.0.1) (2026-06-19)

## 2.0.0

### Fixes

- Android: isolate WebView local storage and cookies from the main app by default on Android 28+. [RMET-4918](https://outsystemsrd.atlassian.net/browse/RMET-4918)

### BREAKING CHANGES

- Android: `openInWebView` now isolates WebView storage by default on Android 28+. Apps that need to share the main app WebView's `localStorage` or cookies must set `android.isIsolated` to `false`.

## 1.6.5

### Fixes

- android: Remove extra requests for checking if a URL hosts a PDF with `openInWebView`. This makes sure that for non-PDF urls, no extra request to the webpage/server is done [RMET-5141](https://outsystemsrd.atlassian.net/browse/RMET-5141) / [RPM-6744](https://outsystemsrd.atlassian.net/browse/RPM-6744).

## 1.6.4

### Fixes

- iOS: Fixed an issue where `window.open()` calls and links with `target="_blank"` were not handled in the OpenInWebView option, causing navigation to fail. [RMET-5110](https://outsystemsrd.atlassian.net/browse/RMET-5110)

## 1.6.3

### Fixes

- android: Do not show error when web page has a resource that is unresolvable [RMET-4937](https://outsystemsrd.atlassian.net/browse/RMET-4937)

## 1.6.2

### Fixes

- iOS: Fixed an issue that was preventing iframe content from loading in the OpenInWebView option. [RMET-4517](https://outsystemsrd.atlassian.net/browse/RMET-4517).
- iOS: Enable WebKit inspector for debugging on iOS 16.4+ in DEBUG builds.

## 1.6.1

### Fixes

- iOS: Fixes an issue where dismissing an alert view triggered the onBrowserClosed event. [RMET-4500](https://outsystemsrd.atlassian.net/browse/RMET-4500).

## 1.6.0

### Features

- Android: Allow for photo and video capturing, and filter for media type for file uploads (`onShowFileChooser`) [RMET-4466](https://outsystemsrd.atlassian.net/browse/RMET-4466)

## 1.5.0

### Features

- Android: Add support for PDF files in the WebView via PDF.js (only for the OpenInWebView option) [RMET-2053](https://outsystemsrd.atlassian.net/browse/RMET-2053)

## 1.4.1

### Features

- Added support for predictive back navigation for Android 13+ (https://outsystemsrd.atlassian.net/browse/RMET-4335)

## 1.4.0

#### Features

- Add support for passing custom headers to web view (only for the OpenInWebView option). [RMET-4287](https://outsystemsrd.atlassian.net/browse/RMET-4287).

## 1.3.1

### iOS

#### Features

- Added support for back and forward swipe navigation gestures in `WKWebView` via the `allowsBackForwardNavigationGestures` option. (for openInWebView option only) (https://outsystemsrd.atlassian.net/browse/RMET-4216).

## 1.3.0

#### Features

- Users now receive an event when the navigation occurs (for openInWebView option only) (https://outsystemsrd.atlassian.net/browse/RMET-4120).

### Android

#### Chores

- Update dependency to Android native library (https://outsystemsrd.atlassian.net/browse/RMET-3982).

## 1.2.1

### Android

#### Fixes

- Remove unnecessary permissions from AndroidManifest (https://outsystemsrd.atlassian.net/browse/RMET-3987).

## 1.2.0

### Android

#### Chores

- Bumps Kotlin and Gradle versions (https://outsystemsrd.atlassian.net/browse/RMET-3887).

## 1.1.0

### Android

#### Chores
- Remove unnecesary `kotlin-kapt` plugin from build.gradle file (https://outsystemsrd.atlassian.net/browse/RMET-3804).

### Features
- Handle Edge-to-Edge on all Android versions.

## 1.0.2
- Android: Fix issue where the custom tabs browser wasn't being closed when navigating back to the app
- Android: Fix race condition that caused the `BrowserFinished` event to not be fired in some instances with the system browser

### Fixes
- Android: Fix issue where some URLs weren't being open in Custom Tabs and the External Browser (https://outsystemsrd.atlassian.net/browse/RMET-3680)

## 1.0.0

### Features
- Add `Close` feature for WebView and System Browser on Android (https://outsystemsrd.atlassian.net/browse/RMET-3428).
- Add error codes and messages on iOS (https://outsystemsrd.atlassian.net/browse/RMET-3465).
- Format error codes and messages on Android (https://outsystemsrd.atlassian.net/browse/RMET-3466).
- Add permissions requests and opening file chooser to `OpenInWebView` feature on Android (https://outsystemsrd.atlassian.net/browse/RMET-3534).
- Add error and loading screens for `OpenInWebView` feature for Android (https://outsystemsrd.atlassian.net/browse/RMET-3492).
- Add custom error page for `OpenInWebView` feature (https://outsystemsrd.atlassian.net/browse/RMET-3491).
- Add browser events to `OpenInSystemBrowser` feature on Android (https://outsystemsrd.atlassian.net/browse/RMET-3431).
- Add `OpenInSystemBrowser`'s features on Android (https://outsystemsrd.atlassian.net/browse/RMET-3424).
- Add possibility to override the user agent used in `OpenInWebView`'s webview (https://outsystemsrd.atlassian.net/browse/RMET-3490).
- Add browser events to `OpenInWebView` feature (https://outsystemsrd.atlassian.net/browse/RMET-3432).
- Add `OpenInWebView` with current features and default UI on Android (https://outsystemsrd.atlassian.net/browse/RMET-3426).
- Add `Close` feature on iOS (https://outsystemsrd.atlassian.net/browse/RMET-3427).
- Add `OpenInWebView`'s interface customisations on iOS (https://outsystemsrd.atlassian.net/browse/RMET-3489).
- Add `OpenInWebView`'s event listeners on iOS (https://outsystemsrd.atlassian.net/browse/RMET-3430).
- Add `OpenInWebView`'s features on iOS (https://outsystemsrd.atlassian.net/browse/RMET-3425).
- Add `OpenInSystemBrowser`'s event listeners on iOS (https://outsystemsrd.atlassian.net/browse/RMET-3429).
- Add `OpenInSystemBrowser`'s features on iOS (https://outsystemsrd.atlassian.net/browse/RMET-3423).
- Add `OpenInExternalBrowser`'s features on Android (https://outsystemsrd.atlassian.net/browse/RMET-3422).
- Add `OpenInExternalBrowser` on iOS (https://outsystemsrd.atlassian.net/browse/RMET-3421).
- [Bridge] Adds cordova bridge, with types (https://outsystemsrd.atlassian.net/browse/RMET-3419).
- Add content to `README` (https://outsystemsrd.atlassian.net/browse/RMET-3473).
