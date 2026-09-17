import PassKit

extension PKPaymentAuthorizationController: OSPMTApplePaySetupAvailabilityDelegate {
    /// Verifies if a payment is available for usage.
    /// - Returns: A boolean indicating if a payment is available.
    static func isPaymentAvailable() -> Bool {
        Self.canMakePayments()
    }
    
    /// Verifies if a payment is available for usage, given the passed payment networks and marchant capabilities
    /// - Parameters:
    ///   - networks: Array of payment networks available by a merchant
    ///   - merchantCapabilities: Bit set containing the payment capabilities available by a merchant.
    /// - Returns: A boolean indicating if the payment is available.
    static func isPaymentAvailable(using networks: [PKPaymentNetwork]?, and merchantCapabilities: PKMerchantCapability?) -> Bool {
        guard let networks = networks, let merchantCapabilities = merchantCapabilities else { return false }
        return Self.canMakePayments(usingNetworks: networks, capabilities: merchantCapabilities)
    }
}

extension PKPaymentAuthorizationController: OSPMTApplePayRequestTriggerDelegate {
    /// Triggers a payment request. The result is processed asyncrhonously and returned by the `completion` parameters.
    /// - Parameter completion: Block that returns the success of the payment request operation.
    func triggerPayment(_ completion: @escaping OSPMTRequestTriggerCompletion) {
        self.present(completion: completion)
    }
    
    /// Creates an object responsible for dealing with the payment request process, delegating the details to the passed parameter.
    /// - Parameters:
    ///   - detailsModel: Payment details.
    ///   - delegate: The object responsible for the request process' response.
    /// - Returns: An instance of the object or an error, it the instatiation fails.
    static func createRequestTriggerBehaviour(for detailsModel: OSPMTDetailsModel, andDelegate delegate: OSPMTApplePayRequestBehaviour?) -> Result<OSPMTApplePayRequestTriggerDelegate, OSPMTError> {
        guard
            let delegate = delegate,
            let merchantIdentifier = delegate.configuration.merchantID,
            let countryCode = delegate.configuration.merchantCountryCode,
            let merchantCapabilities = delegate.configuration.merchantCapabilities,
            let paymentSummaryItems = delegate.getPaymentSummaryItems(for: detailsModel),
            let supportedNetworks = delegate.configuration.supportedNetworks
        else { return .failure(.invalidConfiguration) }
        
        let paymentRequest = PKPaymentRequest()
        paymentRequest.merchantIdentifier = merchantIdentifier
        paymentRequest.countryCode = countryCode
        paymentRequest.currencyCode = detailsModel.currency
        paymentRequest.merchantCapabilities = merchantCapabilities
        paymentRequest.paymentSummaryItems = paymentSummaryItems
        paymentRequest.requiredBillingContactFields = delegate.getContactFields(
            for: detailsModel.billingContact.isCustom
            ? detailsModel.billingContact.contactArray
            : delegate.configuration.billingSupportedContacts
        )
        paymentRequest.requiredShippingContactFields = delegate.getContactFields(
            for: detailsModel.shippingContact.isCustom
            ? detailsModel.shippingContact.contactArray
            : delegate.configuration.shippingSupportedContacts
        )
        paymentRequest.supportedCountries = delegate.configuration.supportedCountries
        paymentRequest.supportedNetworks = supportedNetworks
        
        let paymentAuthorizationController = PKPaymentAuthorizationController(paymentRequest: paymentRequest)
        paymentAuthorizationController.delegate = delegate
        return .success(paymentAuthorizationController)
    }
}
