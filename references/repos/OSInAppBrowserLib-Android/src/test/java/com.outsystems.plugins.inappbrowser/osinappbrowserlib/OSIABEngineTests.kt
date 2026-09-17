package com.outsystems.plugins.inappbrowser.osinappbrowserlib

import com.outsystems.plugins.inappbrowser.osinappbrowserlib.helpers.OSIABRouterSpy
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class OSIABEngineTests {
    private val url = "https://www.outsystems.com/"
    private lateinit var customTabsRouterSpy : OSIABRouterSpy
    private lateinit var externalBrowserRouterSpy : OSIABRouterSpy
    private lateinit var webViewRouterSpy : OSIABRouterSpy

    @Test
    fun test_open_externalBrowserWithoutIssues_doesOpenBrowser() {
        makeSUT(true).openExternalBrowser(externalBrowserRouterSpy, url) { result ->
            assertTrue(result)
        }
    }

    @Test
    fun test_open_externalBrowserWithIssues_doesNotOpenBrowser() {
        makeSUT(false).openExternalBrowser(externalBrowserRouterSpy, url) { result ->
            assertFalse(result)
        }
    }

    @Test
    fun test_open_webViewWithoutIssues_doesOpenWebView() {
        makeSUT(true).openWebView(webViewRouterSpy, url) { result ->
            assertTrue(result)
        }
    }

    @Test
    fun test_open_webViewWithIssues_doesNotOpenWebView() {
        makeSUT(false).openWebView(webViewRouterSpy, url) { result ->
            assertFalse(result)
        }
    }

    @Test
    fun test_open_customTabsWithoutIssues_doesOpenCustomTabs() {
        makeSUT(true).openCustomTabs(customTabsRouterSpy, url) { result ->
            assertTrue(result)
        }
    }

    @Test
    fun test_open_customTabsWithIssues_doesNotOpenCustomTabs() {
        makeSUT(false).openCustomTabs(customTabsRouterSpy, url) { result ->
            assertFalse(result)
        }
    }

    @Test
    fun test_close_webViewWithoutIssues_doesCloseView() {
        val sut = makeSUT(shouldOpenBrowser = true, shouldCloseBrowser = true)
        sut.openWebView(webViewRouterSpy, url) {
            webViewRouterSpy.close { didClose ->
                assertTrue(didClose)
            }
        }
    }

    @Test
    fun test_close_webViewWithIssues_doesNotCloseView() {
        val sut = makeSUT(shouldOpenBrowser = true, shouldCloseBrowser = false)
        sut.openWebView(webViewRouterSpy, url) {
            webViewRouterSpy.close { didClose ->
                assertFalse(didClose)
            }
        }
    }

    @Test
    fun test_close_customTabsWithoutIssues_doesCloseView() {
        val sut = makeSUT(shouldOpenBrowser = true, shouldCloseBrowser = true)
        sut.openCustomTabs(customTabsRouterSpy, url) {
            customTabsRouterSpy.close { didClose ->
                assertTrue(didClose)
            }
        }
    }

    @Test
    fun test_close_customTabsWithIssues_doesNotCloseView() {
        val sut = makeSUT(shouldOpenBrowser = true, shouldCloseBrowser = false)
        sut.openCustomTabs(customTabsRouterSpy, url) {
            customTabsRouterSpy.close { didClose ->
                assertFalse(didClose)
            }
        }
    }

    private fun makeSUT(shouldOpenBrowser: Boolean, shouldCloseBrowser: Boolean = false): OSIABEngine {
        customTabsRouterSpy = OSIABRouterSpy(shouldOpenBrowser, shouldCloseBrowser)
        externalBrowserRouterSpy = OSIABRouterSpy(shouldOpenBrowser, shouldCloseBrowser)
        webViewRouterSpy = OSIABRouterSpy(shouldOpenBrowser, shouldCloseBrowser)
        return OSIABEngine()
    }
}
