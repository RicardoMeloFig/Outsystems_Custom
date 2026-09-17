import OSInAppBrowserLib
import SafariServices
import XCTest

final class OSIABEngineTests: XCTestCase {
    let url = URL(string: "https://www.outsystems.com/")!
    
    // MARK: - Open in External Browser Tests
    
    func test_open_externalBrowserWithoutIssues_doesOpen() {
        let routerSpy = OSIABExternalRouterSpy(shouldOpenSafari: true)
        makeSUT().openExternalBrowser(url, routerDelegate: routerSpy) { XCTAssertTrue($0) }
    }
    
    func test_open_externalBrowserWithIssues_doesNotOpen() {
        let routerSpy = OSIABExternalRouterSpy(shouldOpenSafari: false)
        makeSUT().openExternalBrowser(url, routerDelegate: routerSpy) { XCTAssertFalse($0) }
    }
    
    // MARK: - Open in System Browser Tests
    
    func test_open_systemBrowserWithoutIssues_doesOpen() {
        let routerSpy = OSIABSystemRouterSpy(shouldOpen: UIViewController())
        makeSUT().openSystemBrowser(url, routerDelegate: routerSpy) { XCTAssertNotNil($0) }
    }
    
    // MARK: - Open in Web View Tests
    
    func test_open_webViewWithoutIssues_doesOpen() {
        let routerSpy = OSIABWebViewRouterSpy(shouldOpen: UIViewController())
        makeSUT().openWebView(url, routerDelegate: routerSpy) { XCTAssertNotNil($0) }
    }
}

extension OSIABEngineTests {
    func makeSUT() -> OSIABEngine<OSIABExternalRouterSpy, OSIABSystemRouterSpy, OSIABWebViewRouterSpy> { .init() }
}
