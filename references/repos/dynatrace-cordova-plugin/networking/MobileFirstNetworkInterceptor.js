"use strict";
var __spreadArray = (this && this.__spreadArray) || function (to, from, pack) {
    if (pack || arguments.length === 2) for (var i = 0, l = from.length, ar; i < l; i++) {
        if (ar || !(i in from)) {
            if (!ar) ar = Array.prototype.slice.call(from, 0, i);
            ar[i] = from[i];
        }
    }
    return to.concat(ar || Array.prototype.slice.call(from));
};
Object.defineProperty(exports, "__esModule", { value: true });
exports.MobileFirstRequestInterceptor = void 0;
var pluginId = module.id.slice(0, module.id.lastIndexOf('.'));
var NativeNetworkInterceptorUtils = require(pluginId + '.native-network-interceptor-utils').NativeNetworkInterceptorUtils;
var origWLResourceRequest;
var isNetworkIntercepted = false;
var mfpServerURL;
var tagSendRequest = function (request) {
    var actionId = NativeNetworkInterceptorUtils.enterNativeRequestAction(mfpServerURL + request.getUrl(), 'Mobile First');
    var headers = NativeNetworkInterceptorUtils.getHeadersForNativeRequest(actionId);
    for (var headerProp in headers) {
        if (typeof headers[headerProp] === 'string' || headers[headerProp] instanceof String && headerProp === 'x-dtc') {
            request.setHeader(headerProp, headers[headerProp]);
        }
        else {
            console.log('x-dtc header not found. Cannot add the x-dtc header to the request.');
        }
    }
    return actionId;
};
exports.MobileFirstRequestInterceptor = {
    isInterceptorEnabled: function () { return isNetworkIntercepted; },
    enableInterceptor: function (serverURL) {
        if (typeof dT_ === 'undefined' && typeof dtrum === 'undefined') {
            console.log('Missing Dynatrace Javascript Agent API! MFP Interceptor not enabled!');
            return;
        }
        if (typeof WLResourceRequest === 'undefined' && typeof WL === 'undefined') {
            console.log('Missing Mobile First API! MFP Interceptor not enabled!');
            return;
        }
        if (typeof serverURL !== 'string' || serverURL.trim().length === 0) {
            console.log('Missing Mobile First Server URL! MFP Interceptor not enabled!');
            return;
        }
        if (isNetworkIntercepted) {
            console.log('MFP Interceptor already enabled!');
            return;
        }
        mfpServerURL = serverURL;
        origWLResourceRequest = WLResourceRequest;
        WLResourceRequest = function WLResourceRequest() {
            var args = [];
            for (var _i = 0; _i < arguments.length; _i++) {
                args[_i] = arguments[_i];
            }
            var request = new (origWLResourceRequest.bind.apply(origWLResourceRequest, __spreadArray([void 0], args, false)))();
            var originalSend = request.send;
            var originalSendFormParameters = request.sendFormParameters;
            request.send = function send(content) {
                var actionId = tagSendRequest(this);
                if (actionId !== -1) {
                    return originalSend.apply(this, [content])
                        .then(function (response) {
                        NativeNetworkInterceptorUtils.leaveNativeRequestAction(actionId);
                        return response;
                    }, function (error) {
                        NativeNetworkInterceptorUtils.leaveNativeRequestAction(actionId);
                        return error;
                    });
                }
                else {
                    return originalSend.apply(this, [content]);
                }
            };
            request.sendFormParameters = function sendFormParameters(json) {
                var actionId = tagSendRequest(this);
                if (actionId !== -1) {
                    return originalSendFormParameters.apply(this, [json])
                        .then(function (response) {
                        NativeNetworkInterceptorUtils.leaveNativeRequestAction(actionId);
                        return response;
                    }, function (error) {
                        NativeNetworkInterceptorUtils.leaveNativeRequestAction(actionId);
                        return error;
                    });
                }
                else {
                    return originalSendFormParameters.apply(this, [json]);
                }
            };
            return request;
        };
        for (var prop in origWLResourceRequest) {
            if (origWLResourceRequest.hasOwnProperty(prop) === true) {
                WLResourceRequest[prop] = origWLResourceRequest[prop];
            }
        }
        isNetworkIntercepted = true;
        console.log('Enabled MFP HTTP Interceptor!');
    },
    disableInterceptor: function () {
        if (!isNetworkIntercepted) {
            return;
        }
        mfpServerURL = undefined;
        WLResourceRequest = origWLResourceRequest;
        isNetworkIntercepted = false;
        console.log('Disabled MFP HTTP Interceptor!');
    },
};
