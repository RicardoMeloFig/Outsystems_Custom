import OSInAppBrowserLib
import WebKit

final class OSIABCacheManagerStub: OSIABCacheManager {
    var cacheWasCleared: Bool
    var sessionCacheWasCleared: Bool
    
    init(cacheWasCleared: Bool = false, sessionCacheWasCleared: Bool = false) {
        self.cacheWasCleared = cacheWasCleared
        self.sessionCacheWasCleared = sessionCacheWasCleared
    }
    
    func clearCache(_ completionHandler: CacheCompletion?) {
        self.cacheWasCleared = true
        completionHandler?()
    }
    
    func clearSessionCache(_ completionHandler: CacheCompletion?) {
        self.sessionCacheWasCleared = true
        completionHandler?()
    }
}

final class OSIABWebsiteDataStoreStub: WKWebsiteDataStore {
    static func dataStore(numberOfCookiesToAdd: Int = 0, numberOfSessionCookiesToAdd: Int = 0, _ completionHandler: @escaping () -> Void) -> WKWebsiteDataStore {
        var cookieArray = [HTTPCookie]()
        if numberOfCookiesToAdd > 0 {
            cookieArray += Array(repeating: OSIABCookieMock(), count: numberOfCookiesToAdd)
        }
        if numberOfSessionCookiesToAdd > 0 {
            cookieArray += Array(repeating: OSIABCookieMock(isSessionCookie: true), count: numberOfSessionCookiesToAdd)
        }
        
        let dataStore = Self.nonPersistent()
        dataStore.addCookies(cookieArray, completionHandler)
        
        return dataStore
    }
}

extension WKWebsiteDataStore {
    func addCookies(_ cookieArray: [HTTPCookie], _ completionHandler: @escaping () -> Void) {
        guard let cookie = cookieArray.first else {
            return completionHandler()
        }
        self.httpCookieStore.setCookie(cookie) { [weak self] in
            self?.addCookies(Array(cookieArray.dropFirst()), completionHandler)
        }
    }
}

final class OSIABCookieMock: HTTPCookie {
    var isSessionCookie: Bool
    
    init(isSessionCookie: Bool = false) {
        self.isSessionCookie = isSessionCookie
        super.init(properties: [
            .domain: "example.com",
            .path: "/",
            .name: "MyCookieName",
            .value: "MyCookieValue",
            .secure: "TRUE",
            .expires: NSDate(timeIntervalSinceNow: 31556926)
        ])!
    }
    
    override var isSessionOnly: Bool { self.isSessionCookie }
}
