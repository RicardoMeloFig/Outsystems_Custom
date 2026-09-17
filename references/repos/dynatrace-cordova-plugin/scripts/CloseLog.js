"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
var Logger_1 = require("./logger/Logger");
(function () {
    try {
        Logger_1.Logger.getInstance().closeLogger();
    }
    catch (e) {
        if (e instanceof Error) {
            Logger_1.Logger.getInstance().logError(e.message);
        }
    }
})();
