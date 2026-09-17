/// Provides an interface between the business logic and the iOS platform. It can be used to create custom integrations for specific mobile frameworks (Cordova, Flutter, ...).
public protocol PlatformProtocol {
    /// Processes the  (un)successful results and sends a response back to its.
    /// - Parameters:
    ///   - result: The successful result to process. `nil` value corresponds to an unsuccessful processing.
    ///   - error: The unsuccessful result to process. `nil` value corresponds to a successful processing.
    ///   - callBackID: Identifier of the caller class that trigger the process.
    func sendResult(result: String?, error: NSError?, callBackID: String)
    
    // - MARK: optional methods
    /// Triggers an event on the caller's webview.
    /// - Parameters:
    ///   - event: Event to be triggered.
    ///   - data: Data required to trigger the event.
    func trigger(event: String, data: String)
}

/// The extensions provides a default implementation for the optional methods.
public extension PlatformProtocol {
    func trigger(event: String, data: String) {}
}
