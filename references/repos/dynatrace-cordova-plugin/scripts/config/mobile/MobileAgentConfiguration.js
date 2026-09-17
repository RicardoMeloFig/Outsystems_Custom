"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.MobileAgentConfiguration = void 0;
var MobileAgentConfiguration = (function () {
    function MobileAgentConfiguration(configuration) {
        this.configuration = configuration;
    }
    MobileAgentConfiguration.prototype.getConfiguration = function () {
        return this.configuration;
    };
    MobileAgentConfiguration.prototype.isConfigurationAvailable = function () {
        return this.configuration !== undefined;
    };
    return MobileAgentConfiguration;
}());
exports.MobileAgentConfiguration = MobileAgentConfiguration;
