"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.BaseLogger = void 0;
var LogLevel_1 = require("./LogLevel");
var LogMessageBuilder_1 = require("./LogMessageBuilder");
var BaseLogger = (function () {
    function BaseLogger(debugEnabled) {
        this.debugEnabled = false;
        this.debugEnabled = debugEnabled;
    }
    BaseLogger.prototype.closeLogger = function () {
    };
    BaseLogger.prototype.logInfo = function (message) {
        return this.log(message, LogLevel_1.LogLevel.INFO);
    };
    BaseLogger.prototype.logWarning = function (message) {
        return this.log(message, LogLevel_1.LogLevel.WARNING);
    };
    BaseLogger.prototype.logError = function (message) {
        return this.log(message, LogLevel_1.LogLevel.ERROR);
    };
    BaseLogger.prototype.log = function (message, logLevel) {
        var logMessageBuilder = new LogMessageBuilder_1.LogMessageBuilder(message);
        logMessageBuilder.setLogLevel(logLevel ? logLevel : LogLevel_1.LogLevelDefault);
        var logMessage = logMessageBuilder.build();
        console.log(logMessage);
        return logMessage;
    };
    BaseLogger.prototype.logDebug = function (message, logLevel) {
        if (this.debugEnabled) {
            return this.log(message, logLevel);
        }
    };
    return BaseLogger;
}());
exports.BaseLogger = BaseLogger;
