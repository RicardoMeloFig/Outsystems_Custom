import Foundation

/// Structure responsible for creating a Wrapper for the configured Gateway.
struct OSPMTGatewayFactory {
    /// Creates the correct wrapper for the gateway the user has configured.
    /// - Parameter configuration: Model with the gateway configuration information.
    /// - Returns: The wrapper object is everything is correctly configured. `nil` is returned otherwise.
    static func createWrapper(for configuration: OSPMTGatewayModel) -> OSPMTGatewayDelegate? {
        guard let gateway = configuration.gatewayEnum, let url = URL(string: configuration.requestURL) else { return nil }
        let urlRequest = URLRequest(url: url)
        
        switch gateway {
        case .stripe:
            guard let publishableKey = configuration.publishableKey else { return nil }
            return OSPMTStripeWrapper(urlRequest: urlRequest, publishableKey: publishableKey)
        }
    }
}
