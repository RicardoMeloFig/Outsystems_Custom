"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
var InstallHelper_1 = require("./helpers/InstallHelper");
var InstrumentHelper_1 = require("./helpers/InstrumentHelper");
module.exports = (function (context) { return new Promise(function (resolve) {
    (0, InstrumentHelper_1.instrument)(process).then(function () {
        (0, InstallHelper_1.replacePackageSwiftWithOriginal)().then(function () {
            (0, InstallHelper_1.moveIosFilesForSPM)().then(function () {
                resolve('');
            });
        });
    });
}); })();
