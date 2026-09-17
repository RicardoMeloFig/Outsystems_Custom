import OSInAppBrowserLib
import XCTest

final class OSIABCacheManagerTests: XCTestCase {
    // MARK: Clear Cache Tests
    func test_givenACacheWithACookie_whenClearingCache_allCookiesWillBeDeleted() {
        let cookiesAdded = self.expectation(description: "One cookie added to data store")
        let websiteDataStore = OSIABWebsiteDataStoreStub.dataStore(numberOfCookiesToAdd: 1) {
            cookiesAdded.fulfill()
        }
        wait(for: [cookiesAdded])
        
        let fetchFilledDataStore = self.expectation(description: "Fetch data store after adding cookie")
        websiteDataStore.httpCookieStore.getAllCookies {
            XCTAssertFalse($0.isEmpty)
            fetchFilledDataStore.fulfill()
        }
        wait(for: [fetchFilledDataStore])
        
        let sut = OSIABBrowserCacheManager(dataStore: websiteDataStore)
        let cacheCleared = self.expectation(description: "Clearing cache")
        sut.clearCache {
            cacheCleared.fulfill()
        }
        wait(for: [cacheCleared])
        
        let fetchClearedDataStore = self.expectation(description: "Fetch data store after clearing cookies")
        websiteDataStore.httpCookieStore.getAllCookies {
            XCTAssertTrue($0.isEmpty)
            fetchClearedDataStore.fulfill()
        }
        wait(for: [fetchClearedDataStore])
    }
    
    func test_givenACacheWithASessionCookie_whenClearingCache_allCookiesWillBeDeleted() {
        let cookiesAdded = self.expectation(description: "One session cookie added to data store")
        let websiteDataStore = OSIABWebsiteDataStoreStub.dataStore(numberOfSessionCookiesToAdd: 1) {
            cookiesAdded.fulfill()
        }
        wait(for: [cookiesAdded])
        
        let fetchFilledDataStore = self.expectation(description: "Fetch data store after adding cookie")
        websiteDataStore.httpCookieStore.getAllCookies {
            XCTAssertFalse($0.isEmpty)
            fetchFilledDataStore.fulfill()
        }
        wait(for: [fetchFilledDataStore])
        
        let sut = OSIABBrowserCacheManager(dataStore: websiteDataStore)
        let cacheCleared = self.expectation(description: "Clearing cache")
        sut.clearCache {
            cacheCleared.fulfill()
        }
        wait(for: [cacheCleared])
        
        let fetchClearedDataStore = self.expectation(description: "Fetch data store after clearing cookies")
        websiteDataStore.httpCookieStore.getAllCookies {
            XCTAssertTrue($0.isEmpty)
            fetchClearedDataStore.fulfill()
        }
        wait(for: [fetchClearedDataStore])
    }
    
    // MARK: Clear Session Cache Tests
    func test_givenACacheWithACookie_whenClearingSessionCache_cookieWillNotBeDeleted() {
        let cookiesAdded = self.expectation(description: "One cookie added to data store")
        let websiteDataStore = OSIABWebsiteDataStoreStub.dataStore(numberOfCookiesToAdd: 1) {
            cookiesAdded.fulfill()
        }
        wait(for: [cookiesAdded])
        
        let fetchFilledDataStore = self.expectation(description: "Fetch data store after adding cookie")
        websiteDataStore.httpCookieStore.getAllCookies {
            XCTAssertFalse($0.isEmpty)
            fetchFilledDataStore.fulfill()
        }
        wait(for: [fetchFilledDataStore])
        
        let sut = OSIABBrowserCacheManager(dataStore: websiteDataStore)
        let cacheCleared = self.expectation(description: "Clearing session cache")
        sut.clearSessionCache {
            cacheCleared.fulfill()
        }
        wait(for: [cacheCleared])
        
        let fetchClearedDataStore = self.expectation(description: "Fetch data store after clearing cookies")
        websiteDataStore.httpCookieStore.getAllCookies {
            XCTAssertFalse($0.isEmpty)
            fetchClearedDataStore.fulfill()
        }
        wait(for: [fetchClearedDataStore])
    }
    
    func test_givenACacheWithASessionCookie_whenClearingSessionCache_allCookiesWillBeDeleted() {
        let cookiesAdded = self.expectation(description: "One session cookie added to data store")
        let websiteDataStore = OSIABWebsiteDataStoreStub.dataStore(numberOfSessionCookiesToAdd: 1) {
            cookiesAdded.fulfill()
        }
        wait(for: [cookiesAdded])
        
        let fetchFilledDataStore = self.expectation(description: "Fetch data store after adding cookie")
        websiteDataStore.httpCookieStore.getAllCookies {
            XCTAssertFalse($0.isEmpty)
            fetchFilledDataStore.fulfill()
        }
        wait(for: [fetchFilledDataStore])
        
        let sut = OSIABBrowserCacheManager(dataStore: websiteDataStore)
        let cacheCleared = self.expectation(description: "Clearing session cache")
        sut.clearSessionCache {
            cacheCleared.fulfill()
        }
        wait(for: [cacheCleared])
        
        let fetchClearedDataStore = self.expectation(description: "Fetch data store after clearing cookies")
        websiteDataStore.httpCookieStore.getAllCookies {
            XCTAssertTrue($0.isEmpty)
            fetchClearedDataStore.fulfill()
        }
        wait(for: [fetchClearedDataStore])
    }
}
