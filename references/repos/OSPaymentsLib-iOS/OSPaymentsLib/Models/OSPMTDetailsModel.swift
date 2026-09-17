import PassKit

/// Status of the final value to be charged.
enum OSPMTStatus: String, Codable {
    case final
    case pending
}

/// Structure of the shipping/billing information
struct OSPMTContact: Decodable {
    let isCustom: Bool
    let contactArray: [String]?
    
    /// Keys used to encode and decode the model.
    enum CodingKeys: String, CodingKey {
        case isCustom
        case contactArray = "contactInfo"
    }
    
    /// Constructor method
    /// - Parameters:
    ///   - isCustom: Indicates if the custom contact information should be used when trigger a payment request.
    ///   - contactArray: Shipping/Billing properties required for filling.
    init(isCustom: Bool, contactArray: [String]?) {
        self.isCustom = isCustom
        self.contactArray = contactArray
    }
    
    /// Creates a new instance by decoding from the given decoder.
    ///
    /// This initializer throws an error if reading from the decoder fails, or
    /// if the data read is corrupted or otherwise invalid.
    ///
    /// - Parameter decoder: The decoder to read data from.
    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        let isCustom = try container.decode(Bool.self, forKey: .isCustom)
        let contactArray = isCustom ? try container.decodeIfPresent([String].self, forKey: .contactArray) : nil
        self.init(isCustom: isCustom, contactArray: contactArray)
    }
}

/// Payment Details to be processed.
struct OSPMTDetailsModel: Decodable {
    let amount: Decimal
    let currency: String
    let status: OSPMTStatus
    let shippingContact: OSPMTContact
    let billingContact: OSPMTContact
    let gateway: OSPMTGateway?
    
    /// Keys used to encode and decode the model.
    enum CodingKeys: String, CodingKey {
        case amount
        case currency
        case status
        case shippingContact = "shippingContacts"
        case billingContact = "billingContacts"
        case gateway = "psp"
    }
    
    /// Constructor method.
    /// - Parameters:
    ///   - amount: Amount to be charged.
    ///   - currency: The three-letter ISO 4217 currency code.
    ///   - status: Final value status.
    ///   - shippingContact: Shipping properties required for filling.
    ///   - billingContact: Billiing properties required for filling.
    ///   - gateway: Payment service provided to be used for processing, if passed.
    init(amount: Decimal, currency: String, status: OSPMTStatus, shippingContact: OSPMTContact, billingContact: OSPMTContact, gateway: String? = nil) {
        self.amount = amount
        self.currency = currency
        self.status = status
        self.shippingContact = shippingContact
        self.billingContact = billingContact
        self.gateway = OSPMTGateway.convert(from: gateway)
    }
    
    /// Creates a new instance by decoding from the given decoder.
    ///
    /// This initializer throws an error if reading from the decoder fails, or
    /// if the data read is corrupted or otherwise invalid.
    ///
    /// - Parameter decoder: The decoder to read data from.
    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        let amount = try container.decode(Decimal.self, forKey: .amount)
        let currency = try container.decode(String.self, forKey: .currency)
        let status = try container.decode(OSPMTStatus.self, forKey: .status)
        let shippingContact = try container.decode(OSPMTContact.self, forKey: .shippingContact)
        let billingContact = try container.decode(OSPMTContact.self, forKey: .billingContact)
        let gateway = try container.decodeIfPresent(String.self, forKey: .gateway)
        self.init(
            amount: amount, currency: currency, status: status, shippingContact: shippingContact, billingContact: billingContact, gateway: gateway
        )
    }
}

// MARK: Apple Pay extension
extension OSPMTDetailsModel {
    var paymentAmount: NSDecimalNumber {
        NSDecimalNumber(decimal: self.amount)
    }
    
    var paymentSummaryItemType: PKPaymentSummaryItemType {
        self.status == .pending ? .pending : .final
    }
}
