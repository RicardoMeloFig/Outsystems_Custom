package com.outsystems.plugins.inappbrowser.osinappbrowserlib

interface OSIABClosable {
    /**
     * Handles closing the opened browser
     * @param completionHandler The callback with the result of closing the opened browser
     */
    fun close(completionHandler: (Boolean) -> Unit)
}