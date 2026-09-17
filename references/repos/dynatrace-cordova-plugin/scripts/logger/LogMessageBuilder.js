"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.LogMessageBuilder = void 0;
var LogLevel_1 = require("./LogLevel");
var LogUtil_1 = require("./LogUtil");
var LogMessageBuilder = (function () {
    function LogMessageBuilder(message) {
        this.message = message;
        this.logLevel = LogLevel_1.LogLevel.INFO;
    }
    LogMessageBuilder.prototype.setLogLevel = function (logLevel) {
        this.logLevel = logLevel;
        return this;
    };
    LogMessageBuilder.prototype.setLogDate = function (date) {
        this.date = date;
        return this;
    };
    LogMessageBuilder.prototype.build = function () {
        var stringBuilder = "#".concat(this.logLevel.toString(), " ");
        if (this.date === undefined) {
            stringBuilder += "[".concat((0, LogUtil_1.currentDate)(), "]: ");
        }
        else {
            stringBuilder += "[".concat((0, LogUtil_1.convertDateToString)(this.date), "]: ");
        }
        return stringBuilder += this.message;
    };
    return LogMessageBuilder;
}());
exports.LogMessageBuilder = LogMessageBuilder;
