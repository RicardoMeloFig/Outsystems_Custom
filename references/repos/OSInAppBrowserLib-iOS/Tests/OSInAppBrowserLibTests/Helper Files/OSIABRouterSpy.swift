import OSInAppBrowserLib
import UIKit

struct OSIABExternalRouterSpy: OSIABRouter {
    private var shouldOpenSafari: Bool
    
    init(shouldOpenSafari: Bool) {
        self.shouldOpenSafari = shouldOpenSafari
    }
    
    func handleOpen(_ url: URL, _ completionHandler: @escaping (Bool) -> Void) {
        completionHandler(shouldOpenSafari)
    }
}

struct OSIABSystemRouterSpy: OSIABRouter {
    private var shouldOpenSafariViewController: UIViewController
    
    init(shouldOpen viewController: UIViewController) {
        self.shouldOpenSafariViewController = viewController
    }
    
    func handleOpen(_ url: URL, _ completionHandler: @escaping (UIViewController) -> Void) {
        completionHandler(shouldOpenSafariViewController)
    }
}

struct OSIABWebViewRouterSpy: OSIABRouter {
    private var shouldOpenView: UIViewController
    
    init(shouldOpen viewController: UIViewController) {
        self.shouldOpenView = viewController
    }
    
    func handleOpen(_ url: URL, _ completionHandler: @escaping (UIViewController) -> Void) {
        completionHandler(shouldOpenView)
    }
}
