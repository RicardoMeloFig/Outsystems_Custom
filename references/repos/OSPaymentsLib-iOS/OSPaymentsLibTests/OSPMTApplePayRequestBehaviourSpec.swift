import Nimble
import PassKit
import Quick
@testable import OSPaymentsLib

class MockRequestTriggerBehaviour: OSPMTApplePayRequestTriggerDelegate {
    static var error: OSPMTError?
    static var isCompleted: Bool = false
    static var paymentScope: OSPMTScopeModel?
    
    required init() {}
    
    static func createRequestTriggerBehaviour(for detailsModel: OSPMTDetailsModel, andDelegate delegate: OSPMTApplePayRequestBehaviour?) -> Result<OSPMTApplePayRequestTriggerDelegate, OSPMTError> {
        if let error = error {
            return .failure(error)
        } else {
            delegate?.paymentScope = Self.paymentScope
            return .success(Self.init())
        }
    }
    
    func triggerPayment(_ completion: @escaping OSPMTRequestTriggerCompletion) {
        completion(Self.isCompleted)
    }
}

class OSPMTApplePayRequestBehaviourSpec: QuickSpec {
    override class func spec() {
        var applePayRequestBehaviour: OSPMTApplePayRequestBehaviour!
        
        var mockConfiguration: OSPMTApplePayConfiguration!
        
        describe("Given a Request Behaviour") {
            context("When it's incorrectly configured") {
                beforeEach {
                    mockConfiguration = OSPMTApplePayConfiguration(source: OSPMTTestConfigurations.validNetworkCapabilityConfig)
                    applePayRequestBehaviour = OSPMTApplePayRequestBehaviour(
                        configuration: mockConfiguration, requestTriggerType: MockRequestTriggerBehaviour.self
                    )
                    MockRequestTriggerBehaviour.error = .invalidConfiguration
                }
                
                it("should return an InvalidConfiguration error") {
                    applePayRequestBehaviour.trigger(with: OSPMTTestConfigurations.dummyDetailsModel) { result in
                        if case let .failure(error) = result {
                            expect(error).to(equal(.invalidConfiguration))
                        } else {
                            fail()
                        }
                    }
                }
            }
            
            context("When it's not possible to trigger Apple Pay") {
                beforeEach {
                    mockConfiguration = OSPMTApplePayConfiguration(source: OSPMTTestConfigurations.validNetworkCapabilityConfig)
                    applePayRequestBehaviour = OSPMTApplePayRequestBehaviour(
                        configuration: mockConfiguration, requestTriggerType: MockRequestTriggerBehaviour.self
                    )
                    MockRequestTriggerBehaviour.error = nil
                    MockRequestTriggerBehaviour.isCompleted = false
                }
                
                it("should return a PaymentTriggerPresentationFailed error") {
                    applePayRequestBehaviour.trigger(with: OSPMTTestConfigurations.dummyDetailsModel) { result in
                        if case let .failure(error) = result {
                            expect(error).to(equal(.paymentTriggerPresentationFailed))
                        } else {
                            fail()
                        }
                    }
                }
            }
            
            context("When it's possible to trigger Apple Pay") {
                beforeEach {
                    mockConfiguration = OSPMTApplePayConfiguration(source: OSPMTTestConfigurations.validNetworkCapabilityConfig)
                    applePayRequestBehaviour = OSPMTApplePayRequestBehaviour(
                        configuration: mockConfiguration, requestTriggerType: MockRequestTriggerBehaviour.self
                    )
                    MockRequestTriggerBehaviour.error = nil
                    MockRequestTriggerBehaviour.isCompleted = true
                    MockRequestTriggerBehaviour.paymentScope = OSPMTTestConfigurations.dummyScopeModel
                }
                
                it("should return a valid Scope Model") {
                    applePayRequestBehaviour.trigger(with: OSPMTTestConfigurations.dummyDetailsModel) { result in
                        if case let .success(scopeModel) = result {
                            expect(scopeModel).to(equal(OSPMTTestConfigurations.dummyScopeModel))
                        } else {
                            fail()
                        }
                    }
                }
            }
            
            context("When checking the payment networks") {
                context("and the configuration lacks the merchant's name") {
                    beforeEach {
                        mockConfiguration = OSPMTApplePayConfiguration(source: OSPMTTestConfigurations.noMerchantNameConfig)
                        applePayRequestBehaviour = OSPMTApplePayRequestBehaviour(
                            configuration: mockConfiguration, requestTriggerType: MockRequestTriggerBehaviour.self
                        )
                    }
                    it("should return a nil value") {
                        let result = applePayRequestBehaviour.getPaymentSummaryItems(for: OSPMTTestConfigurations.dummyDetailsModel)
                        
                        expect(result).to(beNil())
                    }
                }
                
                context("and the configuration has the merchant's name") {
                    beforeEach {
                        mockConfiguration = OSPMTApplePayConfiguration(source: OSPMTTestConfigurations.fullConfig)
                        applePayRequestBehaviour = OSPMTApplePayRequestBehaviour(
                            configuration: mockConfiguration, requestTriggerType: MockRequestTriggerBehaviour.self
                        )
                    }
                    it("should return a non empty value") {
                        let result = applePayRequestBehaviour.getPaymentSummaryItems(for: OSPMTTestConfigurations.dummyDetailsModel)
                        
                        expect(result).toNot(beEmpty())
                    }
                }
            }
            
            context("When checking the contact fields") {
                beforeEach {
                    mockConfiguration = OSPMTApplePayConfiguration(source: OSPMTTestConfigurations.fullConfig)
                    applePayRequestBehaviour = OSPMTApplePayRequestBehaviour(
                        configuration: mockConfiguration, requestTriggerType: MockRequestTriggerBehaviour.self
                    )
                }
                context("and no contact is provided") {
                    it("should return a nil value") {
                        let result = applePayRequestBehaviour.getContactFields(for: OSPMTTestConfigurations.emptyContactFieldArray)
                        expect(result).to(beEmpty())
                    }
                }
                
                context("and a invalid contact is provided") {
                    it("should return a nil value") {
                        let result = applePayRequestBehaviour.getContactFields(for: OSPMTTestConfigurations.invalidContactFieldArray)
                        expect(result).to(beEmpty())
                    }
                }
                
                context("and a invalid contact and a valid is provided") {
                    it("should return the a array with the valid value") {
                        let result = applePayRequestBehaviour.getContactFields(for: OSPMTTestConfigurations.withInvalidContactFieldArray)
                        expect(result.count).to(equal(1))
                    }
                }
                
                context("and a valid contacts is provided") {
                    it("should return the a array with the valid value") {
                        let result = applePayRequestBehaviour.getContactFields(for: OSPMTTestConfigurations.validContactFieldArray)
                        expect(result.count).to(equal(2))
                    }
                }
            }
        }
    }
}
