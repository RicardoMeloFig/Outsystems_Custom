import Nimble
import Quick
@testable import OSPaymentsLib

class OSPMTMockCallback: OSPMTCallbackDelegate {
    var successText: String?
    var error: OSPMTError?
    
    func callback(result: String?, error: OSPMTError?) {
        self.successText = result
        self.error = error
    }
}

class OSPMTMockHandler: OSPMTHandlerDelegate {
    var successText: String?
    var error: OSPMTError?
    var scopeModel: OSPMTScopeModel?
    
    init(successText: String? = nil, error: OSPMTError? = nil, scopeModel: OSPMTScopeModel? = nil) {
        self.successText = successText
        self.error = error
        self.scopeModel = scopeModel
    }
    
    func setupConfiguration() -> Result<String, OSPMTError> {
        if let error = self.error {
            return .failure(error)
        } else {
            return .success(self.successText ?? "")
        }
    }
    
    func checkWalletAvailability() -> OSPMTError? {
        return self.error
    }
    
    func set(_ detailsModel: OSPMTDetailsModel, and accessToken: String?, _ completion: @escaping OSPMTCompletionHandler) {
        if let error = self.error {
            completion(.failure(error))
        } else if let scopeModel = self.scopeModel {
            completion(.success(scopeModel))
        }
    }
}

class OSPMTPaymentsSpec: QuickSpec {
    override class func spec() {
        var mockDelegate: OSPMTMockCallback!
        var mockHandler: OSPMTMockHandler!
        var payments: OSPMTPayments!
        
        describe("Given a delegate") {
            beforeEach {
                mockDelegate = OSPMTMockCallback()
            }
            
            describe("and a correctly configured handler") {
                context("When the OSPMTPayments object is initialized") {
                    beforeEach {
                        mockHandler = OSPMTMockHandler(
                            successText: OSPMTTestConfigurations.dummyString, scopeModel: OSPMTTestConfigurations.dummyScopeModel
                        )
                        payments = OSPMTPayments(delegate: mockDelegate, handler: mockHandler)
                    }
                    
                    it("Setup configuration should return a successful text message") {
                        payments.setupConfiguration()
                        
                        expect(mockDelegate.successText).to(equal(OSPMTTestConfigurations.dummyString))
                        expect(mockDelegate.error).to(beNil())
                    }
                    
                    it("Check wallet setup should return a successful empty message") {
                        payments.checkWalletSetup()
                        
                        expect(mockDelegate.successText).to(beEmpty())
                        expect(mockDelegate.error).to(beNil())
                    }
                    
                    it("Set payment details should return a successful scope model") {
                        if let detailsData = try? JSONSerialization.data(withJSONObject: OSPMTTestConfigurations.validDetailModel),
                            let detailsString = String(data: detailsData, encoding: .utf8) {
                            payments.set(detailsString)
                            
                            expect(mockDelegate.error).to(beNil())
                            
                            let result = payments.encode(OSPMTTestConfigurations.dummyScopeModel)
                            if case let .success(scopeText) = result,
                               let scopeTextData = scopeText.data(using: .utf8),
                               let successTextData = mockDelegate.successText?.data(using: .utf8),
                               let scopeObject = try? JSONSerialization.jsonObject(with: scopeTextData) as? [String: Any],
                               let successTextObject = try? JSONSerialization.jsonObject(with: successTextData) as? [String: Any] {
                                expect(NSDictionary(dictionary: scopeObject)).to(equal(NSDictionary(dictionary: successTextObject)))
                            } else {
                                fail()
                            }
                        } else {
                            fail()
                        }
                    }
                }
                
                context("When the OSPMTPayments object is initialized") {
                    context("""
                            And payment details is set so that it should use the billing information
                            from the configuration and custom shipping information
                            """) {
                        beforeEach {
                            mockHandler = OSPMTMockHandler(
                                successText: OSPMTTestConfigurations.dummyString,
                                scopeModel: OSPMTTestConfigurations.useConfigurationBillingScopeModel
                            )
                            payments = OSPMTPayments(delegate: mockDelegate, handler: mockHandler)
                        }
                        
                        it("Should return the values as expected") {
                            if let detailsData = try? JSONSerialization.data(
                                withJSONObject: OSPMTTestConfigurations.useConfigBillingContactDetailModel
                            ),
                               let detailsString = String(data: detailsData, encoding: .utf8) {
                                payments.set(detailsString)
                                
                                expect(mockDelegate.error).to(beNil())
                                
                                let result = payments.encode(OSPMTTestConfigurations.useConfigurationBillingScopeModel)
                                if case let .success(scopeText) = result,
                                   let scopeTextData = scopeText.data(using: .utf8),
                                   let successTextData = mockDelegate.successText?.data(using: .utf8),
                                   let scopeObject = try? JSONSerialization.jsonObject(with: scopeTextData) as? [String: Any],
                                   let successTextObject = try? JSONSerialization.jsonObject(with: successTextData) as? [String: Any] {
                                    expect(NSDictionary(dictionary: scopeObject)).to(equal(NSDictionary(dictionary: successTextObject)))
                                } else {
                                    fail()
                                }
                            } else {
                                fail()
                            }
                        }
                    }
                }
            }
            
            describe("and a handler configured with an error") {
                context("When the OSPMTPayments object is initialized") {
                    beforeEach {
                        mockHandler = OSPMTMockHandler(error: .invalidConfiguration)
                        payments = OSPMTPayments(delegate: mockDelegate, handler: mockHandler)
                    }
                    
                    it("Setup configuration should return an error") {
                        payments.setupConfiguration()
                        
                        expect(mockDelegate.error).to(equal(.invalidConfiguration))
                        expect(mockDelegate.successText).to(beNil())
                    }
                    
                    it("Check wallet setup should return an error") {
                        payments.checkWalletSetup()
                        
                        expect(mockDelegate.error).to(equal(.invalidConfiguration))
                        expect(mockDelegate.successText).to(beNil())
                    }
                    
                    it("Set payment details should return an error") {
                        if let detailsData = try? JSONSerialization.data(withJSONObject: OSPMTTestConfigurations.validDetailModel),
                            let detailsString = String(data: detailsData, encoding: .utf8) {
                            payments.set(detailsString)
                            
                            expect(mockDelegate.error).to(equal(.invalidConfiguration))
                            expect(mockDelegate.successText).to(beNil())
                        } else {
                            fail()
                        }
                    }
                }
            }
            
            describe("and set payment details is called") {
                beforeEach {
                    mockHandler = OSPMTMockHandler(scopeModel: OSPMTTestConfigurations.dummyScopeModel)
                    payments = OSPMTPayments(delegate: mockDelegate, handler: mockHandler)
                }
                context("When an invalid detail model is passed as a parameter") {
                    it("should return a error") {
                        if let detailsData = try? JSONSerialization.data(withJSONObject: OSPMTTestConfigurations.invalidStatusDetailModel),
                            let detailsString = String(data: detailsData, encoding: .utf8) {
                            payments.set(detailsString)
                            
                            expect(mockDelegate.error).to(equal(.invalidDecodeDetails))
                            expect(mockDelegate.successText).to(beNil())
                        } else {
                            fail()
                        }
                    }
                }
            }
            
            describe("and an Apple Pay Handler correctly configured") {
                context("When the OSPMTPayments object is initialized") {
                    it("Setup configuration should return a non empty text message") {
                        payments = OSPMTPayments(applePayWithDelegate: mockDelegate, andConfiguration: OSPMTTestConfigurations.fullConfig)
                        
                        payments.setupConfiguration()
                        
                        expect(mockDelegate.successText).toNot(beEmpty())
                        expect(mockDelegate.error).to(beNil())
                    }
                    
                    context("And Payment Gateway information is provided") {
                        it("Setup configuration should return a non empty text message") {
                            payments = OSPMTPayments(applePayWithDelegate: mockDelegate, andConfiguration: OSPMTTestConfigurations.dummyGatewayConfig)
                            
                            payments.setupConfiguration()
                            
                            expect(mockDelegate.successText).toNot(beEmpty())
                            expect(mockDelegate.error).to(beNil())
                        }
                    }
                    
                    context("And a Stripe Payment Gateway information is provided") {
                        it("Setup configuration should return a non empty text message") {
                            payments = OSPMTPayments(
                                applePayWithDelegate: mockDelegate, andConfiguration: OSPMTTestConfigurations.stripeGatewayConfig
                            )
                            
                            payments.setupConfiguration()
                            
                            expect(mockDelegate.successText).toNot(beEmpty())
                            expect(mockDelegate.error).to(beNil())
                        }
                    }
                }
            }
        }
    }
}
