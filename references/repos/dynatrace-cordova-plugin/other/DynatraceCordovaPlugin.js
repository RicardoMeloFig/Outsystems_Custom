"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
var emptyFunction = function () { };
var pluginId = module.id.slice(0, module.id.lastIndexOf('.'));
var MobileFirstRequestInterceptor = require(pluginId + '.mobile-first-network-interceptor').MobileFirstRequestInterceptor;
var NativeNetworkInterceptorUtils = require(pluginId + '.native-network-interceptor-utils').NativeNetworkInterceptorUtils;
var exec = require('cordova/exec');
var postToMobileAgent = function (message) {
    var _a, _b;
    var handler = (_b = (_a = window.webkit) === null || _a === void 0 ? void 0 : _a.messageHandlers) === null || _b === void 0 ? void 0 : _b.MobileAgent;
    if (!handler || typeof handler.postMessage !== "function") {
        console.warn("MobileAgent messageHandler isn't available!");
        return;
    }
    handler.postMessage(message);
};
module.exports = {
    endVisit: function (success, error) {
        success = success !== null && success !== void 0 ? success : emptyFunction;
        error = error !== null && error !== void 0 ? error : emptyFunction;
        exec(success, error, 'DynatraceCordovaPlugin', 'endVisit', []);
    },
    getMobileFirstNetworkInterceptor: function () { return MobileFirstRequestInterceptor; },
    getNativeNetworkInterceptorUtils: function () { return NativeNetworkInterceptorUtils; },
    getUserPrivacyOptions: function (success, error) {
        success = success !== null && success !== void 0 ? success : emptyFunction;
        error = error !== null && error !== void 0 ? error : emptyFunction;
        if (window.MobileAgent !== undefined) {
            exec(success, error, 'DynatraceCordovaPlugin', 'getUserPrivacyOptions', []);
        }
        else if (window.cordova.platformId === "ios") {
            postToMobileAgent({ "api": "getUserPrivacyOptions" });
        }
    },
    applyUserPrivacyOptions: function (dataCollectionLevel, crashReportingOptedIn, success, error) {
        if (crashReportingOptedIn !== undefined && dataCollectionLevel !== undefined) {
            if (window.MobileAgent !== undefined) {
                success = success !== null && success !== void 0 ? success : emptyFunction;
                error = error !== null && error !== void 0 ? error : emptyFunction;
                var options = { dataCollectionLevel: dataCollectionLevel, crashReportingOptedIn: crashReportingOptedIn };
                exec(success, error, 'DynatraceCordovaPlugin', 'applyUserPrivacyOptions', [options]);
            }
            else if (window.cordova.platformId === "ios") {
                postToMobileAgent({ "api": "applyUserPrivacyOptions", "arguments": { "dataCollectionLevel": dataCollectionLevel, "crashReportingOptedIn": crashReportingOptedIn } });
            }
        }
        else if (error !== undefined) {
            error('UserPrivacyOptions missing properties.');
        }
    },
};
