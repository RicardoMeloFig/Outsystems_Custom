import Nimble
import Quick
@testable import OSPaymentsLib

class OSPMTGatewayFactorySpec: QuickSpec {
    override class func spec() {
        var model: OSPMTGatewayModel!
        var wrapper: OSPMTGatewayDelegate!
        
        describe("Given a OSPMTGatewayModel") {
            context("When given an invalid model") {
                it("No Wrapper should be created") {
                    model = OSPMTTestConfigurations.invalidGatewayModel
                    wrapper = OSPMTGatewayFactory.createWrapper(for: model)
                    
                    expect(wrapper).to(beNil())
                }
            }
            
            context("When given an invalid model Stripe") {
                it("No Wrapper should be created") {
                    model = OSPMTTestConfigurations.invalidStripeModel
                    wrapper = OSPMTGatewayFactory.createWrapper(for: model)
                    
                    expect(wrapper).to(beNil())
                }
            }
            
            context("When given a valid model Stripe") {
                it("a OSPMTStripeWrapper should be created") {
                    model = OSPMTTestConfigurations.validStripeModel
                    wrapper = OSPMTGatewayFactory.createWrapper(for: model)
                    
                    expect(wrapper).toNot(beNil())
                }
            }
        }
    }
}
