"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.currentDate = exports.convertDateToString = void 0;
var convertDateToString = function (date) {
    var localISOTime = date.toISOString().slice(0, -5);
    return localISOTime.replace('T', ' ');
};
exports.convertDateToString = convertDateToString;
var currentDate = function () {
    var tzoffset = (new Date()).getTimezoneOffset() * 60000;
    return (0, exports.convertDateToString)(new Date(Date.now() - tzoffset));
};
exports.currentDate = currentDate;
