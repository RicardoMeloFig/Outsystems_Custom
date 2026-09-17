import Foundation

/// Class that provides the bridge between the library and 3rd party consumers.
public class OSPMTPayments: NSObject {
    private weak var delegate: OSPMTCallbackDelegate?
    private let handler: OSPMTHandlerDelegate
    
    /// Constructor method.
    /// - Parameters:
    ///   - delegate: Handles the asynchronous return calls.
    ///   - handler: Payment service handler.
    init(delegate: OSPMTCallbackDelegate, handler: OSPMTHandlerDelegate) {
        self.delegate = delegate
        self.handler = handler
    }
    
    /// Convenience method for Apple Pay.
    /// - Parameters:
    ///   - delegate: Handles the asynchronous return calls.
    ///   - configurationSource: Configuration source, containing all values needed to configure.
    public convenience init(applePayWithDelegate delegate: OSPMTCallbackDelegate, andConfiguration configurationSource: OSPMTConfiguration = Bundle.main.infoDictionary!) {
        let applePayHandler = OSPMTApplePayHandler(configurationSource: configurationSource)
        self.init(delegate: delegate, handler: applePayHandler)
    }
}

// MARK: - Action Methods to be called by Bridge
extension OSPMTPayments: OSPMTActionDelegate {
    /// Sets up the payment configuration.
    public func setupConfiguration() {
        let result = self.handler.setupConfiguration()
        
        switch result {
        case .success(let message):
            self.delegate?.callback(result: message)
        case .failure(let error):
            self.delegate?.callback(error: error)
        }
    }
    
    /// Verifies the device is ready to process a payment, considering the configuration provided before.
    public func checkWalletSetup() {
        if let error = self.handler.checkWalletAvailability() {
            self.delegate?.callback(error: error)
        } else {
            self.delegate?.callbackSuccess()
        }
    }
    
    /// Sets payment details and triggers the request process.
    /// - Parameters:
    ///   - details: Payment details model serialized into a text field.
    ///   - accessToken: Authorisation token related with a full payment type.
    public func set(_ details: String, and accessToken: String?) {
        let detailsResult = self.decode(details)
        switch detailsResult {
        case .success(let detailsModel):
            self.handler.set(detailsModel, and: accessToken) { [weak self] result in
                guard let self = self else { return }
                switch result {
                case .success(let scopeModel):
                    let scopeResult = self.encode(scopeModel)
                    switch scopeResult {
                    case .success(let scopeText):
                        self.delegate?.callback(result: scopeText)
                    case .failure(let error):
                        self.delegate?.callback(error: error)
                    }
                case .failure(let error):
                    self.delegate?.callback(error: error)
                }
            }
        case .failure(let error):
            self.delegate?.callback(error: error)
        }
    }
}

extension OSPMTPayments {
    /// Decodes the payment details information text into an `OSPMTDetailsModel` instance. If not successful, returns an error
    /// - Parameter detailsText: Payment details information text.
    /// - Returns: The resulting `OSPMTDetailsModel` or error.
    func decode(_ detailsText: String) -> Result<OSPMTDetailsModel, OSPMTError> {
        guard
            let detailsData = detailsText.data(using: .utf8),
            let detailsModel = try? JSONDecoder().decode(OSPMTDetailsModel.self, from: detailsData)
        else { return .failure(.invalidDecodeDetails) }
        return .success(detailsModel)
    }
    
    /// Encondes the payment scope information model into text. If not successful, returns an error.
    /// - Parameter scopeModel: Payment scope information model.
    /// - Returns: The resulting text or error.
    func encode(_ scopeModel: OSPMTScopeModel) -> Result<String, OSPMTError> {
        guard let scopeData = try? JSONEncoder().encode(scopeModel), let scopeText = String(data: scopeData, encoding: .utf8)
        else { return .failure(.invalidEncodeScope) }
        return .success(scopeText)
    }
}
