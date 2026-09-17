/// Protocol that provides the server actions the plugin provides.
public protocol OSPMTActionDelegate: AnyObject {
    /// Sets up the payment configuration.
    func setupConfiguration()
    
    /// Verifies the device is ready to process a payment, considering the configuration provided before.
    func checkWalletSetup()
    
    /// Sets payment details and triggers the request proccess.
    /// - Parameters:
    ///   - details: Payment details model serialized into a text field.
    ///   - accessToken: Authorisation token related with a full payment type.
    func set(_ details: String, and accessToken: String?)
}

public extension OSPMTActionDelegate {
    /// Sets payment details and triggers the request proccess. This uses the default method without the `accessToken` parameter.
    /// - Parameter details: Payment details model serialized into a text field.
    func set(_ details: String) {
        self.set(details, and: nil)
    }
}
