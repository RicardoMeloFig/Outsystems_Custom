import PassKit

extension PKContactField {
    /// Allows the conversion of the contact text associated to Shipping and Billing Information into an object of `PKContactField` type.
    /// - Parameter text: Contact field text to convert.
    /// - Returns: The equivalent `PKContactField` object.
    static func convert(from text: String) -> PKContactField? {
        switch text.lowercased() {
        case "email":
            return .emailAddress
        case "name":
            return .name
        case "phone":
            return .phoneNumber
        case "postal_address":
            return .postalAddress
        default:
            return nil
        }
    }
}
