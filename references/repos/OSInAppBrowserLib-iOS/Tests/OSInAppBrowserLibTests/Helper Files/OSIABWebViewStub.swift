import WebKit

class OSIABNavigationActionStub: WKNavigationAction {
    var url: URL
    var mainDocumentURL: URL
    var useTargetFrame: Bool
    
    init(_ url: URL, mainDocumentURL: URL? = nil, useTargetFrame: Bool = false) {
        self.url = url
        self.mainDocumentURL = mainDocumentURL ?? url
        self.useTargetFrame = useTargetFrame
    }
    
    override var request: URLRequest {
        var result = URLRequest(url: self.url)
        result.mainDocumentURL = self.mainDocumentURL
        return result
    }
    
    override var targetFrame: WKFrameInfo? {
        self.useTargetFrame ? .init() : nil
    }
    
}
