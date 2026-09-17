/// Delegate for the callback return calls for the plugin
public protocol OSPMTCallbackDelegate: AnyObject {
    func callback(result: String?, error: OSPMTError?)
}

// MARK: OSPMTCallbackProtocol Default Implementation
extension OSPMTCallbackDelegate {
    /// Triggers the callback when there's an error
    /// - Parameter error: Error to be thrown
    func callback(error: OSPMTError) {
        self.callback(result: nil, error: error)
    }
    
    /// Triggers the callback when there's a success text
    /// - Parameter result: Text to be returned
    func callback(result: String) {
        self.callback(result: result, error: nil)
    }
    
    /// Triggers the callback when there's a success without text
    func callbackSuccess() {
        self.callback(result: "")
    }
}
