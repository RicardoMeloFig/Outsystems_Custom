import PassKit

extension PKPaymentNetwork {
    /// Allows the conversion of the text associated to a Payment Network  into an object of `PKPaymentNetwork` type.
    /// - Parameter text: Payment network text to convert.
    /// - Returns: The equivalent `PKPaymentNetwork` object.
    static func convert(from text: String) -> PKPaymentNetwork? {
        switch text.lowercased() {
        case "amex":
            return .amex
        case "discover":
            return .discover
        case "visa":
            return .visa
        case "mastercard":
            return .masterCard
        default:
            return nil
        }
    }
}
