import Nimble
import Quick

@testable import OSPaymentsLib

class OSPMTContactInfoModelSpec: QuickSpec {
    struct TestConfiguration {
        static let fullConfig: [String: Any] = [
            OSPMTContactInfoModel.CodingKeys.address.rawValue: OSPMTAddressModelSpec.TestConfiguration.fullConfig,
            OSPMTContactInfoModel.CodingKeys.phoneNumber.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTContactInfoModel.CodingKeys.name.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTContactInfoModel.CodingKeys.email.rawValue: OSPMTTestConfigurations.dummyString
        ]
        static let noAddressConfig: [String: Any] = [
            OSPMTContactInfoModel.CodingKeys.phoneNumber.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTContactInfoModel.CodingKeys.name.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTContactInfoModel.CodingKeys.email.rawValue: OSPMTTestConfigurations.dummyString
        ]
        static let noPhoneNumberConfig: [String: Any] = [
            OSPMTContactInfoModel.CodingKeys.address.rawValue: OSPMTTestConfigurations.dummyContactInfoModel,
            OSPMTContactInfoModel.CodingKeys.name.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTContactInfoModel.CodingKeys.email.rawValue: OSPMTTestConfigurations.dummyString
        ]
        static let noNameConfig: [String: Any] = [
            OSPMTContactInfoModel.CodingKeys.address.rawValue: OSPMTTestConfigurations.dummyContactInfoModel,
            OSPMTContactInfoModel.CodingKeys.phoneNumber.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTContactInfoModel.CodingKeys.email.rawValue: OSPMTTestConfigurations.dummyString
        ]
        static let noEmailConfig: [String: Any] = [
            OSPMTContactInfoModel.CodingKeys.address.rawValue: OSPMTTestConfigurations.dummyContactInfoModel,
            OSPMTContactInfoModel.CodingKeys.phoneNumber.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTContactInfoModel.CodingKeys.name.rawValue: OSPMTTestConfigurations.dummyString
        ]
        
        static let fullModel = OSPMTContactInfoModel(
            address: OSPMTTestConfigurations.dummyAddressModel,
            phoneNumber: OSPMTTestConfigurations.dummyString,
            name: OSPMTTestConfigurations.dummyString,
            email: OSPMTTestConfigurations.dummyString
        )
        static let noAddressModel = OSPMTContactInfoModel(
            phoneNumber: OSPMTTestConfigurations.dummyString,
            name: OSPMTTestConfigurations.dummyString,
            email: OSPMTTestConfigurations.dummyString
        )
        static let noPhoneNumberModel = OSPMTContactInfoModel(
            address: OSPMTTestConfigurations.dummyAddressModel,
            name: OSPMTTestConfigurations.dummyString,
            email: OSPMTTestConfigurations.dummyString
        )
        static let noNameModel = OSPMTContactInfoModel(
            address: OSPMTTestConfigurations.dummyAddressModel,
            phoneNumber: OSPMTTestConfigurations.dummyString,
            email: OSPMTTestConfigurations.dummyString
        )
        static let noEmailModel = OSPMTContactInfoModel(
            address: OSPMTTestConfigurations.dummyAddressModel,
            phoneNumber: OSPMTTestConfigurations.dummyString,
            name: OSPMTTestConfigurations.dummyString
        )
    }
    
    override class func spec() {
        describe("Given a full configuration") {
            context("When decoding the Contact Info Model") {
                it("Should return a filled object") {
                    if let model = OSPMTTestConfigurations.decode(for: OSPMTContactInfoModel.self, TestConfiguration.fullConfig) {
                        expect(model).toNot(beNil())
                        expect(model.address).to(equal(OSPMTAddressModelSpec.TestConfiguration.fullModel))
                        expect(model.phoneNumber).to(equal(OSPMTTestConfigurations.dummyString))
                        expect(model.name).to(equal(OSPMTTestConfigurations.dummyString))
                        expect(model.email).to(equal(OSPMTTestConfigurations.dummyString))
                    } else {
                        fail()
                    }
                }
            }
        }
        
        describe("Given a full configuration without address") {
            context("When decoding the Contact Info Model") {
                it("Should return a filled object") {
                    if let model = OSPMTTestConfigurations.decode(for: OSPMTContactInfoModel.self, TestConfiguration.fullConfig) {
                        expect(model).toNot(beNil())
                        expect(model.phoneNumber).to(equal(OSPMTTestConfigurations.dummyString))
                        expect(model.name).to(equal(OSPMTTestConfigurations.dummyString))
                        expect(model.email).to(equal(OSPMTTestConfigurations.dummyString))
                    } else {
                        fail()
                    }
                }
            }
        }
        
        describe("Given a full configuration without phone number") {
            context("When decoding the Contact Info Model") {
                it("Should return a filled object") {
                    if let model = OSPMTTestConfigurations.decode(for: OSPMTContactInfoModel.self, TestConfiguration.fullConfig) {
                        expect(model).toNot(beNil())
                        expect(model.address).to(equal(OSPMTAddressModelSpec.TestConfiguration.fullModel))
                        expect(model.name).to(equal(OSPMTTestConfigurations.dummyString))
                        expect(model.email).to(equal(OSPMTTestConfigurations.dummyString))
                    } else {
                        fail()
                    }
                }
            }
        }
        
        describe("Given a full configuration without name") {
            context("When decoding the Contact Info Model") {
                it("Should return a filled object") {
                    if let model = OSPMTTestConfigurations.decode(for: OSPMTContactInfoModel.self, TestConfiguration.fullConfig) {
                        expect(model).toNot(beNil())
                        expect(model.address).to(equal(OSPMTAddressModelSpec.TestConfiguration.fullModel))
                        expect(model.phoneNumber).to(equal(OSPMTTestConfigurations.dummyString))
                        expect(model.email).to(equal(OSPMTTestConfigurations.dummyString))
                    } else {
                        fail()
                    }
                }
            }
        }
        
        describe("Given a full configuration without email") {
            context("When decoding the Contact Info Model") {
                it("Should return a filled object") {
                    if let model = OSPMTTestConfigurations.decode(for: OSPMTContactInfoModel.self, TestConfiguration.fullConfig) {
                        expect(model).toNot(beNil())
                        expect(model.address).to(equal(OSPMTAddressModelSpec.TestConfiguration.fullModel))
                        expect(model.phoneNumber).to(equal(OSPMTTestConfigurations.dummyString))
                        expect(model.name).to(equal(OSPMTTestConfigurations.dummyString))
                    } else {
                        fail()
                    }
                }
            }
        }
        
        describe("Given a full model") {
            context("When encoding it into JSON") {
                it("Should return a filled object") {
                    if let text = OSPMTTestConfigurations.encode(TestConfiguration.fullModel) {
                        expect(text).toNot(beEmpty())
                    } else {
                        fail()
                    }
                }
            }
        }
        
        describe("Given a full model without address") {
            context("When encoding it into JSON") {
                it("Should return a filled object") {
                    if let text = OSPMTTestConfigurations.encode(TestConfiguration.noAddressModel) {
                        expect(text).toNot(beEmpty())
                    } else {
                        fail()
                    }
                }
            }
        }
        
        describe("Given a full model without phone number") {
            context("When encoding it into JSON") {
                it("Should return a filled object") {
                    if let text = OSPMTTestConfigurations.encode(TestConfiguration.noPhoneNumberModel) {
                        expect(text).toNot(beEmpty())
                    } else {
                        fail()
                    }
                }
            }
        }
        
        describe("Given a full model without name") {
            context("When encoding it into JSON") {
                it("Should return a filled object") {
                    if let text = OSPMTTestConfigurations.encode(TestConfiguration.noNameModel) {
                        expect(text).toNot(beEmpty())
                    } else {
                        fail()
                    }
                }
            }
        }
        
        describe("Given a full model without email") {
            context("When encoding it into JSON") {
                it("Should return a filled object") {
                    if let text = OSPMTTestConfigurations.encode(TestConfiguration.noEmailModel) {
                        expect(text).toNot(beEmpty())
                    } else {
                        fail()
                    }
                }
            }
        }
    }
}
