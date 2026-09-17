/// Payment Service Provider enum object
enum OSPMTGateway: String {
    case stripe = "Stripe"
}

extension OSPMTGateway {
    
    /// Converts a string into a `OSPMTGateway` object.
    /// - Parameter text: Text to convert.
    /// - Returns: A `OSPMTGateway` enum object if successful. `nil` is returned in case of error.
    static func convert(from text: String?) -> OSPMTGateway? {
        guard let text = text, text.lowercased() == "stripe" else { return nil }
        return .stripe
    }
}
