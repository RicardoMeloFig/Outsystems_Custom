# OSInAppBrowserLib

The `OSInAppBrowserLib-iOS` is a library built using `Swift` that provides a web browser view to load a web page within a Mobile Application. It behaves as a standard web browser and is useful to load untrusted content without risking your application's security.

The `OSIABEngine` structure provides the main features of the Library, which are 3 different ways to open a URL:
- using an External Browser;
- using a System Browser;
- using a Web View.

Each is detailed in the following sections.

## Index

- [Motivation](#motivation)
- [Usage](#usage)
- [Methods](#methods)
    - [Open a URL in an External Browser](#open-a-url-in-an-external-browser)
    - [Open a URL in a System Browser](#open-a-url-in-a-system-browser)
    - [Open a URL in a Web View](#open-a-url-in-a-web-view)

## Motivation

This library is to be used by the InAppBrowser Plugin for [OutSystems' Cordova Plugin](https://github.com/OutSystems/cordova-outsystems-inappbrowser) and [Ionic's Capacitor Plugin](https://github.com/ionic-team/capacitor-os-inappbrowser). 

## Usage

The library is available on CocoaPods as `OSInAppBrowserLib`. The following is an example of how to insert it into a Cordova plugin (through the `plugin.xml` file).

```xml
<podspec>
    <config>
        <source url="https://cdn.cocoapods.org/"/>
    </config>
    <pods use-frameworks="true">
        ...
        <pod name="OSInAppBrowserLib" spec="${version to use}" />
        ...
    </pods>
</podspec>
```

It can also be included as a dependency on other podspecs.

```ruby
Pod::Spec.new do |s|
  ...
  s.dependency 'OSInAppBrowserLib', '${version to use}'
  ...
end
```

## Methods

As mentioned before, the library offers the `OSIABEngine` structure that provides the following methods to interact with:

### Open a URL in an External Browser

```swift
func openExternalBrowser(_ url: URL, routerDelegate: ExternalBrowser, _ completionHandler: @escaping (ExternalBrowser.ReturnType) -> Void)
```

Uses the parameter `routerDelegate` - an object that offers an External Browser interface - to open the parameter `url`. The method is composed of the following input parameters:
- **url**: the URL for the web page to be opened.
- **routerDelegate**: The External Browser interface that will open the URL. Its return type should be `Bool`.
- **completionHandler**: The callback with the result of opening the URL with the External Browser interface.

### Open a URL in a System Browser

```swift
func openSystemBrowser(_ url: URL, routerDelegate: SystemBrowser, _ completionHandler: @escaping (SystemBrowser.ReturnType) -> Void)
```

Uses the parameter `routerDelegate` - an object that offers a System Browser interface - to open the parameter `url`. The method is composed of the following input parameters:
- **url**: the URL for the web page to be opened.
- **routerDelegate**: The System Browser interface that will open the URL. Its return type should be `UIViewController` or a subclass. The library provides an `OSIABSafariViewControllerRouterAdapter` class that uses `SFSafariViewController` to open it. 
- **completionHandler**: The callback with the result of opening the URL with the System Browser interface.

### Open a URL in a Web View

```swift
func openWebView(_ url: URL, routerDelegate: WebView, _ completionHandler: @escaping (WebView.ReturnType) -> Void)
```

Uses the parameter `routerDelegate` - an object that offers a Web View interface - to open the parameter `url`. The method is composed of the following input parameters:
- **url**: the URL for the web page to be opened.
- **routerDelegate**: The Web View interface that will open the URL. Its return type should be `UIViewController` or a subclass. The library provides an `OSIABWebViewRouterAdapter` class that uses `WKWebView` to open it. 
- **completionHandler**: The callback with the result of opening the URL with the Web View interface.
