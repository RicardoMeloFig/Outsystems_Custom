/// Model to manage the gateway payment process' request parameters.
class OSPMTRequestParametersModel: Encodable {
    let amount: Int
    let currency: String
    
    /// Keys used to encode and decode the model.
    enum CodingKeys: String, CodingKey {
        case amount
        case currency
    }
    
    /// Constructor method.
    /// - Parameters:
    ///   - amount: Amount to charge.
    ///   - currency: Currency to charge.
    init(amount: Int, currency: String) {
        self.amount = amount
        self.currency = currency
    }
    
    /// Encodes this value into the given encoder.
    ///
    /// If the value fails to encode anything, `encoder` will encode an empty
    /// keyed container in its place.
    ///
    /// This function throws an error if any values are invalid for the given
    /// encoder's format.
    ///
    /// - Parameter encoder: The encoder to write data to.
    func encode(to encoder: Encoder) throws {
        var container = encoder.container(keyedBy: CodingKeys.self)
        
        try container.encode(amount, forKey: .amount)
        try container.encode(currency, forKey: .currency)
    }
}
