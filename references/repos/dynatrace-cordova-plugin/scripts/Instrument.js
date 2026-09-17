"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
var InstrumentHelper_1 = require("./helpers/InstrumentHelper");
module.exports = function (context) { return new Promise(function (resolve) {
    (0, InstrumentHelper_1.instrument)(process).then(function () {
        resolve('');
    });
}); };
