import PassKit

/// Delegate class that triggers the backend payment process request.
protocol OSPMTGatewayDelegate: AnyObject {
    var urlRequest: URLRequest { get set }
    var urlSession: URLSession { get }
    
    /// Triggers the process through the configured gateway.
    /// - Parameters:
    ///   - payment: Apple Pay's payment request result.
    ///   - details: Payment details to trigger processing.
    ///   - accessToken: Authorisation token related with a full payment type.
    ///   - completion: Payment process result. If returns the process result in case of success or an error otherwise.
    func process(_ payment: PKPayment, with details: OSPMTDetailsModel, and accessToken: String, _ completion: @escaping (Result<OSPMTServiceProviderInfoModel, OSPMTError>) -> Void)
}

extension OSPMTGatewayDelegate {
    
    /// Triggers the backend url request.
    /// - Parameters:
    ///   - requestParameters: Model containing the request body to trigger
    ///   - accessToken: Authorisation token related with a full payment type.
    ///   - completion: Payment process result. If returns the process result in case of success or an error otherwise.
    func processURLRequest(_ requestParameters: OSPMTRequestParametersModel, and accessToken: String, _ completion: @escaping (Result<OSPMTServiceProviderInfoModel, OSPMTError>) -> Void) {
        self.urlRequest.httpMethod = "POST"
        self.urlRequest.setValue("application/json", forHTTPHeaderField: "Content-Type")
        self.urlRequest.setValue("application/json", forHTTPHeaderField: "Accept")
        self.urlRequest.setValue(accessToken, forHTTPHeaderField: "Payments-Token")
        
        self.urlRequest.httpBody = try? JSONEncoder().encode(requestParameters)
        let task = self.urlSession.dataTask(with: self.urlRequest) { data, response, error in
            guard let response = response as? HTTPURLResponse,
                  response.statusCode == 200,
                  error == nil,
                  let data = data,
                  let responseModel = try? JSONDecoder().decode(OSPMTServiceProviderInfoModel.self, from: data),
                  responseModel.status != .fail
                else {
                completion(.failure(.paymentIssue))
                return
            }

            completion(.success(responseModel))
        }
        task.resume()
    }
}
