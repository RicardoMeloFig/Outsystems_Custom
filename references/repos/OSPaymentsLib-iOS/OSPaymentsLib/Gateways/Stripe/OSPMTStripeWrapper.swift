import PassKit
@_implementationOnly import StripeCore

/// Object responsible for making a Stripe payment process. The Wrapper deals with all calls that are required to Stripe's SDK.
final class OSPMTStripeWrapper: OSPMTGatewayDelegate {
    var urlRequest: URLRequest
    var urlSession: URLSession
    let apiDelegate: OSPMTStripeAPIDelegate
    
    /// Constructor method.
    /// - Parameters:
    ///   - urlRequest: URL load request object.
    ///   - urlSession: Coordinator object for network data transfer tasks.
    ///   - publishableKey: Key required for Stripe's API to trigger calls.
    ///   - apiDelegate: Object responsible for Stripe's API calls.
    init(urlRequest: URLRequest, urlSession: URLSession = .shared, publishableKey: String, apiDelegate: OSPMTStripeAPIDelegate = STPAPIClient.shared) {
        self.urlRequest = urlRequest
        self.urlSession = urlSession
        
        apiDelegate.set(publishableKey)
        self.apiDelegate = apiDelegate
    }
}

extension OSPMTStripeWrapper {
    /// Triggers the process through the configured gateway.
    /// - Parameters:
    ///   - payment: Apple Pay's payment request result.
    ///   - details: Payment details to trigger processing.
    ///   - accessToken: Authorisation token related with a full payment type.
    ///   - completion: Payment process result. If returns the process result in case of success or an error otherwise.
    func process(_ payment: PKPayment, with details: OSPMTDetailsModel, and accessToken: String, _ completion: @escaping (Result<OSPMTServiceProviderInfoModel, OSPMTError>) -> Void) {
        self.apiDelegate.getPaymentMethodId(from: payment) { result in
            switch result {
            case .success(let paymentMethodId):
                let requestParametersModel = OSPMTStripeRequestParametersModel(
                    amount: details.paymentAmount.multiplying(by: 100).intValue, currency: details.currency, paymentMethodId: paymentMethodId
                )
                self.processURLRequest(requestParametersModel, and: accessToken, completion)
            case .failure(let error):
                completion(.failure(error))
            }
        }
    }
}
