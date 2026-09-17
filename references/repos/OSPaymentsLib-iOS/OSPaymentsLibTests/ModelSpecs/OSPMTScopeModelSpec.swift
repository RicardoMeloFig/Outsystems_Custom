import Nimble
import Quick

@testable import OSPaymentsLib
import PassKit

class OSPMTScopeModelSpec: QuickSpec {
    struct TestConfiguration {
        static let fullConfig = [
            OSPMTScopeModel.CodingKeys.paymentData.rawValue: OSPMTDataModelSpec.TestConfiguration.fullConfig,
            OSPMTScopeModel.CodingKeys.shippingInfo.rawValue: OSPMTContactInfoModelSpec.TestConfiguration.fullConfig
        ]
        static let noPaymentDataConfig = [OSPMTScopeModel.CodingKeys.shippingInfo.rawValue: OSPMTContactInfoModelSpec.TestConfiguration.fullConfig]
        static let noShippingInfoConfig = [OSPMTScopeModel.CodingKeys.paymentData.rawValue: OSPMTDataModelSpec.TestConfiguration.fullConfig]
        
        static let fullModel = OSPMTScopeModel(
            paymentData: OSPMTDataModelSpec.TestConfiguration.fullModel,
            shippingInfo: OSPMTContactInfoModelSpec.TestConfiguration.fullModel
        )
        static let noShippingInfoModel = OSPMTScopeModel(paymentData: OSPMTDataModelSpec.TestConfiguration.fullModel)
        
        static let paymentRequest = PKPaymentRequest()
    }
    
    override class func spec() {
        describe("Given a full configuration") {
            context("When decoding the Data Model") {
                it("Should return a filled object") {
                    if let model = OSPMTTestConfigurations.decode(for: OSPMTScopeModel.self, TestConfiguration.fullConfig) {
                        expect(model).toNot(beNil())
                        expect(model.paymentData).toNot(beNil())
                        expect(model.shippingInfo).toNot(beNil())
                    } else {
                        fail()
                    }
                }
            }
        }
        
        describe("Given a full configuration without Payment Data") {
            context("When decoding the Data Model") {
                it("Should return a nil object") {
                    if OSPMTTestConfigurations.decode(for: OSPMTScopeModel.self, TestConfiguration.noPaymentDataConfig) != nil {
                        fail()
                    } else {
                        expect(true).to(beTruthy())
                    }
                }
            }
        }
        
        describe("Given a full configuration without Billing Info") {
            context("When decoding the Data Model") {
                it("Should return a filled object") {
                    if let model = OSPMTTestConfigurations.decode(for: OSPMTScopeModel.self, TestConfiguration.noShippingInfoConfig) {
                        expect(model).toNot(beNil())
                        expect(model.paymentData).toNot(beNil())
                        expect(model.shippingInfo).to(beNil())
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
        
        describe("Given a full model without Shipping Info") {
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
    }
}
