import Nimble
import PassKit
import Quick
@testable import OSPaymentsLib

class MockWalletAvailableBehaviour: OSPMTWalletAvailabilityDelegate {
    static var result: Bool = false
    
    static func isWalletAvailable() -> Bool {
        return Self.result
    }
}

class MockSetupAvailableBehaviour: OSPMTApplePaySetupAvailabilityDelegate {
    static var setupAvailable: Bool = false
    static var paymentAvailable: Bool = false
    
    static func isPaymentAvailable(using networks: [PKPaymentNetwork]?, and merchantCapabilities: PKMerchantCapability?) -> Bool {
        return Self.setupAvailable
    }
    
    static func isPaymentAvailable() -> Bool {
        return Self.paymentAvailable
    }
}

class OSPMTApplePayAvailabilityBehaviourSpec: QuickSpec {
    override class func spec() {
        var applePayAvailabilityBehaviour: OSPMTApplePayAvailabilityBehaviour!
        
        let mockConfiguration = OSPMTApplePayConfiguration(source: OSPMTTestConfigurations.validNetworkCapabilityConfig)
        
        describe("Given an AvailabilityBehaviour") {
            beforeEach {
                applePayAvailabilityBehaviour = OSPMTApplePayAvailabilityBehaviour(
                    configuration: mockConfiguration,
                    walletAvailableBehaviour: MockWalletAvailableBehaviour.self,
                    setupAvailableBehaviour: MockSetupAvailableBehaviour.self
                )
            }
            
            context("When a Wallet is not available") {
                beforeEach {
                    MockWalletAvailableBehaviour.result = false
                }
                
                it("should return a WalletNotAvailable error") {
                    let result = applePayAvailabilityBehaviour.checkWallet()
                    expect(result).to(equal(.walletNotAvailable))
                }
            }
            
            context("When a Wallet is available") {
                beforeEach {
                    MockWalletAvailableBehaviour.result = true
                }
                
                it("should succeed and return nil") {
                    let result = applePayAvailabilityBehaviour.checkWallet()
                    expect(result).to(beNil())
                }
            }
            
            context("When Payment is not available") {
                beforeEach {
                    MockSetupAvailableBehaviour.paymentAvailable = false
                }
                
                it("should return a PaymentNotAvailable error") {
                    let result = applePayAvailabilityBehaviour.checkPaymentAvailability()
                    expect(result).to(equal(.paymentNotAvailable))
                }
            }
            
            context("When Payment is available") {
                beforeEach {
                    MockSetupAvailableBehaviour.paymentAvailable = true
                }
                
                it("should succeed and return nil") {
                    let result = applePayAvailabilityBehaviour.checkPaymentAvailability()
                    expect(result).to(beNil())
                }
            }
            
            context("When Payment Setup is not available") {
                beforeEach {
                    MockSetupAvailableBehaviour.setupAvailable = false
                }
                
                it("should return a PaymentNotAvailable error") {
                    let result = applePayAvailabilityBehaviour.checkPaymentAvailabilityWithSetup()
                    expect(result).to(equal(.setupPaymentNotAvailable))
                }
            }
            
            context("When Payment Setup is available") {
                beforeEach {
                    MockSetupAvailableBehaviour.setupAvailable = true
                }
                
                it("should succeed and return nil") {
                    let result = applePayAvailabilityBehaviour.checkPaymentAvailabilityWithSetup()
                    expect(result).to(beNil())
                }
            }
        }
    }
}
