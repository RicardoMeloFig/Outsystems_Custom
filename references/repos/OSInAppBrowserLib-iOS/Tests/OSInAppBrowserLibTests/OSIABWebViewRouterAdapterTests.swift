import XCTest

@testable import OSInAppBrowserLib

final class OSIABWebViewRouterAdapterTests: XCTestCase {
    let validURL = URL(string: "http://outsystems.com")!
    
    func test_handleOpen_validURL_doesReturnHostingViewController() {
        makeSUT().handleOpen(validURL) { XCTAssertNotNil($0) }
    }
    
    // MARK: Clear Cache Tests
    
    func test_handleOpen_defaultClearCacheValue_cacheIsCleared() {
        let cacheManager = OSIABCacheManagerStub(cacheWasCleared: false)
        XCTAssertFalse(cacheManager.cacheWasCleared)
        
        makeSUT(cacheManager: cacheManager).handleOpen(validURL) { _ in }
        XCTAssertTrue(cacheManager.cacheWasCleared)
    }
    
    func test_handleOpen_disableClearCache_cacheIsNotCleared() {
        let cacheManager = OSIABCacheManagerStub(cacheWasCleared: false)
        XCTAssertFalse(cacheManager.cacheWasCleared)
        
        let options = OSIABWebViewOptions(clearCache: false)
        makeSUT(options, cacheManager: cacheManager).handleOpen(validURL) { _ in }
        
        XCTAssertFalse(cacheManager.cacheWasCleared)
    }
    
    // MARK: Clear Session Cache Tests
    
    func test_handleOpen_defaultClearSessionCacheValue_andDisableClearCache_sessionCacheIsNotCleared() {
        let cacheManager = OSIABCacheManagerStub(sessionCacheWasCleared: false)
        XCTAssertFalse(cacheManager.sessionCacheWasCleared)
        
        let options = OSIABWebViewOptions(clearCache: false)
        makeSUT(options, cacheManager: cacheManager).handleOpen(validURL) { _ in }
        XCTAssertTrue(cacheManager.sessionCacheWasCleared)
    }
    
    func test_handleOpen_defaultClearSessionCacheValue_butEnableClearCache_sessionCacheIsNotCleared() {
        let cacheManager = OSIABCacheManagerStub(sessionCacheWasCleared: false)
        XCTAssertFalse(cacheManager.sessionCacheWasCleared)
        
        makeSUT(cacheManager: cacheManager).handleOpen(validURL) { _ in }
        
        XCTAssertFalse(cacheManager.sessionCacheWasCleared)
    }
    
    func test_handleOpen_disableClearSessionCache_sessionCacheIsNotCleared() {
        let cacheManager = OSIABCacheManagerStub(sessionCacheWasCleared: false)
        XCTAssertFalse(cacheManager.sessionCacheWasCleared)
        
        let options = OSIABWebViewOptions(clearSessionCache: false)
        makeSUT(options, cacheManager: cacheManager).handleOpen(validURL) { _ in }
        
        XCTAssertFalse(cacheManager.sessionCacheWasCleared)
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
        let options = OSIABWebViewOptions(viewStyle: .pageSheet)
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
        let options = OSIABWebViewOptions(animationEffect: .flipHorizontal)
        makeSUT(options).handleOpen(validURL) {
            XCTAssertEqual(
                $0.modalTransitionStyle, OSIABAnimationEffect.flipHorizontal.toModalTransitionStyle()
            )
        }
    }
    
    // MARK: onBrowserClose Callback Tests
    
    func test_handleOpen_withBrowserClosedConfigured_eventShouldBeTriggeredWhenDismissed() {
        var isBrowserAlreadyClosed = false
        let expectation = self.expectation(description: "Trigger onBrowserClose Event")
        makeSUT(onBrowserClosed: { param in
            isBrowserAlreadyClosed = param
            expectation.fulfill()
        }).handleOpen(validURL) {
            if let presentationController = $0.presentationController {
                presentationController.delegate?.presentationControllerDidDismiss?(presentationController)
            }
        }
        waitForExpectations(timeout: 1)
        XCTAssertTrue(isBrowserAlreadyClosed)
    }
    
    func test_handleOpen_whenDismissingViewController_eventShouldBeTriggered() {
        var isBrowserAlreadyClosed = false
        let expectation = self.expectation(description: "Trigger onBrowserClose Event")
        makeSUT { param in
            isBrowserAlreadyClosed = param
            expectation.fulfill()
        }.handleOpen(validURL) {
            $0.dismiss(animated: false)
        }
        waitForExpectations(timeout: 1)
        XCTAssertTrue(isBrowserAlreadyClosed)
    }
}

private extension OSIABWebViewRouterAdapterTests {
    func makeSUT(
        _ options: OSIABWebViewOptions = .init(),
        cacheManager: OSIABCacheManager = OSIABCacheManagerStub(),
        onBrowserClosed: @escaping (Bool) -> Void = { _ in }
    ) -> OSIABWebViewRouterAdapter {
        .init(
            options: options, 
            cacheManager: cacheManager,
            callbackHandler: .init(
                onDelegateURL: { _ in },
                onDelegateAlertController: { _ in },
                onBrowserPageLoad: {},
                onBrowserClosed: onBrowserClosed,
                onBrowserPageNavigationCompleted: { _ in }
            )
        )
    }
}
