import XCTest
import WebKit

@testable import OSInAppBrowserLib

final class OSIABViewModelTests: XCTestCase {
    private var url: URL = .init(string: "https://outsystems.com/")!
    private var secondURL: URL = .init(string: "https://google.com/")!
    
    // MARK: MediaTypesRequiringUserActionForPlayback Tests
    func test_createModel_noMediaTypesRequiringUserActionForPlayback_usesNone() {
        XCTAssertEqual(makeSUT(url).webView.configuration.mediaTypesRequiringUserActionForPlayback, [])
    }
    
    func test_createModel_withMediaTypesRequiringUserActionForPlayback_usesSpecifiedValue() {
        XCTAssertEqual(
            makeSUT(url, mediaTypesRequiringUserActionForPlayback: .all).webView.configuration.mediaTypesRequiringUserActionForPlayback,
                .all
        )
    }
    
    // MARK: IgnoresViewportScaleLimits Tests
    func test_createModel_noIgnoresViewportScaleLimits_setsToFalse() {
        XCTAssertFalse(makeSUT(url).webView.configuration.ignoresViewportScaleLimits)
    }
    
    func test_createModel_withIgnoresViewportScaleLimitsEnabled_setsToValueSpecified() {
        XCTAssertTrue(makeSUT(url, ignoresViewportScaleLimits: true).webView.configuration.ignoresViewportScaleLimits)
    }
    
    // MARK: AllowsInlineMediaPlayback Tests
    func test_createModel_noAllowsInlineMediaPlayback_setsToFalse() {
        XCTAssertFalse(makeSUT(url).webView.configuration.allowsInlineMediaPlayback)
    }
    
    func test_createModel_withAllowsInlineMediaPlaybackEnabled_setsToValueSpecified() {
        XCTAssertTrue(makeSUT(url, allowsInlineMediaPlayback: true).webView.configuration.allowsInlineMediaPlayback)
    }
    
    // MARK: SurpressesIncrementalRendering Tests
    func test_createModel_noSurpressesIncrementalRendering_setsToFalse() {
        XCTAssertFalse(makeSUT(url).webView.configuration.suppressesIncrementalRendering)
    }
    
    func test_createModel_withSurpressesIncrementalRenderingEnabled_setsToValueSpecified() {
        XCTAssertTrue(makeSUT(url, surpressesIncrementalRendering: true).webView.configuration.suppressesIncrementalRendering)
    }
    
    // MARK: ScrollViewBounds Tests
    func test_createModel_noScrollViewBounds_setsToFalse() {
        XCTAssertFalse(makeSUT(url).webView.scrollView.bounces)
    }
    
    func test_createModel_withScrollViewBoundsEnabled_setsToValueSpecified() {
        XCTAssertTrue(makeSUT(url, scrollViewBounds: true).webView.scrollView.bounces)
    }
    
    // MARK: CustomUserAgent Tests
    func test_createModel_settingCustomUserAgent_setsToSpecifiedValue() {
        let newUserAgent = "New User Agent"
        XCTAssertEqual(makeSUT(url, customUserAgent: newUserAgent).webView.customUserAgent, newUserAgent)
    }
    
    // MARK: ShowURL Tests
    func test_createModel_noShowURL_setsAddressLabel() {
        XCTAssertEqual(makeSUT(url).addressLabel, url.absoluteString)
    }
    
    func test_createModel_setShowURLToFalse_addressLabelNotSet() {
        XCTAssertEqual(makeSUT(url, showURL: false).addressLabel, "")
    }
    
    // MARK: ShowToolbar Tests
    func test_createModel_noShowToolbar_setsToolbarPositionValue() {
        XCTAssertNotNil(makeSUT(url).toolbarPosition)
    }
    
    func test_createModel_setsShowToolbarToFalse_toolbarPositionValueShouldBeNil() {
        XCTAssertNil(makeSUT(url, showToolbar: false).toolbarPosition)
    }
    
    // MARK: ToolbarPosition Tests
    func test_createModel_noToolbarPosition_setsToDefault() {
        XCTAssertEqual(makeSUT(url).toolbarPosition, OSIABToolbarPosition.defaultValue)
    }
    
    func test_createModel_setsToolbarPosition_setsToSpecificValue() {
        XCTAssertEqual(makeSUT(url, toolbarPosition: .bottom).toolbarPosition, OSIABToolbarPosition.bottom)
    }
    
    // MARK: ShowNavigationButtons Tests
    func test_createModel_noShowNavigationButtons_setsToDefault() {
        XCTAssertTrue(makeSUT(url).showNavigationButtons)
    }
    
    func test_createModel_disablesShowNavigationButtons_setsToFalse() {
        XCTAssertFalse(makeSUT(url, showNavigationButtons: false).showNavigationButtons)
    }
    
    // MARK: LeftToRight Tests
    func test_createModel_noLeftToRighthowNavigationButtons_setsToDefault() {
        XCTAssertFalse(makeSUT(url).leftToRight)
    }
    
    func test_createModel_enablesLeftToRight_setsToTrue() {
        XCTAssertTrue(makeSUT(url, leftToRight: true).leftToRight)
    }
    
    // MARK: URL Address Tests
    
    func test_createModel_whenChangingURL_urlAddressIsModified() {
        let sut = makeSUT(url)
        sut.webView(sut.webView, decidePolicyFor: OSIABNavigationActionStub(secondURL)) { _ in }
        XCTAssertEqual(sut.addressLabel, secondURL.absoluteString)
    }
    
    func test_createModel_withShowURLSetToFalse_whenChangingURL_urlAddressIsNotSet() {
        let sut = makeSUT(url, showURL: false)
        sut.webView(sut.webView, decidePolicyFor: OSIABNavigationActionStub(secondURL)) { _ in }
        XCTAssertEqual(sut.addressLabel, "")
    }
    
    func test_createModel_withShowToolbarSetToFalse_whenChangingURL_urlAddressIsNotSet() {
        let sut = makeSUT(url, showToolbar: false)
        sut.webView(sut.webView, decidePolicyFor: OSIABNavigationActionStub(secondURL)) { _ in }
        XCTAssertEqual(sut.addressLabel, "")
    }
    
    // MARK: Load URL Tests
    
    func test_loadURL_webViewShouldUpdate() {
        let sut = makeSUT(url)
        
        XCTAssertNil(sut.webView.url)
        sut.loadURL()
        XCTAssertEqual(sut.webView.url, url)
    }
    
    func test_loadURL_withCustomHeaders() {
        let mockWebView = MockWKWebView(frame: .zero, configuration: WKWebViewConfiguration())
        let sut = makeSUT(url, webView: mockWebView, customHeaders: ["Custom-Header": "Value"])
        
        sut.loadURL()
        
        XCTAssertEqual(mockWebView.lastRequest?.value(forHTTPHeaderField: "Custom-Header"), "Value")
    }
    
    // MARK: Close Button Pressed
    
    func test_closeButtonPressed_triggersTheBrowserClosedEvent() {
        var closed = false
        var isBrowserAlreadyClosed = true
        let sut = makeSUT(url, onBrowserClosed: { param in
            isBrowserAlreadyClosed = param
            closed = true
        })
        sut.closeButtonPressed()
        XCTAssertTrue(closed)
        XCTAssertFalse(isBrowserAlreadyClosed)
    }
    
    // MARK: Navigation Tests
    
    func test_navigateToNewPage_whenMainDocumentURLDoesNotMatchURL_cancelShouldBeReturned() {
        let sut = makeSUT(url)
        var resultAction: WKNavigationActionPolicy = .allow
        sut.webView(sut.webView, decidePolicyFor: OSIABNavigationActionStub(url, mainDocumentURL: secondURL)) { resultAction = $0 }
        XCTAssertEqual(resultAction, .cancel)
    }
    
    func test_navigateToMailToScheme_shouldDelegateURL() {
        var shouldDelegateURL = false
        let mailToURL: URL = .init(string: "mailto://mail@outsystems.com/")!
        var resultAction: WKNavigationActionPolicy = .allow
        
        let sut = makeSUT(url, onDelegateURL: { urlToDelegate in
            XCTAssertEqual(mailToURL, urlToDelegate)
            shouldDelegateURL = true
        })
        sut.webView(sut.webView, decidePolicyFor: OSIABNavigationActionStub(mailToURL)) { resultAction = $0 }
        XCTAssertTrue(shouldDelegateURL)
        XCTAssertEqual(resultAction, .cancel)
    }
    
    func test_navigateToNewPage_whenTargetFrameIsSet_allowShouldBeReturned() {
        var resultAction: WKNavigationActionPolicy = .cancel
        
        let sut = makeSUT(url)
        sut.webView(sut.webView, decidePolicyFor: OSIABNavigationActionStub(secondURL, useTargetFrame: true)) { resultAction = $0 }
        XCTAssertEqual(resultAction, .allow)
    }
    
    func test_navigateToNewPage_onBrowserPageNavigationCompletedEventShouldBeTrigged() {
        var result: String? = nil
        let expectation = self.expectation(description: "Trigger onBrowserPageNavigationCompleted Event")

        let sut = makeSUT(url, onBrowserPageNavigationCompleted: { data in
            result = data
            expectation.fulfill()
        })
        sut.webView(sut.webView, didFinish: nil)
        sut.webView(sut.webView, didFinish: nil)
        waitForExpectations(timeout: 1)
        XCTAssertEqual(url.absoluteString, result)
    }
    
    // MARK: Browser Page Load Event Tests
    
    func test_navigateToURL_whenFinished_triggerPageLoadEventOnFirstTime() {
        var pageLoaded = false
        let sut = makeSUT(url, onBrowserPageLoad: { pageLoaded = true })
        sut.webView(sut.webView, didFinish: nil)
        XCTAssertTrue(pageLoaded)
    }
    
    func test_navigateToURLTwice_triggerPageLoadEventIsTriggeredOnlyOnTheFirstTime() {
        var pageLoaded = false
        let sut = makeSUT(url, onBrowserPageLoad: { pageLoaded = true })
        sut.webView(sut.webView, didFinish: nil)
        pageLoaded = false
        sut.webView(sut.webView, didFinish: nil)
        XCTAssertFalse(pageLoaded)
    }
    
    // MARK: Failed Navigation Tests
    
    func test_provisionalNavigationToURLFails_errorIsTriggered() {
        let sut = makeSUT(url)
        sut.webView(sut.webView, didFailProvisionalNavigation: nil, withError: NSError(domain: "testing", code: NSURLErrorUnknown))
        XCTAssertNotNil(sut.error)
    }
    
    func test_provisionalNavigationToURLFailsDueToCancelling_errorIsNotTriggered() {
        let sut = makeSUT(url)
        sut.webView(sut.webView, didFailProvisionalNavigation: nil, withError: NSError(domain: "testing", code: NSURLErrorCancelled))
        XCTAssertNil(sut.error)
    }
    
    func test_navigationToURLFails_errorIsTriggered() {
        let sut = makeSUT(url)
        sut.webView(sut.webView, didFail: nil, withError: NSError(domain: "testing", code: NSURLErrorUnknown))
        XCTAssertNotNil(sut.error)
    }
    
    func test_navigationToURLFailsDueToCancelling_errorIsNotTriggered() {
        let sut = makeSUT(url)
        sut.webView(sut.webView, didFail: nil, withError: NSError(domain: "testing", code: NSURLErrorCancelled))
        XCTAssertNil(sut.error)
    }
    
    // MARK: Delegate Alert Controller Event Tests
    
    func test_whenOpeningAnAlertPanel_theAlertIsDelegated() {
        let alertMessage = "Body"
        var alertController: UIAlertController?
        let sut = makeSUT(url, onDelegateAlertController: { alertController = $0 })
        sut.webView(sut.webView, runJavaScriptAlertPanelWithMessage: alertMessage, initiatedByFrame: .init(), completionHandler: {})
        XCTAssertEqual(alertController?.message, alertMessage)
        XCTAssertEqual(alertController?.actions.count, 1)
    }
    
    func test_whenOpeningAnConfirmPanel_theAlertIsDelegated() {
        let alertMessage = "Body"
        var alertController: UIAlertController?
        let sut = makeSUT(url, onDelegateAlertController: { alertController = $0 })
        sut.webView(sut.webView, runJavaScriptConfirmPanelWithMessage: alertMessage, initiatedByFrame: .init()) { _ in }
        XCTAssertEqual(alertController?.message, alertMessage)
        XCTAssertEqual(alertController?.actions.count, 2)
    }
    
    func test_whenOpeningAnTextInputPanel_theAlertIsDelegated() {
        let alertMessage = "Body"
        let defaultText = "Some random text"
        var alertController: UIAlertController?
        let sut = makeSUT(url, onDelegateAlertController: { alertController = $0 })
        sut.webView(sut.webView, runJavaScriptTextInputPanelWithPrompt: alertMessage, defaultText: defaultText, initiatedByFrame: .init()) { _ in }
        XCTAssertEqual(alertController?.message, alertMessage)
        XCTAssertEqual(alertController?.actions.count, 2)
        XCTAssertEqual(alertController?.textFields?.first?.text, defaultText)
        
    }
}

private extension OSIABViewModelTests {
    func makeSUT(
        _ url: URL,
        webView: WKWebView? = nil,
        customHeaders: [String: String]? = nil,
        mediaTypesRequiringUserActionForPlayback: WKAudiovisualMediaTypes = [],
        ignoresViewportScaleLimits: Bool = false,
        allowsInlineMediaPlayback: Bool = false,
        surpressesIncrementalRendering: Bool = false,
        scrollViewBounds: Bool = false,
        customUserAgent: String? = nil,
        showURL: Bool = true,
        showToolbar: Bool = true,
        toolbarPosition: OSIABToolbarPosition = .defaultValue,
        showNavigationButtons: Bool = true,
        leftToRight: Bool = false,
        onDelegateURL: @escaping (URL) -> Void = { _ in },
        onDelegateAlertController: @escaping (UIAlertController) -> Void = { _ in },
        onBrowserPageLoad: @escaping () -> Void = {},
        onBrowserClosed: @escaping (Bool) -> Void = { _ in },
        onBrowserPageNavigationCompleted: @escaping (String?) -> Void = { _ in }
    ) -> OSIABWebViewModel {
        let configurationModel = OSIABWebViewConfigurationModel(
            mediaTypesRequiringUserActionForPlayback, ignoresViewportScaleLimits, allowsInlineMediaPlayback, surpressesIncrementalRendering
        )
        
        return .init(
            url: url,
            customHeaders: customHeaders,
            webView: webView ?? WKWebView(frame: .zero, configuration: configurationModel.toWebViewConfiguration()),
            scrollViewBounces: scrollViewBounds,
            customUserAgent: customUserAgent,
            uiModel: .init(
                showURL: showURL,
                showToolbar: showToolbar,
                toolbarPosition: toolbarPosition,
                showNavigationButtons: showNavigationButtons,
                leftToRight: leftToRight
            ),
            callbackHandler: .init(
                onDelegateURL: onDelegateURL,
                onDelegateAlertController: onDelegateAlertController,
                onBrowserPageLoad: onBrowserPageLoad,
                onBrowserClosed: onBrowserClosed,
                onBrowserPageNavigationCompleted: onBrowserPageNavigationCompleted
            )
        )
    }
}

class MockWKWebView: WKWebView {
    var lastRequest: URLRequest?
    override func load(_ request: URLRequest) -> WKNavigation? {
        lastRequest = request
        return nil
    }
}
