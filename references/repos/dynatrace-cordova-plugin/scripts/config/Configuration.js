"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.Configuration = void 0;
var Configuration = (function () {
    function Configuration(cordovaPluginConfiguration, javascriptAgentConfiguration, androidAgentConfiguration, iOSAgentConfiguration) {
        this.cordovaPluginConfiguration = cordovaPluginConfiguration;
        this.javascriptAgentConfiguration = javascriptAgentConfiguration;
        this.iOSAgentConfiguration = iOSAgentConfiguration;
        this.androidAgentConfiguration = androidAgentConfiguration;
    }
    Configuration.prototype.getCordovaPluginConfiguration = function () {
        return this.cordovaPluginConfiguration;
    };
    Configuration.prototype.getJavaScriptAgentConfiguration = function () {
        return this.javascriptAgentConfiguration;
    };
    Configuration.prototype.getAndroidConfiguration = function () {
        return this.androidAgentConfiguration;
    };
    Configuration.prototype.getIosConfiguration = function () {
        return this.iOSAgentConfiguration;
    };
    Configuration.prototype.isJavaScriptAgentConfigurationAvailable = function () {
        return this.javascriptAgentConfiguration !== undefined;
    };
    Configuration.prototype.isAndroidConfigurationAvailable = function () {
        return this.androidAgentConfiguration !== undefined
            && this.androidAgentConfiguration.isConfigurationAvailable();
    };
    Configuration.prototype.isIosConfigurationAvailable = function () {
        return this.iOSAgentConfiguration !== undefined &&
            this.iOSAgentConfiguration.isConfigurationAvailable();
    };
    return Configuration;
}());
exports.Configuration = Configuration;
