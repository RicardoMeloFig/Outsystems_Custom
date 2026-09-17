import PassKit
@_implementationOnly import StripePayments

/// Delegate class containing the required calls for Stripe's SDK to process.
protocol OSPMTStripeAPIDelegate: AnyObject {
    /// Sets the required publishable key, required to trigger payments through Stripe
    /// - Parameter publishableKey: Key obtained via Stripe's Dashboard.
    func set(_ publishableKey: String)
    
    /// Retrieves Stripe's Payment Method's Identifier in exchange for Apple Pay's payment request result.
    /// - Parameters:
    ///   - payment: Apple Pay's payment request result.
    ///   - completion: The exchange operation result. In case of success, it returns the Payment Method Id or an error otherwise.
    func getPaymentMethodId(from payment: PKPayment, _ completion: @escaping (Result<String, OSPMTError>) -> Void)
}

extension STPAPIClient: OSPMTStripeAPIDelegate {
    /// Sets the required publishable key, required to trigger payments through Stripe
    /// - Parameter publishableKey: Key obtained via Stripe's Dashboard.
    func set(_ publishableKey: String) {
        self.publishableKey = publishableKey
    }
    
    /// Retrieves Stripe's Payment Method's Identifier in exchange for Apple Pay's payment request result.
    /// - Parameters:
    ///   - payment: Apple Pay's payment request result.
    ///   - completion: The exchange operation result. In case of success, it returns the Payment Method Id or an error otherwise.
    func getPaymentMethodId(from payment: PKPayment, _ completion: @escaping (Result<String, OSPMTError>) -> Void) {
        self.createPaymentMethod(with: payment) { paymentMethod, _ in
            if let paymentMethod = paymentMethod {
                completion(.success(paymentMethod.stripeId))
            } else {
                completion(.failure(.stripePaymentMethodCreation))
            }
        }
    }
}
