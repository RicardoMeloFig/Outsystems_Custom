/// Token Information Model
struct OSPMTTokenInfoModel: Codable, Equatable {
    let token: String
    let type: String
    
    /// Keys used to encode and decode the model.
    enum CodingKeys: String, CodingKey {
        case token, type
    }
    
    /// Constructor method.
    /// - Parameters:
    ///   - token: Token data text.
    ///   - type: Payment gateway (e.g. Payment Service Provider name).
    init(token: String, type: String) {
        self.token = token
        self.type = type
    }
    
    /// Creates a new instance by decoding from the given decoder.
    ///
    /// This initializer throws an error if reading from the decoder fails, or
    /// if the data read is corrupted or otherwise invalid.
    ///
    /// - Parameter decoder: The decoder to read data from.
    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        let token = try container.decode(String.self, forKey: .token)
        let type = try container.decode(String.self, forKey: .type)
        self.init(token: token, type: type)
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
        try container.encode(token, forKey: .token)
        try container.encode(type, forKey: .type)
    }
}
