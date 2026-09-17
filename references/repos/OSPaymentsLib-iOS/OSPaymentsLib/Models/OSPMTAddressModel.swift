/// Address Information related to a Billing or Shipping Information
struct OSPMTAddressModel: Codable, Equatable {
    let postalCode: String
    let fullAddress: String
    let countryCode: String
    let city: String
    let administrativeArea: String?
    let state: String?
    
    /// Keys used to encode and decode the model.
    enum CodingKeys: String, CodingKey {
        case postalCode, fullAddress, countryCode, city, administrativeArea, state
    }
    
    /// Constructor method
    /// - Parameters:
    ///   - postalCode: Zip code.
    ///   - fullAddress: Text containing full address (e.g. street, door number, floor, ...)
    ///   - countryCode: ISO 3166-1 country code representation of the country.
    ///   - city: City.
    ///   - administrativeArea: Administrative Area, if existing.
    ///   - state: State, if existing.
    init(postalCode: String, fullAddress: String, countryCode: String, city: String, administrativeArea: String? = nil, state: String? = nil) {
        self.postalCode = postalCode
        self.fullAddress = fullAddress
        self.countryCode = countryCode
        self.city = city
        self.administrativeArea = administrativeArea
        self.state = state
    }
    
    /// Creates a new instance by decoding from the given decoder.
    ///
    /// This initializer throws an error if reading from the decoder fails, or
    /// if the data read is corrupted or otherwise invalid.
    ///
    /// - Parameter decoder: The decoder to read data from.
    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        let postalCode = try container.decode(String.self, forKey: .postalCode)
        let fullAddress = try container.decode(String.self, forKey: .fullAddress)
        let countryCode = try container.decode(String.self, forKey: .countryCode)
        let city = try container.decode(String.self, forKey: .city)
        let administrativeArea = try container.decodeIfPresent(String.self, forKey: .administrativeArea)
        let state = try container.decodeIfPresent(String.self, forKey: .state)
        self.init(
            postalCode: postalCode,
            fullAddress: fullAddress,
            countryCode: countryCode,
            city: city,
            administrativeArea: administrativeArea,
            state: state
        )
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
        try container.encode(postalCode, forKey: .postalCode)
        try container.encode(fullAddress, forKey: .fullAddress)
        try container.encode(countryCode, forKey: .countryCode)
        try container.encode(city, forKey: .city)
        try container.encodeIfPresent(administrativeArea, forKey: .administrativeArea)
        try container.encodeIfPresent(state, forKey: .state)
    }
}
