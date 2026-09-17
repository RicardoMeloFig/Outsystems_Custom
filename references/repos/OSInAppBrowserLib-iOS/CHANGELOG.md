## [2.3.2](https://github.com/OutSystems/OSInAppBrowserLib-iOS/compare/2.3.1...2.3.2) (2026-04-08)


### Bug Fixes

* use Xcode 16 instead of 26 ([#45](https://github.com/OutSystems/OSInAppBrowserLib-iOS/issues/45)) ([1e0b419](https://github.com/OutSystems/OSInAppBrowserLib-iOS/commit/1e0b4199b13ccf07e76a20b5023ad250001c7e39))
* window.open() handling in WKWebView by implementing createWebViewWith delegate ([#47](https://github.com/OutSystems/OSInAppBrowserLib-iOS/issues/47)) ([254327a](https://github.com/OutSystems/OSInAppBrowserLib-iOS/commit/254327a84ee85d3b9b868e9c8ad15717253b2d26))

## 2.3.1

### Fixes

- Fix iframe loading issue. [RMET-4517](https://outsystemsrd.atlassian.net/browse/RMET-4517).

## 2.3.0

### Features

- Enable WebKit inspector for debugging on iOS 16.4+ in DEBUG builds.

## 2.2.1

### Fixes

- Fixes an issue where dismissing an alert view triggered the onBrowserClosed event. [RMET-4500](https://outsystemsrd.atlassian.net/browse/RMET-4500).

## 2.2.0

### Features

- Add support for Swift Package Manager (SPM) compatibility. [RMET-4316](https://outsystemsrd.atlassian.net/browse/RMET-4316).

## 2.1.0

### Features

- Add support for passing custom headers to `WKWebView` (only for the openInWebView option). [RMET-4287](https://outsystemsrd.atlassian.net/browse/RMET-4287).

## 2.0.1

### Features

- Added support for back and forward swipe navigation gestures in `WKWebView` via the `allowsBackForwardNavigationGestures` option. (for openInWebView option only) (https://outsystemsrd.atlassian.net/browse/RMET-4216).

## 2.0.0

### Features

- Users now receive an event when the navigation occurs (for openInWebView option only) (https://outsystemsrd.atlassian.net/browse/RMET-4122).

### Fixes

- **BREAKING:** Fix an issue where the application freezes when 'OpenInExternalBrowser' is called

## 1.0.0

### Features
- Add custom error page for `OpenInWebView` feature (https://outsystemsrd.atlassian.net/browse/RMET-3491).
- Add `Close` feature (https://outsystemsrd.atlassian.net/browse/RMET-3427).
- Add `OpenInWebView`'s interface customisations (https://outsystemsrd.atlassian.net/browse/RMET-3489).
- Add `OpenInWebView`'s event listeners (https://outsystemsrd.atlassian.net/browse/RMET-3430).
- Add `OpenInWebView`'s features (https://outsystemsrd.atlassian.net/browse/RMET-3425).
- Add `OpenInSystemBrowser`'s event listeners (https://outsystemsrd.atlassian.net/browse/RMET-3429).
- Add `OpenInSystemBrowser`'s features (https://outsystemsrd.atlassian.net/browse/RMET-3423).
- Add `OpenInExternalBrowser` (https://outsystemsrd.atlassian.net/browse/RMET-3421).

### Chores
- Create `podspec` to enable CocoaPods publish (https://outsystemsrd.atlassian.net/browse/RMET-3578).
- Add content to `README` (https://outsystemsrd.atlassian.net/browse/RMET-3473).
