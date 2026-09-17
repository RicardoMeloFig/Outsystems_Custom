import SafariServices
import XCTest

@testable import OSInAppBrowserLib

final class OSIABSafariVCRouterAdapterTests: XCTestCase {
    let validURL = URL(string: "http://outsystems.com")!
    
    func test_handleOpen_validURL_doesReturnSFSafariViewController() {
        makeSUT().handleOpen(validURL) { XCTAssertNotNil($0) }
    }
    
    // MARK: Dismiss Style Tests
    
    func test_handleOpen_noDismissStyle_doesReturnWithDefaultDismissStyle() {
        makeSUT().handleOpen(validURL) {
            XCTAssertEqual(
                ($0 as? SFSafariViewController)?.dismissButtonStyle, OSIABDismissStyle.defaultValue.toSFSafariViewControllerDismissButtonStyle()
            )
        }
    }
    
    func test_handleOpen_withDismissStyle_doesReturnWithSpecifiedDismissStyle() {
        let options = OSIABSystemBrowserOptions.init(dismissStyle: .close)
        makeSUT(options).handleOpen(validURL) {
            XCTAssertEqual(
                ($0 as? SFSafariViewController)?.dismissButtonStyle, OSIABDismissStyle.close.toSFSafariViewControllerDismissButtonStyle()
            )
        }
    }

    // MARK: View Style Tests
    
    func test_handleOpen_noViewStyle_doesReturnWithDefaultViewStyle() {
        makeSUT().handleOpen(validURL) {
            XCTAssertEqual(
                $0.modalPresentationStyle, OSIABViewStyle.defaultValue.toModalPresentationStyle()
            )
        }
    }
    
    func test_handleOpen_withViewStyle_doesReturnWithSpecifiedViewStyle() {
        let options = OSIABSystemBrowserOptions.init(viewStyle: .pageSheet)
        makeSUT(options).handleOpen(validURL) {
            XCTAssertEqual(
                $0.modalPresentationStyle, OSIABViewStyle.pageSheet.toModalPresentationStyle()
            )
        }
    }
 
    // MARK: Animation Effect Tests
    
    func test_handleOpen_noAnimationEffect_doesReturnWithDefaultAnimationEffect() {
        makeSUT().handleOpen(validURL) {
            XCTAssertEqual(
                $0.modalTransitionStyle, OSIABAnimationEffect.defaultValue.toModalTransitionStyle()
            )
        }
    }
    
    func test_handleOpen_withAnimationEffect_doesReturnWithSpecifiedAnimationEffect() {
        let options = OSIABSystemBrowserOptions.init(animationEffect: .flipHorizontal)
        makeSUT(options).handleOpen(validURL) {
            XCTAssertEqual(
                $0.modalTransitionStyle, OSIABAnimationEffect.flipHorizontal.toModalTransitionStyle()
            )
        }
    }
       
    // MARK: Enable Bars Collapsing Tests
    
    func test_handleOpen_noEnableBarsCollapsingSet_doesReturnWithBarsCollapsing() {
        makeSUT().handleOpen(validURL) {
            XCTAssertEqual(
                ($0 as? SFSafariViewController)?.configuration.barCollapsingEnabled, true
            )
        }
    }
    
    func test_handleOpen_disableBarsCollapsing_doesReturnWithoutBarsCollapsing() {
        let options = OSIABSystemBrowserOptions.init(enableBarsCollapsing: false)
        makeSUT(options).handleOpen(validURL) {
            XCTAssertEqual(
                ($0 as? SFSafariViewController)?.configuration.barCollapsingEnabled, false
            )
        }
    }

    // MARK: Enable Readers Mode Tests
    
    func test_handleOpen_noEnableReadersModeSet_doesReturnWithReadersModeDisabled() {
        makeSUT().handleOpen(validURL) {
            XCTAssertEqual(
                ($0 as? SFSafariViewController)?.configuration.entersReaderIfAvailable, false
            )
        }
    }
    
    func test_handleOpen_enableReadersMode_doesReturnWithReadersModeEnabled() {
        let options = OSIABSystemBrowserOptions.init(enableReadersMode: true)
        makeSUT(options).handleOpen(validURL) {
            XCTAssertEqual(
                ($0 as? SFSafariViewController)?.configuration.entersReaderIfAvailable, true
            )
        }
    }
    
    // MARK: onBrowserPageLoad Callback Tests
    
    func test_handleOpen_withBrowserPageLoadConfigured_eventShouldBeTriggeredWhenLoaded() {
        let expectation = self.expectation(description: "Trigger onBrowserPageLoad Event")
        makeSUT(onBrowserPageLoad: { expectation.fulfill() }).handleOpen(validURL) {
            if let safariViewController = $0 as? SFSafariViewController {
                safariViewController.delegate?.safariViewController?(safariViewController, didCompleteInitialLoad: true)
            }
        }
        waitForExpectations(timeout: 1)
    }
    
    func test_handleOpen_withBrowserPageLoadConfigured_eventShouldNotBeTriggeredWhenNotLoaded() {
        let expectation = self.expectation(description: "Trigger onBrowserPageLoad Event")
        expectation.isInverted = true
        makeSUT(onBrowserPageLoad: { expectation.fulfill() }).handleOpen(validURL) {
            if let safariViewController = $0 as? SFSafariViewController {
                safariViewController.delegate?.safariViewController?(safariViewController, didCompleteInitialLoad: false)
            }
        }
        waitForExpectations(timeout: 1)
    }
    
    // MARK: onBrowserClose Callback Tests
    
    func test_handleOpen_withBrowserClosedConfigured_eventShouldBeTriggeredWhenClosed() {
        let expectation = self.expectation(description: "Trigger onBrowserClose Event")
        makeSUT(onBrowserClosed: { expectation.fulfill() }).handleOpen(validURL) {
            $0.dismiss(animated: false)
        }
        waitForExpectations(timeout: 1)
    }
    
    func test_handleOpen_withBrowserClosedConfigured_eventShouldBeTriggeredWhenDismiss() {
        let expectation = self.expectation(description: "Trigger onBrowserClose Event")
        makeSUT(onBrowserClosed: { expectation.fulfill() }).handleOpen(validURL) {
            if let presentationController = $0.presentationController {
                presentationController.delegate?.presentationControllerDidDismiss?(presentationController)
            }
        }
        waitForExpectations(timeout: 1)
    }
}

private extension OSIABSafariVCRouterAdapterTests {
    func makeSUT(_ options: OSIABSystemBrowserOptions = .init(), onBrowserPageLoad: @escaping () -> Void = {}, onBrowserClosed: @escaping () -> Void = {}) -> OSIABSafariViewControllerRouterAdapter {
        .init(options, onBrowserPageLoad: onBrowserPageLoad, onBrowserClosed: onBrowserClosed)
    }
}
