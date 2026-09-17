import PassKit

// MARK: - OSPMTWalletAvailabilityDelegate Protocol Methods
/// Protocol that is responsible to check if a Wallet is available to be used by the plugin.
protocol OSPMTWalletAvailabilityDelegate: AnyObject {
    /// Verifies if the wallet is available for usage.
    /// - Returns: A boolean indicating if the wallet is available.
    static func isWalletAvailable() -> Bool
}

// MARK: - OSPMTSetupAvailabilityDelegate Protocol and Related Methods
/// Protocol that is responsible to check if a Payment is available to be used by the plugin.
protocol OSPMTSetupAvailabilityDelegate: AnyObject {
    /// Verifies if a payment is available for usage.
    /// - Returns: A boolean indicating if a payment is available.
    static func isPaymentAvailable() -> Bool
}

/// Protocol that enhances the validation done by `OSPMTSetupAvailabilityDelegate` protocol and verifies Apple Pay availability.
protocol OSPMTApplePaySetupAvailabilityDelegate: OSPMTSetupAvailabilityDelegate {
    /// Verifies if a payment is available for usage, given the passed payment networks and marchant capabilities
    /// - Parameters:
    ///   - networks: Array of payment networks available by a merchant
    ///   - merchantCapabilities: Bit set containing the payment capabilities available by a merchant.
    /// - Returns: A boolean indicating if the payment is available.
    static func isPaymentAvailable(using networks: [PKPaymentNetwork]?, and merchantCapabilities: PKMerchantCapability?) -> Bool
}

// MARK: - OSPMTAvailabilityDelegate Protocol Methods
/// Protocol that is responsible to verify if wallet and payment are ready to trigger a payment process.
protocol OSPMTAvailabilityDelegate: AnyObject {
    /// Verifies if the wallet is ready to be used.
    /// - Returns: A boolean indicating if it's ready or not.
    func checkWallet() -> OSPMTError?
    
    /// Verifies if a payment request can be triggered.
    /// - Parameter shouldVerifySetup: Indicates whether the plugin should also check its configuration properties.
    /// - Returns: A boolean indicating if it's ready or not
    func checkPayment(shouldVerifySetup: Bool) -> OSPMTError?
}

extension OSPMTAvailabilityDelegate {
    /// Verifies if a payment request can be triggered. It's the same as calling `checkPayment(shouldVerifySetup:)` with a `false` parameter.
    /// - Returns: A boolean indicating if it's ready or not.
    func checkPaymentAvailability() -> OSPMTError? {
        self.checkPayment(shouldVerifySetup: false)
    }
    
    /// Verifies if a payment request can be triggere, regarding its configuration properties. It's the same as calling `checkPayment(shouldVerifySetup:)` with a `true` parameter.
    /// - Returns: A boolean indicating if it's ready or not.
    func checkPaymentAvailabilityWithSetup() -> OSPMTError? {
        self.checkPayment(shouldVerifySetup: true)
    }
}

// MARK: - OSPMTApplePayAvailabilityBehaviour Implementation Methods
/// Class responsible for verifying if Apple Wallet and Apple Pay are available and ready to be used. Apple Pay is also check against it's payment network and merchant capabilities.
class OSPMTApplePayAvailabilityBehaviour: OSPMTAvailabilityDelegate {
    let configuration: OSPMTApplePayConfiguration
    let walletAvailableBehaviour: OSPMTWalletAvailabilityDelegate.Type
    let setupAvailableBehaviour: OSPMTApplePaySetupAvailabilityDelegate.Type
    
    /// Constructor method.
    /// - Parameters:
    ///   - configuration: Apple Pay Configuration.
    ///   - walletAvailableBehaviour: Implementation of the protocol that verifies Apple Wallet availability.
    ///   - setupAvailableBehaviour: Implementation of the protocol that verifies Apple Pay payment availability.
    init(configuration: OSPMTApplePayConfiguration, walletAvailableBehaviour: OSPMTWalletAvailabilityDelegate.Type = PKPassLibrary.self, setupAvailableBehaviour: OSPMTApplePaySetupAvailabilityDelegate.Type = PKPaymentAuthorizationController.self) {
        self.configuration = configuration
        self.walletAvailableBehaviour = walletAvailableBehaviour
        self.setupAvailableBehaviour = setupAvailableBehaviour
    }
    
    /// Verifies if the wallet is ready to be used.
    /// - Returns: A boolean indicating if it's ready or not.
    func checkWallet() -> OSPMTError? {
        return !self.walletAvailableBehaviour.isWalletAvailable() ? .walletNotAvailable : nil
    }
    
    /// Verifies if a payment request can be triggered.
    /// - Parameter shouldVerifySetup: Indicates whether the plugin should also check its configuration properties.
    /// - Returns: A boolean indicating if it's ready or not
    func checkPayment(shouldVerifySetup: Bool) -> OSPMTError? {
        var error: OSPMTError?
        
        if shouldVerifySetup {
            if !self.setupAvailableBehaviour.isPaymentAvailable(
                using: self.configuration.supportedNetworks, and: self.configuration.merchantCapabilities
            ) {
                error = .setupPaymentNotAvailable
            }
        } else if !self.setupAvailableBehaviour.isPaymentAvailable() {
            error = .paymentNotAvailable
        }
        
        return error
    }
}
