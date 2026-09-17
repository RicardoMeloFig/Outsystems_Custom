import Nimble
import Quick

@testable import OSPaymentsLib

class OSPMTAddressModelSpec: QuickSpec {
    struct TestConfiguration {
        static let fullConfig = [
            OSPMTAddressModel.CodingKeys.postalCode.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTAddressModel.CodingKeys.fullAddress.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTAddressModel.CodingKeys.countryCode.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTAddressModel.CodingKeys.city.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTAddressModel.CodingKeys.administrativeArea.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTAddressModel.CodingKeys.state.rawValue: OSPMTTestConfigurations.dummyString
        ]
        static let noPostalCodeConfig = [
            OSPMTAddressModel.CodingKeys.fullAddress.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTAddressModel.CodingKeys.countryCode.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTAddressModel.CodingKeys.city.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTAddressModel.CodingKeys.administrativeArea.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTAddressModel.CodingKeys.state.rawValue: OSPMTTestConfigurations.dummyString
        ]
        static let noFullAddressConfig = [
            OSPMTAddressModel.CodingKeys.postalCode.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTAddressModel.CodingKeys.countryCode.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTAddressModel.CodingKeys.city.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTAddressModel.CodingKeys.administrativeArea.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTAddressModel.CodingKeys.state.rawValue: OSPMTTestConfigurations.dummyString
        ]
        static let noCountryCodeConfig = [
            OSPMTAddressModel.CodingKeys.postalCode.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTAddressModel.CodingKeys.fullAddress.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTAddressModel.CodingKeys.city.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTAddressModel.CodingKeys.administrativeArea.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTAddressModel.CodingKeys.state.rawValue: OSPMTTestConfigurations.dummyString
        ]
        static let noCityConfig = [
            OSPMTAddressModel.CodingKeys.postalCode.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTAddressModel.CodingKeys.fullAddress.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTAddressModel.CodingKeys.countryCode.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTAddressModel.CodingKeys.administrativeArea.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTAddressModel.CodingKeys.state.rawValue: OSPMTTestConfigurations.dummyString
        ]
        static let noAdministrativeAreaConfig = [
            OSPMTAddressModel.CodingKeys.postalCode.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTAddressModel.CodingKeys.fullAddress.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTAddressModel.CodingKeys.countryCode.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTAddressModel.CodingKeys.city.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTAddressModel.CodingKeys.state.rawValue: OSPMTTestConfigurations.dummyString
        ]
        static let noStateConfig = [
            OSPMTAddressModel.CodingKeys.postalCode.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTAddressModel.CodingKeys.fullAddress.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTAddressModel.CodingKeys.countryCode.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTAddressModel.CodingKeys.city.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTAddressModel.CodingKeys.administrativeArea.rawValue: OSPMTTestConfigurations.dummyString
        ]
        
        static let fullModel = OSPMTAddressModel(
            postalCode: OSPMTTestConfigurations.dummyString,
            fullAddress: OSPMTTestConfigurations.dummyString,
            countryCode: OSPMTTestConfigurations.dummyString,
            city: OSPMTTestConfigurations.dummyString,
            administrativeArea: OSPMTTestConfigurations.dummyString,
            state: OSPMTTestConfigurations.dummyString
        )
        static let noAdministrativeAreaModel = OSPMTAddressModel(
            postalCode: OSPMTTestConfigurations.dummyString,
            fullAddress: OSPMTTestConfigurations.dummyString,
            countryCode: OSPMTTestConfigurations.dummyString,
            city: OSPMTTestConfigurations.dummyString,
            state: OSPMTTestConfigurations.dummyString
        )
        static let noStateModel = OSPMTAddressModel(
            postalCode: OSPMTTestConfigurations.dummyString,
            fullAddress: OSPMTTestConfigurations.dummyString,
            countryCode: OSPMTTestConfigurations.dummyString,
            city: OSPMTTestConfigurations.dummyString,
            administrativeArea: OSPMTTestConfigurations.dummyString
        )
    }
    
    override class func spec() {
        describe("Given a full configuration") {
            context("When decoding the Address Model") {
                it("Should return a filled object") {
                    if let addressModel = OSPMTTestConfigurations.decode(for: OSPMTAddressModel.self, TestConfiguration.fullConfig) {
                        expect(addressModel).toNot(beNil())
                        expect(addressModel.postalCode).to(equal(OSPMTTestConfigurations.dummyString))
                        expect(addressModel.fullAddress).to(equal(OSPMTTestConfigurations.dummyString))
                        expect(addressModel.countryCode).to(equal(OSPMTTestConfigurations.dummyString))
                        expect(addressModel.city).to(equal(OSPMTTestConfigurations.dummyString))
                        expect(addressModel.administrativeArea).to(equal(OSPMTTestConfigurations.dummyString))
                        expect(addressModel.state).to(equal(OSPMTTestConfigurations.dummyString))
                    } else {
                        fail()
                    }
                }
            }
        }
        
        describe("Given a full configuration without Postal Code") {
            context("When decoding the Address Model") {
                it("Should return a nil value") {
                    if OSPMTTestConfigurations.decode(for: OSPMTAddressModel.self, TestConfiguration.noPostalCodeConfig) != nil {
                        fail()
                    } else {
                        expect(true).to(beTruthy())
                    }
                }
            }
        }
        
        describe("Given a full configuration without Full Address") {
            context("When decoding the Address Model") {
                it("Should return a nil value") {
                    if OSPMTTestConfigurations.decode(for: OSPMTAddressModel.self, TestConfiguration.noFullAddressConfig) != nil {
                        fail()
                    } else {
                        expect(true).to(beTruthy())
                    }
                }
            }
        }
        
        describe("Given a full configuration without Country Code") {
            context("When decoding the Address Model") {
                it("Should return a nil value") {
                    if OSPMTTestConfigurations.decode(for: OSPMTAddressModel.self, TestConfiguration.noCountryCodeConfig) != nil {
                        fail()
                    } else {
                        expect(true).to(beTruthy())
                    }
                }
            }
        }
        
        describe("Given a full configuration without City") {
            context("When decoding the Address Model") {
                it("Should return a nil value") {
                    if OSPMTTestConfigurations.decode(for: OSPMTAddressModel.self, TestConfiguration.noCityConfig) != nil {
                        fail()
                    } else {
                        expect(true).to(beTruthy())
                    }
                }
            }
        }
        
        describe("Given a full configuration without Administrative Area") {
            context("When decoding the Address Model") {
                it("Should return a filled object") {
                    if let addressModel = OSPMTTestConfigurations.decode(for: OSPMTAddressModel.self, TestConfiguration.noAdministrativeAreaConfig) {
                        expect(addressModel).toNot(beNil())
                        expect(addressModel.postalCode).to(equal(OSPMTTestConfigurations.dummyString))
                        expect(addressModel.fullAddress).to(equal(OSPMTTestConfigurations.dummyString))
                        expect(addressModel.countryCode).to(equal(OSPMTTestConfigurations.dummyString))
                        expect(addressModel.city).to(equal(OSPMTTestConfigurations.dummyString))
                        expect(addressModel.state).to(equal(OSPMTTestConfigurations.dummyString))
                    } else {
                        fail()
                    }
                }
            }
        }
        
        describe("Given a full configuration without State") {
            context("When decoding the Address Model") {
                it("Should return a filled object") {
                    if let addressModel = OSPMTTestConfigurations.decode(for: OSPMTAddressModel.self, TestConfiguration.fullConfig) {
                        expect(addressModel).toNot(beNil())
                        expect(addressModel.postalCode).to(equal(OSPMTTestConfigurations.dummyString))
                        expect(addressModel.fullAddress).to(equal(OSPMTTestConfigurations.dummyString))
                        expect(addressModel.countryCode).to(equal(OSPMTTestConfigurations.dummyString))
                        expect(addressModel.city).to(equal(OSPMTTestConfigurations.dummyString))
                        expect(addressModel.administrativeArea).to(equal(OSPMTTestConfigurations.dummyString))                        
                    } else {
                        fail()
                    }
                }
            }
        }
        
        describe("Given a full model") {
            context("When encoding it into JSON") {
                it("Should return a filled object") {
                    if let addressText = OSPMTTestConfigurations.encode(TestConfiguration.fullModel) {
                        expect(addressText).toNot(beEmpty())
                    } else {
                        fail()
                    }
                }
            }
        }
        
        describe("Given a full model without Adminsitrative Area") {
            context("When encoding it into JSON") {
                it("Should return a filled object") {
                    if let addressText = OSPMTTestConfigurations.encode(TestConfiguration.noAdministrativeAreaModel) {
                        expect(addressText).toNot(beEmpty())
                    } else {
                        fail()
                    }
                }
            }
        }
        
        describe("Given a full model without State") {
            context("When encoding it into JSON") {
                it("Should return a filled object") {
                    if let addressText = OSPMTTestConfigurations.encode(TestConfiguration.noStateModel) {
                        expect(addressText).toNot(beEmpty())
                    } else {
                        fail()
                    }
                }
            }
        }
    }
            
}
