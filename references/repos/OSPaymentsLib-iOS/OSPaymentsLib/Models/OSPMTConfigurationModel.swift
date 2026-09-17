import PassKit

/// Model that manages the Payment Service Provider's configuration.
struct OSPMTGatewayModel: Encodable {
    let gateway: String
    let publishableKey: String?
    let requestURL: String
    
    /// Keys used to encode and decode the model.
    enum CodingKeys: String, CodingKey {
        case gateway
        case publishableKey
        case requestURL
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
        
        try container.encode(gateway, forKey: .gateway)
        try container.encodeIfPresent(publishableKey, forKey: .publishableKey)
        try container.encode(requestURL, forKey: .requestURL)
    }
}

extension OSPMTGatewayModel {
    var gatewayEnum: OSPMTGateway? {
        return OSPMTGateway.convert(from: self.gateway)
    }
}

/// Model that contains all properties needed to configure a payment service.
class OSPMTConfigurationModel: Encodable {
    // MARK: Merchant Information
    var merchantID: String?
    var merchantName: String?
    var merchantCountryCode: String?
    
    // MARK: Payment Information
    var paymentAllowedNetworks: [String]?
    var paymentSupportedCapabilities: [String]?
    var paymentSupportedCardCountries: [String]?
    
    // MARK: Shipping Information
    var shippingSupportedContacts: [String]?
    
    // MARK: Billing Information
    var billingSupportedContacts: [String]?
    
    // MARK: Payment Service Provider Information
    var gatewayModel: OSPMTGatewayModel?
    
    /// Keys used to encode and decode the model.
    enum CodingKeys: String, CodingKey {
        case merchantID
        case merchantName
        case merchantCountryCode
        case paymentAllowedNetworks
        case paymentSupportedCapabilities
        case paymentSupportedCardCountries
        case shippingSupportedContacts
        case billingSupportedContacts
        case gatewayModel = "tokenization"
    }
    
    /// Constructor method.
    /// - Parameters:
    ///   - merchantID: Merchant ID configured
    ///   - merchantName: Merchant Name configured
    ///   - merchantCountryCode: Merchant Country Code configured
    ///   - paymentAllowedNetworks: Payment Allowed Networks configured
    ///   - paymentSupportedCapabilities: Payment Supported Capabilities configured
    ///   - paymentSupportedCardCountries: Payment Support Card Countries configured
    ///   - shippingSupportedContacts: Shipping Supported Contacts configured
    ///   - billingSupportedContacts: Billing Supported Contacts configured
    ///   - tokenization: Payment Service Gateway configured
    init(
        merchantID: String?,
        merchantName: String?,
        merchantCountryCode: String?,
        paymentAllowedNetworks: [String]?,
        paymentSupportedCapabilities: [String]?,
        paymentSupportedCardCountries: [String]?,
        shippingSupportedContacts: [String]?,
        billingSupportedContacts: [String]?,
        gatewayModel: OSPMTGatewayModel?
    ) {
        self.merchantID = merchantID
        self.merchantName = merchantName
        self.merchantCountryCode = merchantCountryCode
        self.paymentAllowedNetworks = paymentAllowedNetworks
        self.paymentSupportedCapabilities = paymentSupportedCapabilities
        self.paymentSupportedCardCountries = paymentSupportedCardCountries
        self.shippingSupportedContacts = shippingSupportedContacts
        self.billingSupportedContacts = billingSupportedContacts
        self.gatewayModel = gatewayModel
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
        
        // MARK: Merchant Information
        try container.encodeIfPresent(merchantID, forKey: .merchantID)
        try container.encodeIfPresent(merchantName, forKey: .merchantName)
        try container.encodeIfPresent(merchantCountryCode, forKey: .merchantCountryCode)
        
        // MARK: Payment Information
        try container.encodeIfPresent(paymentAllowedNetworks, forKey: .paymentAllowedNetworks)
        try container.encodeIfPresent(paymentSupportedCapabilities, forKey: .paymentSupportedCapabilities)
        try container.encodeIfPresent(paymentSupportedCardCountries, forKey: .paymentSupportedCardCountries)
        
        // MARK: Shipping Information
        try container.encodeIfPresent(shippingSupportedContacts, forKey: .shippingSupportedContacts)
        
        // MARK: Billing Information
        try container.encodeIfPresent(billingSupportedContacts, forKey: .billingSupportedContacts)
        
        // MARK: Payment Service Provider Information
        try container.encodeIfPresent(gatewayModel, forKey: .gatewayModel)
    }
}

public typealias OSPMTConfiguration = [String: Any]

/// Manages all configuration properties required to enable Apple Pay in the plugin.
class OSPMTApplePayConfiguration: OSPMTConfigurationModel {
    struct ConfigurationKeys {
        static let merchantID = "ApplePayMerchantID"
        static let merchantName = "ApplePayMerchantName"
        static let merchantCountryCode = "ApplePayMerchantCountryCode"
        
        static let paymentAllowedNetworks = "ApplePayPaymentAllowedNetworks"
        static let paymentSupportedCapabilities = "ApplePayPaymentSupportedCapabilities"
        static let paymentSupportedCardCountries = "ApplePayPaymentSupportedCardCountries"
        
        static let shippingSupportedContacts = "ApplePayShippingSupportedContacts"
        
        static let billingSupportedContacts = "ApplePayBillingSupportedContacts"
        
        static let paymentGateway = "ApplePayPaymentGateway"
        static let paymentGatewayName = "ApplePayPaymentGatewayName"
        static let paymentRequestURL = "ApplePayRequestURL"
        static let stripePublishableKey = "ApplePayStripePublishableKey"
    }
    
    /// Constructor method.
    /// - Parameter source: Source class contaning the configuration.
    convenience init(source: OSPMTConfiguration) {
        // MARK: Merchant Information
        let merchantID: String? = Self.getRequiredProperty(forSource: source, andKey: ConfigurationKeys.merchantID)
        let merchantName: String? = Self.getRequiredProperty(forSource: source, andKey: ConfigurationKeys.merchantName)
        let merchantCountryCode: String? = Self.getRequiredProperty(forSource: source, andKey: ConfigurationKeys.merchantCountryCode)
        
        // MARK: Payment Information
        let paymentAllowedNetworks: [String]? = Self.getRequiredProperty(forSource: source, andKey: ConfigurationKeys.paymentAllowedNetworks)
        let paymentSupportedCapabilities: [String]? = Self.getRequiredProperty(
            forSource: source, andKey: ConfigurationKeys.paymentSupportedCapabilities
        )
        let paymentSupportedCardCountries: [String]? = Self.getProperty(forSource: source, andKey: ConfigurationKeys.paymentSupportedCardCountries)
        
        // MARK: Shipping Information
        let shippingSupportedContacts: [String]? = Self.getProperty(forSource: source, andKey: ConfigurationKeys.shippingSupportedContacts)
        
        // MARK: Billing Information
        let billingSupportedContacts: [String]? = Self.getProperty(forSource: source, andKey: ConfigurationKeys.billingSupportedContacts)
        
        // MARK: Payment Service Provider Information
        var gatewayModel: OSPMTGatewayModel?
        if let providerGateway: [String: Any] = Self.getProperty(forSource: source, andKey: ConfigurationKeys.paymentGateway),
            let providerGatewayName = providerGateway[ConfigurationKeys.paymentGatewayName] as? String,
            let requestURL = providerGateway[ConfigurationKeys.paymentRequestURL] as? String {
            let publishableKey = providerGateway[ConfigurationKeys.stripePublishableKey] as? String
            gatewayModel = OSPMTGatewayModel(gateway: providerGatewayName, publishableKey: publishableKey, requestURL: requestURL)
        }
        
        self.init(
            merchantID: merchantID,
            merchantName: merchantName,
            merchantCountryCode: merchantCountryCode,
            paymentAllowedNetworks: paymentAllowedNetworks,
            paymentSupportedCapabilities: paymentSupportedCapabilities,
            paymentSupportedCardCountries: paymentSupportedCardCountries,
            shippingSupportedContacts: shippingSupportedContacts,
            billingSupportedContacts: billingSupportedContacts,
            gatewayModel: gatewayModel
        )
    }
}

private extension OSPMTApplePayConfiguration {
    /// Fetches the parameter property, if it exists.
    /// - Parameters:
    ///   - type: Type of variable to return.
    ///   - key: Property key to search from.
    ///   - isRequired: Indicates if the property is mandatory or not.
    /// - Returns: The configuration property, if it exists.
    static func getProperty<T: Collection>(forSource source: OSPMTConfiguration, andKey key: String, isRequired: Bool = false) -> T? {
        let result = source[key] as? T
        return !isRequired || result?.isEmpty == false ? result : nil
    }
    
    /// An acelerator for `getProperty(ofType:forKey:isRequired:)`, that should be used for mandatory properties.
    /// - Parameters:
    ///   - type: Type of variable to return.
    ///   - key: Property key to search from.
    /// - Returns: The configuration property, if it exists.
    static func getRequiredProperty<T: Collection>(forSource source: OSPMTConfiguration, andKey key: String) -> T? {
        self.getProperty(forSource: source, andKey: key, isRequired: true)
    }
}

extension OSPMTApplePayConfiguration {
    var supportedNetworks: [PKPaymentNetwork]? {
        guard let paymentAllowedNetworks = self.paymentAllowedNetworks else { return nil }
        let result = paymentAllowedNetworks.compactMap(PKPaymentNetwork.convert(from:))
        
        return !result.isEmpty ? result : nil
    }
    
    var merchantCapabilities: PKMerchantCapability? {
        guard let paymentSupportedCapabilities = self.paymentSupportedCapabilities else { return nil }
        
        var result: PKMerchantCapability = []
        result = paymentSupportedCapabilities.reduce(into: result) { partialResult, capability in
            let merchantCapability = PKMerchantCapability.convert(from: capability)
            if let merchantCapability = merchantCapability {
                partialResult.insert(merchantCapability)
            }
        }
        
        return !result.isEmpty ? result : nil
    }
    
    var supportedCountries: Set<String>? {
        guard let paymentSupportedCardCountries = self.paymentSupportedCardCountries, !paymentSupportedCardCountries.isEmpty else { return nil }
        return Set(paymentSupportedCardCountries)
    }
}
