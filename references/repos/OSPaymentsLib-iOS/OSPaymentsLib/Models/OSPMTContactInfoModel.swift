/// Information related to Billing or Shipping
struct OSPMTContactInfoModel: Codable {
    let address: OSPMTAddressModel?
    let phoneNumber: String?
    let name: String?
    let email: String?
    
    /// Keys used to encode and decode the model.
    enum CodingKeys: String, CodingKey {
        case address, phoneNumber, name, email
    }
    
    /// Constructor method.
    /// - Parameters:
    ///   - address: Address information, if existing.
    ///   - phoneNumber: Phone number information, if existing.
    ///   - name: Given and family name, if existing.
    ///   - email: Email address, if existing.
    init(address: OSPMTAddressModel? = nil, phoneNumber: String? = nil, name: String? = nil, email: String? = nil) {
        self.address = address
        self.phoneNumber = phoneNumber
        self.name = name
        self.email = email
    }
    
    /// Creates a new instance by decoding from the given decoder.
    ///
    /// This initializer throws an error if reading from the decoder fails, or
    /// if the data read is corrupted or otherwise invalid.
    ///
    /// - Parameter decoder: The decoder to read data from.
    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        let address = try container.decodeIfPresent(OSPMTAddressModel.self, forKey: .address)
        let phoneNumber = try container.decodeIfPresent(String.self, forKey: .phoneNumber)
        let name = try container.decodeIfPresent(String.self, forKey: .name)
        let email = try container.decodeIfPresent(String.self, forKey: .email)
        self.init(address: address, phoneNumber: phoneNumber, name: name, email: email)
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
        try container.encodeIfPresent(address, forKey: .address)
        try container.encodeIfPresent(phoneNumber, forKey: .phoneNumber)
        try container.encodeIfPresent(name, forKey: .name)
        try container.encodeIfPresent(email, forKey: .email)
    }
}

extension OSPMTContactInfoModel: Equatable {
    /// Returns a Boolean value indicating whether two values are equal.
    ///
    /// Equality is the inverse of inequality. For any values `a` and `b`,
    /// `a == b` implies that `a != b` is `false`.
    ///
    /// - Parameters:
    ///   - lhs: A value to compare.
    ///   - rhs: Another value to compare.
    /// - Returns: The comparison result.
    static func == (lhs: OSPMTContactInfoModel, rhs: OSPMTContactInfoModel) -> Bool {
        lhs.address == rhs.address && lhs.phoneNumber == rhs.phoneNumber && lhs.name == rhs.name && lhs.email == rhs.email
    }
}
