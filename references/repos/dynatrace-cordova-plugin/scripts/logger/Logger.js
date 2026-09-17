"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.Logger = void 0;
var FileLogger_1 = require("./FileLogger");
var BaseLogger_1 = require("./BaseLogger");
var LoggerType_1 = require("./LoggerType");
var Logger = (function () {
    function Logger() {
    }
    Logger.getInstance = function () {
        if (!this.instance) {
            this.setType(LoggerType_1.LoggerType.FileLogger, false);
        }
        return this.instance;
    };
    Logger.setType = function (loggerType, debug) {
        if (loggerType === LoggerType_1.LoggerType.FileLogger) {
            this.instance = new FileLogger_1.FileLogger(debug);
        }
        else {
            this.instance = new BaseLogger_1.BaseLogger(debug);
        }
    };
    Logger.setLogger = function (logger) {
        this.instance = logger;
    };
    return Logger;
}());
exports.Logger = Logger;
