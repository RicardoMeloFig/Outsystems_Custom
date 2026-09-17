/// Payment Scope Information
struct OSPMTScopeModel: Codable {
    let paymentData: OSPMTDataModel
    let shippingInfo: OSPMTContactInfoModel?
    
    /// Keys used to encode and decode the model.
    enum CodingKeys: String, CodingKey {
        case paymentData, shippingInfo
    }
    
    /// Construct method.
    /// - Parameters:
    ///   - paymentData: Payment Data Information.
    ///   - shippingInfo: Shipping Information Filled.
    init(paymentData: OSPMTDataModel, shippingInfo: OSPMTContactInfoModel? = nil) {
        self.paymentData = paymentData
        self.shippingInfo = shippingInfo
    }
    
    /// Creates a new instance by decoding from the given decoder.
    ///
    /// This initializer throws an error if reading from the decoder fails, or
    /// if the data read is corrupted or otherwise invalid.
    ///
    /// - Parameter decoder: The decoder to read data from.
    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        let paymentData = try container.decode(OSPMTDataModel.self, forKey: .paymentData)
        let shippingInfo = try container.decodeIfPresent(OSPMTContactInfoModel.self, forKey: .shippingInfo)
        self.init(paymentData: paymentData, shippingInfo: shippingInfo)
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
        try container.encode(paymentData, forKey: .paymentData)
        try container.encodeIfPresent(shippingInfo, forKey: .shippingInfo)
    }
}

extension OSPMTScopeModel: Equatable {
    /// Returns a Boolean value indicating whether two values are equal.
    ///
    /// Equality is the inverse of inequality. For any values `a` and `b`,
    /// `a == b` implies that `a != b` is `false`.
    ///
    /// - Parameters:
    ///   - lhs: A value to compare.
    ///   - rhs: Another value to compare.
    /// - Returns: The comparison result.
    static func == (lhs: OSPMTScopeModel, rhs: OSPMTScopeModel) -> Bool {
        lhs.paymentData == rhs.paymentData && lhs.shippingInfo == rhs.shippingInfo
    }
}
