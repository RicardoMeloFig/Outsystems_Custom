typealias OSPMTCompletionHandler = (Result<OSPMTScopeModel, OSPMTError>) -> Void

/// Protocol that provides all methods a Payment Service handler should implement.
protocol OSPMTHandlerDelegate: AnyObject {
    /// Allows the configuration of the payment service.
    /// - Returns: Returns a JSON mapping if successful or an error if anything failed.
    func setupConfiguration() -> Result<String, OSPMTError>
    
    /// Checks for the Wallet and Payment availability.
    /// - Returns: Returns `nil` if successful or an error otherwise.
    func checkWalletAvailability() -> OSPMTError?
    
    /// Sets Payment details and triggers its processing.
    /// - Parameters:
    ///   - detailsModel: payment details information.
    ///   - accessToken: Authorisation token related with a full payment type.
    ///   - completion: an async closure that can return a successful Payment Scope Model or an error otherwise.
    func set(_ detailsModel: OSPMTDetailsModel, and accessToken: String?, _ completion: @escaping OSPMTCompletionHandler)
}

extension OSPMTHandlerDelegate {
    /// Sets Payment details and triggers its processing. It uses the default `set` method without the `accessToken` parameter.
    /// - Parameters:
    ///   - detailsModel: payment details information.
    ///   - completion: an async closure that can return a successful Payment Scope Model or an error otherwise.
    func set(_ detailsModel: OSPMTDetailsModel, _ completion: @escaping OSPMTCompletionHandler) {
        self.set(detailsModel, and: nil, completion)
    }
}
