import Nimble
import PassKit
import Quick
@testable import OSPaymentsLib

class OSPMTApplePayConfigurationSpec: QuickSpec {
    override class func spec() {
        var applePayConfiguration: OSPMTApplePayConfiguration!
        
        describe("Given a correct configuration") {
            beforeEach {
                applePayConfiguration = OSPMTApplePayConfiguration(source: OSPMTTestConfigurations.validNetworkCapabilityConfig)
            }
            
            context("When checking the Supported Networks property") {
                it("should return a valid value") {
                    expect(applePayConfiguration.supportedNetworks).to(equal([PKPaymentNetwork.visa, .masterCard]))
                }
            }
            
            context("When checking the Merchant Capabilities property") {
                it("should return a valid value") {
                    expect(applePayConfiguration.merchantCapabilities).to(equal([PKMerchantCapability.capability3DS, .capabilityEMV]))
                }
            }
            
            context("When checking the Supported Countries property") {
                it("should return a valid value") {
                    expect(applePayConfiguration.supportedCountries).to(equal(Set([OSPMTTestConfigurations.dummyString])))
                }
            }
        }
        
        describe("Given a configuration with both valid an invalid values") {
            beforeEach {
                applePayConfiguration = OSPMTApplePayConfiguration(source: OSPMTTestConfigurations.validNetworkCapabilityWithErrorConfig)
            }
            
            context("When checking the Supported Networks property") {
                it("should return a valid value without the invalid one") {
                    expect(applePayConfiguration.supportedNetworks).to(equal([PKPaymentNetwork.visa]))
                }
            }
            
            context("When checking the Merchant Capabilities property") {
                it("should return a valid value without the invalid one") {
                    expect(applePayConfiguration.merchantCapabilities).to(equal(.capability3DS))
                }
            }
        }
        
        describe("Given a configuration with an invalid value") {
            beforeEach {
                applePayConfiguration = OSPMTApplePayConfiguration(source: OSPMTTestConfigurations.invalidNetworkCapabilityConfig)
            }
            
            context("When checking the Supported Networks property") {
                it("should return a nil value") {
                    expect(applePayConfiguration.supportedNetworks).to(beNil())
                }
            }
            
            context("When checking the Merchant Capabilities property") {
                it("should return a nil value") {
                    expect(applePayConfiguration.merchantCapabilities).to(beNil())
                }
            }
        }
        
        describe("Given an empty configuration") {
            beforeEach {
                applePayConfiguration = OSPMTApplePayConfiguration(source: OSPMTTestConfigurations.emptyNetworkCapabilityConfig)
            }
            
            context("When checking the Supported Networks property") {
                it("should return a nil value") {
                    expect(applePayConfiguration.supportedNetworks).to(beNil())
                }
            }
            
            context("When checking the Merchant Capabilities property") {
                it("should return a nil value") {
                    expect(applePayConfiguration.merchantCapabilities).to(beNil())
                }
            }
            
            context("When checking the Supported Countries property") {
                it("should return a nil value") {
                    expect(applePayConfiguration.supportedCountries).to(beNil())
                }
            }
        }
        
        describe("Given a nil configuration") {
            beforeEach {
                applePayConfiguration = OSPMTApplePayConfiguration(source: OSPMTTestConfigurations.nilNetworkCapabilityConfig)
            }
            
            context("When checking the Supported Networks property") {
                it("should return a nil value") {
                    expect(applePayConfiguration.supportedNetworks).to(beNil())
                }
            }
            
            context("When checking the Merchant Capabilities property") {
                it("should return a nil value") {
                    expect(applePayConfiguration.merchantCapabilities).to(beNil())
                }
            }
            
            context("When checking the Supported Countries property") {
                it("should return a nil value") {
                    expect(applePayConfiguration.supportedCountries).to(beNil())
                }
            }
        }
    }
}
