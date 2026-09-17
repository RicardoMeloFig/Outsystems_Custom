"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.LogLevelDefault = exports.LogLevel = void 0;
var LogLevel;
(function (LogLevel) {
    LogLevel["ERROR"] = "ERROR";
    LogLevel["INFO"] = "INFO ";
    LogLevel["WARNING"] = "WARN ";
})(LogLevel = exports.LogLevel || (exports.LogLevel = {}));
exports.LogLevelDefault = LogLevel.INFO;
