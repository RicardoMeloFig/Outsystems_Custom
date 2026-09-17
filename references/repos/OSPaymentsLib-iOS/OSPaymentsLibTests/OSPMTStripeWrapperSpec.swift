import Nimble
import PassKit
import Quick
@testable import OSPaymentsLib

class MockStripeAPIDelegate: OSPMTStripeAPIDelegate {
    var paymentMethodId: String?
    var error: OSPMTError?
    
    func set(_ publishableKey: String) {}
    
    func getPaymentMethodId(from payment: PKPayment, _ completion: @escaping (Result<String, OSPMTError>) -> Void) {
        if let paymentMethodId = paymentMethodId {
            completion(.success(paymentMethodId))
        } else if let error = error {
            completion(.failure(error))
        }
    }
}

enum MockError: Error {
    case generic
}

class MockURLProtocol: URLProtocol {
    override class func canInit(with request: URLRequest) -> Bool {
        return true
    }
    
    override class func canonicalRequest(for request: URLRequest) -> URLRequest {
        return request
    }
    
    static var requestHandler: ((URLRequest) throws -> (HTTPURLResponse, Data?))?
      
    override func startLoading() {
      guard let handler = MockURLProtocol.requestHandler else {
        fatalError("Handler is unavailable.")
      }
        
      do {
        let (response, data) = try handler(request)
        client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
        
        if let data = data {
          client?.urlProtocol(self, didLoad: data)
        }
        
        client?.urlProtocolDidFinishLoading(self)
      } catch {
        client?.urlProtocol(self, didFailWithError: error)
      }
    }
    
    override func stopLoading() {}
}

class OSPMTStripeWrapperSpec: QuickSpec {
    override class func spec() {        
        var mockAPIDelegate: MockStripeAPIDelegate!
        var mockPayment: PKPayment!
        var mockDetailsModel: OSPMTDetailsModel!
        var mockURL: URL!
        
        var stripeWrapper: OSPMTStripeWrapper!
        
        describe("Given a Stripe API Delegate object") {
            beforeEach {
                mockAPIDelegate = MockStripeAPIDelegate()
                
                let configuration = URLSessionConfiguration.default
                configuration.protocolClasses = [MockURLProtocol.self]
                let urlSession = URLSession.init(configuration: configuration)
                
                mockPayment = PKPayment()
                
                mockDetailsModel = OSPMTTestConfigurations.dummyDetailsModel
                
                mockURL = URL(string: OSPMTTestConfigurations.dummyString)!
                
                stripeWrapper = OSPMTStripeWrapper(
                    urlRequest: URLRequest(url: mockURL),
                    urlSession: urlSession,
                    publishableKey: OSPMTTestConfigurations.dummyString,
                    apiDelegate: mockAPIDelegate
                )
            }
            
            context("When there's an error while obtaining a Payment Method ID") {
                it("It should return a Stripe Payment Method Creation error") {
                    mockAPIDelegate.error = OSPMTError.stripePaymentMethodCreation

                    stripeWrapper.process(mockPayment, with: mockDetailsModel, and: OSPMTTestConfigurations.dummyAccessToken) { result in
                        switch result {
                        case .failure(let errorResult):
                            expect(errorResult).to(equal(OSPMTError.stripePaymentMethodCreation))
                        case .success:
                            fail()
                        }
                    }
                }
            }
            
            context("When a Payment Method ID is returned") {
                beforeEach {
                    mockAPIDelegate.paymentMethodId = OSPMTTestConfigurations.dummyString
                }
                context("And there's an error on processing the Payment Request") {
                    it("It should return an 100 Status Code Error") {
                        MockURLProtocol.requestHandler = { request in
                            guard let url = request.url, url == mockURL else {
                                throw MockError.generic
                            }
                            
                            let response = HTTPURLResponse(url: mockURL, statusCode: 100, httpVersion: nil, headerFields: nil)!
                            return (response, OSPMTTestConfigurations.dummyString.data(using: .utf8))
                        }
                        
                        stripeWrapper.process(mockPayment, with: mockDetailsModel, and: OSPMTTestConfigurations.dummyAccessToken) { result in
                            switch result {
                            case .failure(let errorResult):
                                expect(errorResult).to(equal(.paymentIssue))
                            case .success:
                                fail()
                            }
                        }
                    }
                    
                    it("It should return an Failed Request Error") {
                        MockURLProtocol.requestHandler = { request in
                            guard let url = request.url,
                                    url == mockURL,
                                    let resultData = try? JSONEncoder().encode(OSPMTTestConfigurations.invalidPaymentProcessResultModel)
                            else {
                                throw MockError.generic
                            }
                            
                            let response = HTTPURLResponse(url: mockURL, statusCode: 200, httpVersion: nil, headerFields: nil)!
                            return (response, resultData)
                        }
                        
                        stripeWrapper.process(mockPayment, with: mockDetailsModel, and: OSPMTTestConfigurations.dummyAccessToken) { result in
                            switch result {
                            case .failure(let errorResult):
                                expect(errorResult).to(equal(.paymentIssue))
                            case .success:
                                fail()
                            }
                        }
                    }
                }
                
                context("And there's the Payment Request process is successful") {
                    it("It should return a success text") {
                        MockURLProtocol.requestHandler = { request in
                            guard let url = request.url,
                                    url == mockURL,
                                    let resultData = try? JSONEncoder().encode(OSPMTTestConfigurations.validPaymentProcessResultModel)
                            else {
                                throw MockError.generic
                            }

                            let response = HTTPURLResponse(url: mockURL, statusCode: 200, httpVersion: nil, headerFields: nil)!
                            return (response, resultData)
                        }

                        stripeWrapper.process(mockPayment, with: mockDetailsModel, and: OSPMTTestConfigurations.dummyAccessToken) { result in
                            switch result {
                            case .success(let resultModel):
                                expect(resultModel).to(equal(OSPMTTestConfigurations.validPaymentProcessResultModel))
                            case .failure:
                                fail()
                            }
                        }
                    }
                }
            }
        }
    }
}
