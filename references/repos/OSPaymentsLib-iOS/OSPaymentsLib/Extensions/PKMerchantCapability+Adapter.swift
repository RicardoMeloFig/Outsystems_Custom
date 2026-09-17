import PassKit

extension PKMerchantCapability {
    /// Allows the conversion of the text associated to a Merchant Capability into an object of `PKMerchantCapability` type.
    /// - Parameter text: Merchant capability text to convert.
    /// - Returns: The equivalent `PKMerchantCapability` object.
    static func convert(from text: String) -> PKMerchantCapability? {
        switch text.lowercased() {
        case "debit":
            return .capabilityDebit
        case "credit":
            return .capabilityCredit
        case "3ds":
            return .capability3DS
        case "emv":
            return .capabilityEMV
        default:
            return nil
        }
    }
}
