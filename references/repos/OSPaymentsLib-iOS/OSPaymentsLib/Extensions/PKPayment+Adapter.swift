import PassKit

extension PKPayment {
    /// Converts a `PKPayment` object into a `OSPMTScopeModel` one. Returns `nil` if it can't.
    /// - Parameter paymentGatewayModel: model that contains the payment gateway information resulting from completing a payment process.
    /// - Returns: The corresponding `OSPMTScopeModel` object. Can also return `nil` if the conversion fails.
    func createScopeModel(for paymentGatewayModel: OSPMTServiceProviderInfoModel? = nil) -> OSPMTScopeModel? {
        var result: [String: Any] = [OSPMTScopeModel.CodingKeys.paymentData.rawValue: self.createTokenDataData(for: paymentGatewayModel)]
        if let shippingContact = self.shippingContact {
            result[OSPMTScopeModel.CodingKeys.shippingInfo.rawValue] = self.createContactInfoData(for: shippingContact)
        }
        
        guard let scopeData = try? JSONSerialization.data(withJSONObject: result),
            let scopeModel = try? JSONDecoder().decode(OSPMTScopeModel.self, from: scopeData)
        else { return nil }
        return scopeModel
    }
    
    /// Converts a `PKPayment` object into a dictionary that relates to an `OSPMTDataModel` object.
    /// - Parameter paymentGatewayModel: model that contains the payment gateway information resulting from completing a payment process.
    /// - Returns: The corresponding `OSPMTDataModel` dictionary object.
    private func createTokenDataData(for paymentGatewayModel: OSPMTServiceProviderInfoModel?) -> [String: Any] {
        var result: [String: Any] = [
            OSPMTDataModel.CodingKeys.tokenData.rawValue: self.createTokenData(for: self.token.paymentData)
        ]
        if let billingContact = self.billingContact {
            result[OSPMTDataModel.CodingKeys.billingInfo.rawValue] = self.createContactInfoData(for: billingContact)
        }
        if let paymentMethodName = self.token.paymentMethod.displayName {
            let cardInfo = paymentMethodName.components(separatedBy: " ")
            if let cardNetwork = cardInfo.first, let cardDetails = cardInfo.last {
                result[OSPMTDataModel.CodingKeys.cardDetails.rawValue] = cardDetails
                result[OSPMTDataModel.CodingKeys.cardNetwork.rawValue] = cardNetwork
            }
        }
        if let paymentGatewayModel = paymentGatewayModel,
            let paymentGatewayData = try? JSONEncoder().encode(paymentGatewayModel),
            let paymentGatewayDict = try? JSONSerialization.jsonObject(with: paymentGatewayData) as? [String: String] {
            result[OSPMTDataModel.CodingKeys.paymentServiceProviderData.rawValue] = paymentGatewayDict
        }
        
        return result
    }
    
    /// Takes the passed payment data object and converts it into a dictionary that relates to an `OSPMTTokenInfoModel` object.
    /// - Parameter paymentData: `Data` type object that contains information related to a payment token.
    /// - Returns: The corresponding `OSPMTTokenInfoModel` dictionary object.
    private func createTokenData(for paymentData: Data) -> [String: String] {
        var result = [OSPMTTokenInfoModel.CodingKeys.type.rawValue: "Apple Pay"]
        
        if let token = String(data: paymentData, encoding: .utf8) {
            result[OSPMTTokenInfoModel.CodingKeys.token.rawValue] = token
        }
        
        return result
    }
    
    /// Takes the passed contact object and converts it into a dictionary that relates to an `OSPMTContactInfoModel` object.
    /// - Parameter contact: `PKContact` type object that contains information related to the filled Billing or Shipping Information
    /// - Returns: The corresponding `OSPMTContactInfoModel` dictionary object.
    private func createContactInfoData(for contact: PKContact) -> [String: Any]? {
        var result = [String: Any]()
        if let address = contact.postalAddress {
            result[OSPMTContactInfoModel.CodingKeys.address.rawValue] = self.createAddressData(for: address)
        }
        if let phoneNumber = contact.phoneNumber {
            result[OSPMTContactInfoModel.CodingKeys.phoneNumber.rawValue] = phoneNumber.stringValue
        }
        if let name = contact.name, let givenName = name.givenName, let familyName = name.familyName, !givenName.isEmpty, !familyName.isEmpty {
            result[OSPMTContactInfoModel.CodingKeys.name.rawValue] = "\(givenName) \(familyName)"
        }
        if let email = contact.emailAddress {
            result[OSPMTContactInfoModel.CodingKeys.email.rawValue] = email
        }
        
        return !result.isEmpty ? result : nil
    }
    
    /// Takes the passed address object and converts it into a dictionary that relates to an `OSPMTAddressModel` object.
    /// - Parameter postalAddress: `CNPostalAddress` type object that contains an address related to the filled Billing or Shipping Information.
    /// - Returns: The corresponding `OSPMTAddressModel` dictionary object.
    private func createAddressData(for postalAddress: CNPostalAddress) -> [String: String] {
        var result = [
            OSPMTAddressModel.CodingKeys.postalCode.rawValue: postalAddress.postalCode,
            OSPMTAddressModel.CodingKeys.fullAddress.rawValue: postalAddress.street,
            OSPMTAddressModel.CodingKeys.countryCode.rawValue: postalAddress.isoCountryCode,
            OSPMTAddressModel.CodingKeys.city.rawValue: postalAddress.city
        ]
        if !postalAddress.subAdministrativeArea.isEmpty {
            result[OSPMTAddressModel.CodingKeys.administrativeArea.rawValue] = postalAddress.subAdministrativeArea
        }
        if !postalAddress.state.isEmpty {
            result[OSPMTAddressModel.CodingKeys.state.rawValue] = postalAddress.state
        }
        
        return result
    }
}
