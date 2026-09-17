import PassKit

// MARK: - OSPMTRequestTriggerDelegate Protocol and Related Methods
typealias OSPMTRequestTriggerCompletion = ((Bool) -> Void)

/// Protocol that is responsible to trigger a payment request..
protocol OSPMTRequestTriggerDelegate: AnyObject {
    /// Triggers a payment request. The result is processed asyncrhonously and returned by the `completion` parameters.
    /// - Parameter completion: Block that returns the success of the payment request operation.
    func triggerPayment(_ completion: @escaping OSPMTRequestTriggerCompletion)
}

/// Protocol that enhances the validation done by `OSPMTRequestTriggerDelegate` protocol and triggers a payment request for Apple Pay..
protocol OSPMTApplePayRequestTriggerDelegate: OSPMTRequestTriggerDelegate {
    /// Creates an object responsible for dealing with the payment request process, delegating the details to the passed parameter.
    /// - Parameters:
    ///   - detailsModel: Payment details.
    ///   - delegate: The object responsible for the request process' response.
    /// - Returns: An instance of the object or an error, it the instatiation fails.
    static func createRequestTriggerBehaviour(for detailsModel: OSPMTDetailsModel, andDelegate delegate: OSPMTApplePayRequestBehaviour?) -> Result<OSPMTApplePayRequestTriggerDelegate, OSPMTError>
}

/// Protocol that manages the process request, in order to process a payment transaction.
protocol OSPMTRequestDelegate: AnyObject {
    /// Sets Payment details and triggers its processing.
    /// - Parameters:
    ///   - detailsModel: payment details information.
    ///   - accessToken: Authorisation token related with a full payment type.
    ///   - completion: an async closure that can return a successful Payment Scope Model or an error otherwise.
    func trigger(with detailsModel: OSPMTDetailsModel, and accessToken: String?, _ completion: @escaping OSPMTCompletionHandler)
}

extension OSPMTRequestDelegate {
    /// Sets Payment details and triggers its processing. It uses the default method without the `accessToken` parameter.
    /// - Parameters:
    ///   - detailsModel: payment details information.
    ///   - completion: an async closure that can return a successful Payment Scope Model or an error otherwise.
    func trigger(with detailsModel: OSPMTDetailsModel, _ completion: @escaping OSPMTCompletionHandler) {
        self.trigger(with: detailsModel, and: nil, completion)
    }
}

/// Class that implements the `OSPMTRequestDelegate` for Apple Pay, providing it the required that details to work.
class OSPMTApplePayRequestBehaviour: NSObject, OSPMTRequestDelegate {
    let configuration: OSPMTApplePayConfiguration
    var requestTriggerType: OSPMTApplePayRequestTriggerDelegate.Type
    
    var paymentStatus: PKPaymentAuthorizationStatus = .failure
    var paymentScope: OSPMTScopeModel?
    var paymentDetails: OSPMTDetailsModel?
    var accessToken: String?
    var completionHandler: OSPMTCompletionHandler!
    
    /// Constructor method.
    /// - Parameters:
    ///   - configuration: Apple Pay configuration manager.
    ///   - requestTriggerType: Apple Pay request type, used to create a request trigger processor class.
    init(configuration: OSPMTApplePayConfiguration, requestTriggerType: OSPMTApplePayRequestTriggerDelegate.Type = PKPaymentAuthorizationController.self) {
        self.configuration = configuration
        self.requestTriggerType = requestTriggerType
    }
    
    /// Sets Payment details and triggers its processing.
    /// - Parameters:
    ///   - detailsModel: payment details information.
    ///   - accessToken: Authorisation token related with a full payment type.
    ///   - completion: an async closure that can return a successful Payment Scope Model or an error otherwise.
    func trigger(with detailsModel: OSPMTDetailsModel, and accessToken: String?, _ completion: @escaping OSPMTCompletionHandler) {
        self.paymentDetails = detailsModel
        self.accessToken = accessToken
        self.completionHandler = completion
        
        let result = self.requestTriggerType.createRequestTriggerBehaviour(for: detailsModel, andDelegate: self)
        switch result {
        case .success(let paymentController):
            paymentController.triggerPayment { [weak self] presented in
                guard let self = self else { return }
                
                self.paymentStatus = .failure
                if !presented {
                    completion(.failure(.paymentTriggerPresentationFailed))
                }
            }
        case .failure(let error):
            completion(.failure(error))
        }
    }
}

extension OSPMTApplePayRequestBehaviour {
    /// Converts payment details information into an Apple Pay `PKPaymentSummaryItem` array type.
    /// - Parameter detailsModel: Payment details information.
    /// - Returns: The corresponding value if success, nil otherwise.
    func getPaymentSummaryItems(for detailsModel: OSPMTDetailsModel) -> [PKPaymentSummaryItem]? {
        guard let label = self.configuration.merchantName else { return nil }
        return [PKPaymentSummaryItem(label: label, amount: detailsModel.paymentAmount, type: detailsModel.paymentSummaryItemType)]
    }
    
    /// Converts the shipping/billing contact information text into an Apple Pay `PKContactField` set type.
    /// - Parameter text: The billing/shipping contact information.
    /// - Returns: The corresponding value.
    func getContactFields(for text: [String]?) -> Set<PKContactField> {
        var result = [PKContactField]()
        if let text = text {
            result.append(contentsOf: text.compactMap(PKContactField.convert(from:)))
        }
        return Set(result)
    }
}

// MARK: - Set up PKPaymentAuthorizationControllerDelegate conformance
extension OSPMTApplePayRequestBehaviour: PKPaymentAuthorizationControllerDelegate {
    func paymentAuthorizationControllerDidFinish(_ controller: PKPaymentAuthorizationController) {
        controller.dismiss()
        // The payment sheet doesn't automatically dismiss once it has finished. Dismiss the payment sheet.
        DispatchQueue.main.async {
            if self.paymentStatus == .success, let paymentScope = self.paymentScope {
                self.completionHandler(.success(paymentScope))
            } else {
                self.completionHandler(.failure(.paymentCancelled))
            }
        }
    }
    
    func paymentAuthorizationController(_ controller: PKPaymentAuthorizationController, didAuthorizePayment payment: PKPayment, handler completion: @escaping (PKPaymentAuthorizationResult) -> Void) {
        func setPaymentResults(with errorArray: [OSPMTError], and scopeModel: OSPMTScopeModel?, _ completion: @escaping (PKPaymentAuthorizationResult) -> Void) {
            if errorArray.isEmpty, let scopeModel = scopeModel {
                self.paymentScope = scopeModel
                self.paymentStatus = .success
            } else {
                self.paymentScope = nil
                self.paymentStatus = .failure
            }
            
            completion(PKPaymentAuthorizationResult(status: self.paymentStatus, errors: errorArray))
        }
        
        if let paymentDetails = paymentDetails, paymentDetails.gateway != nil {
            guard let accessToken = self.accessToken, !accessToken.isEmpty else {
                return completion(PKPaymentAuthorizationResult(status: self.paymentStatus, errors: [OSPMTError.tokenIssue]))
            }
            
            guard let paymentGateway = self.configuration.gatewayModel, paymentGateway.gatewayEnum == paymentDetails.gateway else {
                return completion(PKPaymentAuthorizationResult(status: self.paymentStatus, errors: [OSPMTError.gatewayNotConfigured]))
            }
            
            guard let gatewayWrapper = OSPMTGatewayFactory.createWrapper(for: paymentGateway) else {
                return completion(PKPaymentAuthorizationResult(status: self.paymentStatus, errors: [OSPMTError.gatewaySetFailed]))
            }
            
            gatewayWrapper.process(payment, with: paymentDetails, and: accessToken) { result in
                var errorArray = [OSPMTError]()
                var paymentResultModel: OSPMTServiceProviderInfoModel?
                
                switch result {
                case .success(let result):
                    paymentResultModel = result
                case .failure(let error):
                    errorArray += [error]
                }
                
                setPaymentResults(with: errorArray, and: payment.createScopeModel(for: paymentResultModel), completion)
            }
        } else {
            setPaymentResults(with: [], and: payment.createScopeModel(), completion)
        }
    }
    
    /**
     Despite the need to implement the method, this is not required by app's functionality.
     For that reason, we're returning `nil` as the presentation window.
     */
    func presentationWindow(for controller: PKPaymentAuthorizationController) -> UIWindow? { nil }
}
