"use strict";
var __extends = (this && this.__extends) || (function () {
    var extendStatics = function (d, b) {
        extendStatics = Object.setPrototypeOf ||
            ({ __proto__: [] } instanceof Array && function (d, b) { d.__proto__ = b; }) ||
            function (d, b) { for (var p in b) if (Object.prototype.hasOwnProperty.call(b, p)) d[p] = b[p]; };
        return extendStatics(d, b);
    };
    return function (d, b) {
        if (typeof b !== "function" && b !== null)
            throw new TypeError("Class extends value " + String(b) + " is not a constructor or null");
        extendStatics(d, b);
        function __() { this.constructor = d; }
        d.prototype = b === null ? Object.create(b) : (__.prototype = b.prototype, new __());
    };
})();
var __awaiter = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
    function adopt(value) { return value instanceof P ? value : new P(function (resolve) { resolve(value); }); }
    return new (P || (P = Promise))(function (resolve, reject) {
        function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
        function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
        function step(result) { result.done ? resolve(result.value) : adopt(result.value).then(fulfilled, rejected); }
        step((generator = generator.apply(thisArg, _arguments || [])).next());
    });
};
var __generator = (this && this.__generator) || function (thisArg, body) {
    var _ = { label: 0, sent: function() { if (t[0] & 1) throw t[1]; return t[1]; }, trys: [], ops: [] }, f, y, t, g;
    return g = { next: verb(0), "throw": verb(1), "return": verb(2) }, typeof Symbol === "function" && (g[Symbol.iterator] = function() { return this; }), g;
    function verb(n) { return function (v) { return step([n, v]); }; }
    function step(op) {
        if (f) throw new TypeError("Generator is already executing.");
        while (g && (g = 0, op[0] && (_ = 0)), _) try {
            if (f = 1, y && (t = op[0] & 2 ? y["return"] : op[0] ? y["throw"] || ((t = y["return"]) && t.call(y), 0) : y.next) && !(t = t.call(y, op[1])).done) return t;
            if (y = 0, t) op = [op[0] & 2, t.value];
            switch (op[0]) {
                case 0: case 1: t = op; break;
                case 4: _.label++; return { value: op[1], done: false };
                case 5: _.label++; y = op[1]; op = [0]; continue;
                case 7: op = _.ops.pop(); _.trys.pop(); continue;
                default:
                    if (!(t = _.trys, t = t.length > 0 && t[t.length - 1]) && (op[0] === 6 || op[0] === 2)) { _ = 0; continue; }
                    if (op[0] === 3 && (!t || (op[1] > t[0] && op[1] < t[3]))) { _.label = op[1]; break; }
                    if (op[0] === 6 && _.label < t[1]) { _.label = t[1]; t = op; break; }
                    if (t && _.label < t[2]) { _.label = t[2]; _.ops.push(op); break; }
                    if (t[2]) _.ops.pop();
                    _.trys.pop(); continue;
            }
            op = body.call(thisArg, _);
        } catch (e) { op = [6, e]; y = 0; } finally { f = t = 0; }
        if (op[0] & 5) throw op[1]; return { value: op[0] ? op[1] : void 0, done: true };
    }
};
Object.defineProperty(exports, "__esModule", { value: true });
exports.createHTTPRequestWithRetries = exports.retrieveJSAgentHttp = exports.downloadAgent = exports.StopBuildError = exports.AGENT_DTCONFIG = void 0;
var Logger_1 = require("./logger/Logger");
var HttpCommunication_1 = require("./http/HttpCommunication");
exports.AGENT_DTCONFIG = 'data-dtconfig';
var StopBuildError = (function (_super) {
    __extends(StopBuildError, _super);
    function StopBuildError(message) {
        return _super.call(this, message) || this;
    }
    return StopBuildError;
}(Error));
exports.StopBuildError = StopBuildError;
var downloadAgent = function (config) { return __awaiter(void 0, void 0, void 0, function () {
    var jsagentContent, exception_1;
    return __generator(this, function (_a) {
        switch (_a.label) {
            case 0:
                if (!config.isJavaScriptAgentConfigurationAvailable()) {
                    Logger_1.Logger.getInstance()
                        .logInfo('No Settings available for JSAgent in the dynatrace.config.js file - Will not retrieve or include JSAgent.');
                    return [2];
                }
                else if (!config.getJavaScriptAgentConfiguration().isAgentUrlValid()) {
                    Logger_1.Logger.getInstance().logWarning('Incorrect or empty url found for JSAgent - Will not retrieve or include JSAgent.');
                    return [2];
                }
                Logger_1.Logger.getInstance().logInfo('Starting the retrieval of the JSAgent ..');
                _a.label = 1;
            case 1:
                _a.trys.push([1, 3, , 4]);
                return [4, (0, exports.retrieveJSAgentHttp)(config.getJavaScriptAgentConfiguration())];
            case 2:
                jsagentContent = _a.sent();
                Logger_1.Logger.getInstance().logInfo('JSAgent was retrieved successfully!');
                return [2, jsagentContent];
            case 3:
                exception_1 = _a.sent();
                Logger_1.Logger.getInstance().logError('Could not retrieve the JSAgent! - ' + exception_1);
                return [3, 4];
            case 4: return [2];
        }
    });
}); };
exports.downloadAgent = downloadAgent;
var retrieveJSAgentHttp = function (configuration) { return __awaiter(void 0, void 0, void 0, function () {
    var jsagentContent;
    return __generator(this, function (_a) {
        switch (_a.label) {
            case 0:
                if (configuration.isAnyCertificateAllowed()) {
                    Logger_1.Logger.getInstance().logWarning('Ignoring certificate when retrieving the JSAgent!');
                }
                return [4, (0, exports.createHTTPRequestWithRetries)(3, configuration, 'Retrieving of JS Agent options finished', 'Could not retrieve JS Agent options')];
            case 1:
                jsagentContent = _a.sent();
                if (jsagentContent !== undefined && jsagentContent.startsWith('<script')) {
                    return [2, jsagentContent.toString()];
                }
                else {
                    throw new Error('Content of the JS Agent is invalid! Please check the js url in the dynatrace.config.js file.');
                }
                return [2];
        }
    });
}); };
exports.retrieveJSAgentHttp = retrieveJSAgentHttp;
var createHTTPRequestWithRetries = function (retries, configuration, finishMsg, errorMsg) { return __awaiter(void 0, void 0, void 0, function () {
    var amountOfRetries, jsagentContent, error_1;
    return __generator(this, function (_a) {
        switch (_a.label) {
            case 0:
                amountOfRetries = retries || 1;
                jsagentContent = '';
                _a.label = 1;
            case 1:
                amountOfRetries -= 1;
                _a.label = 2;
            case 2:
                _a.trys.push([2, 4, , 5]);
                return [4, (0, HttpCommunication_1.createJSAgentHTTPRequest)(configuration, finishMsg, errorMsg)];
            case 3:
                jsagentContent = _a.sent();
                return [3, 5];
            case 4:
                error_1 = _a.sent();
                if (amountOfRetries === 0) {
                    throw error_1;
                }
                return [3, 5];
            case 5:
                if (jsagentContent.length === 0 && amountOfRetries > 0) return [3, 1];
                _a.label = 6;
            case 6: return [2, jsagentContent];
        }
    });
}); };
exports.createHTTPRequestWithRetries = createHTTPRequestWithRetries;
