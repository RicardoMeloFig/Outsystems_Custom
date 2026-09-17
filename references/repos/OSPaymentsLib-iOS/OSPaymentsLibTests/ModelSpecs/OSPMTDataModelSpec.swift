import Nimble
import Quick

@testable import OSPaymentsLib

class OSPMTDataModelSpec: QuickSpec {
    struct TestConfiguration {
        static let fullConfig: [String: Any] = [
            OSPMTDataModel.CodingKeys.billingInfo.rawValue: OSPMTContactInfoModelSpec.TestConfiguration.fullConfig,
            OSPMTDataModel.CodingKeys.cardDetails.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTDataModel.CodingKeys.cardNetwork.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTDataModel.CodingKeys.tokenData.rawValue: OSPMTTokenInfoModelSpec.TestConfiguration.fullConfig
        ]
        static let noBillingInfoConfig: [String: Any] = [
            OSPMTDataModel.CodingKeys.cardDetails.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTDataModel.CodingKeys.cardNetwork.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTDataModel.CodingKeys.tokenData.rawValue: OSPMTTokenInfoModelSpec.TestConfiguration.fullConfig
        ]
        static let noCardDetailsConfig: [String: Any] = [
            OSPMTDataModel.CodingKeys.billingInfo.rawValue: OSPMTContactInfoModelSpec.TestConfiguration.fullConfig,
            OSPMTDataModel.CodingKeys.cardNetwork.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTDataModel.CodingKeys.tokenData.rawValue: OSPMTTokenInfoModelSpec.TestConfiguration.fullConfig
        ]
        static let noCardNetworkConfig: [String: Any] = [
            OSPMTDataModel.CodingKeys.billingInfo.rawValue: OSPMTContactInfoModelSpec.TestConfiguration.fullConfig,
            OSPMTDataModel.CodingKeys.cardDetails.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTDataModel.CodingKeys.tokenData.rawValue: OSPMTTokenInfoModelSpec.TestConfiguration.fullConfig
        ]
        static let noTokenDataConfig: [String: Any] = [
            OSPMTDataModel.CodingKeys.billingInfo.rawValue: OSPMTContactInfoModelSpec.TestConfiguration.fullConfig,
            OSPMTDataModel.CodingKeys.cardDetails.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTDataModel.CodingKeys.cardNetwork.rawValue: OSPMTTestConfigurations.dummyString
        ]
        
        static let fullModel = OSPMTDataModel(
            billingInfo: OSPMTContactInfoModelSpec.TestConfiguration.fullModel,
            cardDetails: OSPMTTestConfigurations.dummyString,
            cardNetwork: OSPMTTestConfigurations.dummyString,
            tokenData: OSPMTTokenInfoModelSpec.TestConfiguration.fullModel
        )
        static let noBillingModel = OSPMTDataModel(
            cardDetails: OSPMTTestConfigurations.dummyString,
            cardNetwork: OSPMTTestConfigurations.dummyString,
            tokenData: OSPMTTokenInfoModelSpec.TestConfiguration.fullModel
        )
    }
    
    override class func spec() {
        describe("Given a full configuration") {
            context("When decoding the Data Model") {
                it("Should return a filled object") {
                    if let model = OSPMTTestConfigurations.decode(for: OSPMTDataModel.self, TestConfiguration.fullConfig) {
                        expect(model).toNot(beNil())
                        expect(model.billingInfo).toNot(beNil())
                        expect(model.cardDetails).to(equal(OSPMTTestConfigurations.dummyString))
                        expect(model.cardNetwork).to(equal(OSPMTTestConfigurations.dummyString))
                        expect(model.tokenData).to(equal(OSPMTTokenInfoModelSpec.TestConfiguration.fullModel))
                    } else {
                        fail()
                    }
                }
            }
        }
        
        describe("Given a full configuration without Billing Info") {
            context("When decoding the Data Model") {
                it("Should return a filled object") {
                    if let model = OSPMTTestConfigurations.decode(for: OSPMTDataModel.self, TestConfiguration.noBillingInfoConfig) {
                        expect(model).toNot(beNil())
                        expect(model.billingInfo).to(beNil())
                        expect(model.cardDetails).to(equal(OSPMTTestConfigurations.dummyString))
                        expect(model.cardNetwork).to(equal(OSPMTTestConfigurations.dummyString))
                        expect(model.tokenData).to(equal(OSPMTTokenInfoModelSpec.TestConfiguration.fullModel))
                    } else {
                        fail()
                    }
                }
            }
        }
        
        describe("Given a full configuration without Card Details") {
            context("When decoding the Data Model") {
                it("Should return a nil object") {
                    if OSPMTTestConfigurations.decode(for: OSPMTDataModel.self, TestConfiguration.noCardDetailsConfig) != nil {
                        fail()
                    } else {
                        expect(true).to(beTruthy())
                    }
                }
            }
        }
        
        describe("Given a full configuration without Card Network") {
            context("When decoding the Data Model") {
                it("Should return a nil object") {
                    if OSPMTTestConfigurations.decode(for: OSPMTDataModel.self, TestConfiguration.noCardNetworkConfig) != nil {
                        fail()
                    } else {
                        expect(true).to(beTruthy())
                    }
                }
            }
        }
        
        describe("Given a full configuration without Token Data") {
            context("When decoding the Data Model") {
                it("Should return a nil object") {
                    if OSPMTTestConfigurations.decode(for: OSPMTDataModel.self, TestConfiguration.noTokenDataConfig) != nil {
                        fail()
                    } else {
                        expect(true).to(beTruthy())
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
        
        describe("Given a full model without Billing Info") {
            context("When encoding it into JSON") {
                it("Should return a filled object") {
                    if let text = OSPMTTestConfigurations.encode(TestConfiguration.noBillingModel) {
                        expect(text).toNot(beEmpty())
                    } else {
                        fail()
                    }
                }
            }
        }
    }
}
