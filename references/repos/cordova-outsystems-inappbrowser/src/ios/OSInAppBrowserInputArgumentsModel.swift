import OSInAppBrowserLib

class OSInAppBrowserInputArgumentsSimpleModel: Decodable {
    let url: String
}

class OSInAppBrowserInputArgumentsComplexModel: OSInAppBrowserInputArgumentsSimpleModel {
    struct Options: Decodable {
        struct iOS: Decodable {
            let closeButtonText: OSIABDismissStyle?
            let viewStyle: OSIABViewStyle?
            let animationEffect: OSIABAnimationEffect?
            let enableBarsCollapsing: Bool?
            let enableReadersMode: Bool?
            
            let allowOverScroll: Bool?
            let enableViewportScale: Bool?
            let allowInLineMediaPlayback: Bool?
            let surpressIncrementalRendering: Bool?
            let allowsBackForwardNavigationGestures: Bool?
        }
        
        let iOS: iOS
        
        let showURL: Bool?
        let showToolbar: Bool?
        let clearCache: Bool?
        let clearSessionCache: Bool?
        let mediaPlaybackRequiresUserAction: Bool?
        let closeButtonText: String?
        let toolbarPosition: OSIABToolbarPosition?
        let leftToRight: Bool?
        let showNavigationButtons: Bool?
        let customWebViewUserAgent: String?
    }
    
    let options: Options
    let customHeaders: [String: String]?

    enum CodingKeys: CodingKey {
        case options
        case customHeaders
    }
    
    required init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        self.options = try container.decode(Options.self, forKey: .options)
        self.customHeaders = try container.decodeIfPresent([String: String].self, forKey: .customHeaders)
        try super.init(from: decoder)
    }
}

extension OSInAppBrowserInputArgumentsComplexModel {
    func toSystemBrowserOptions() -> OSIABSystemBrowserOptions {
        .init(
            dismissStyle: self.options.iOS.closeButtonText ?? .defaultValue,
            viewStyle: self.options.iOS.viewStyle ?? .defaultValue,
            animationEffect: self.options.iOS.animationEffect ?? .defaultValue,
            enableBarsCollapsing: self.options.iOS.enableBarsCollapsing ?? true,
            enableReadersMode: self.options.iOS.enableReadersMode ?? false
        )
    }
    
    func toWebViewOptions() -> OSIABWebViewOptions {
        OSIABWebViewOptions(
            showURL: self.options.showURL ?? true,
            showToolbar: self.options.showToolbar ?? true,
            clearCache: self.options.clearCache ?? true,
            clearSessionCache: self.options.clearSessionCache ?? true,
            mediaPlaybackRequiresUserAction: self.options.mediaPlaybackRequiresUserAction ?? false,
            closeButtonText: self.options.closeButtonText ?? "Close",
            toolbarPosition: self.options.toolbarPosition ?? .defaultValue,
            showNavigationButtons: self.options.showNavigationButtons ?? true,
            leftToRight: self.options.leftToRight ?? false,
            allowOverScroll: self.options.iOS.allowOverScroll ?? true,
            enableViewportScale: self.options.iOS.enableViewportScale ?? false,
            allowInLineMediaPlayback: self.options.iOS.allowInLineMediaPlayback ?? false,
            surpressIncrementalRendering: self.options.iOS.surpressIncrementalRendering ?? false,
            viewStyle: self.options.iOS.viewStyle ?? .defaultValue,
            animationEffect: self.options.iOS.animationEffect ?? .defaultValue,
            customUserAgent: self.options.customWebViewUserAgent,
            allowsBackForwardNavigationGestures: self.options.iOS.allowsBackForwardNavigationGestures ?? true
        )
    }
}

extension OSIABAnimationEffect: Decodable {}
extension OSIABDismissStyle: Decodable {}
extension OSIABToolbarPosition: Decodable {}
extension OSIABViewStyle: Decodable {}
