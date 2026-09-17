import Nimble
import Quick

@testable import OSPaymentsLib

class OSPMTTokenInfoModelSpec: QuickSpec {
    struct TestConfiguration {
        static let fullConfig = [
            OSPMTTokenInfoModel.CodingKeys.token.rawValue: OSPMTTestConfigurations.dummyString,
            OSPMTTokenInfoModel.CodingKeys.type.rawValue: OSPMTTestConfigurations.dummyString
        ]
        static let noTokenConfig = [OSPMTTokenInfoModel.CodingKeys.type.rawValue: OSPMTTestConfigurations.dummyString]
        static let noTypeConfig = [OSPMTTokenInfoModel.CodingKeys.token.rawValue: OSPMTTestConfigurations.dummyString]
        
        static let fullModel = OSPMTTokenInfoModel(token: OSPMTTestConfigurations.dummyString, type: OSPMTTestConfigurations.dummyString)
    }
    
    override class func spec() {
        describe("Given a full configuration") {
            context("When decoding the Token Info Model") {
                it("Should return a filled object") {
                    if let model = OSPMTTestConfigurations.decode(for: OSPMTTokenInfoModel.self, TestConfiguration.fullConfig) {
                        expect(model).toNot(beNil())
                        expect(model.token).to(equal(OSPMTTestConfigurations.dummyString))
                        expect(model.type).to(equal(OSPMTTestConfigurations.dummyString))
                    } else {
                        fail()
                    }
                }
            }
        }
        
        describe("Given a full configuration without token") {
            context("When decoding the Token Info Model") {
                it("Should return a nil object") {
                    if OSPMTTestConfigurations.decode(for: OSPMTTokenInfoModel.self, TestConfiguration.noTokenConfig) != nil {
                        fail()
                    } else {
                        expect(true).to(beTruthy())
                    }
                }
            }
        }
        
        describe("Given a full configuration without type") {
            context("When decoding the Token Info Model") {
                it("Should return a nil object") {
                    if OSPMTTestConfigurations.decode(for: OSPMTTokenInfoModel.self, TestConfiguration.noTypeConfig) != nil {
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
    }
}
