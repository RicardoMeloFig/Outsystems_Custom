# Swift protocol for stack agnostic plugin implementation

## Index

<!-- MarkdownTOC levels="2" autolink="true" -->

- [Motivation](#motivation)
- [Supported Platforms](#supported-platforms)
- [Usage](#usage)
- [Methods](#methods)

<!-- /MarkdownTOC -->

## Motivation

This repository contains a protocol that interfaces business logic with iOS platform.

Developers are able to create their own integrations of `PlatformProtocol` for their target mobile framework.
<div align="center">
    <img src="/assets/dependencies.png">
</div>

This structure allows us to develop native plugins, portable between techonological stacks (ie: Cordova, React-native, Flutter...)

## Supported Platforms
- iOS
 
## Usage

1. Include the `POD_NAME` pod in your project.

2. Create an extension that implements `PlatformProtocol`.
```swift
extension CordovaInterface: PlatformProtocol {
    func sendResult(result: String?, error: NSError?, callBackID: String) {
        // ... processes and sends a response back to Cordova
    }
    
    func trigger(event: String, data: String) {
        // ... triggers an event on the Cordova webview 
    }
}
```

## Methods

- `sendResult(result: String?, error: NSError?, callBackID: String)` will send messages back to the mobile framework.

- `trigger(event: String, data: String)` will trigger an event on the mobile framework's webview.
