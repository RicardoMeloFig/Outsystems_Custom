"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.ConfigurationBuilder = void 0;
var Configuration_1 = require("./Configuration");
var ConfigurationBuilder = (function () {
    function ConfigurationBuilder(cordovaPluginConfiguration) {
        this.cordovaPluginConfiguration = cordovaPluginConfiguration;
    }
    ConfigurationBuilder.prototype.setJavaScriptAgentConfiguration = function (javascriptAgentConfiguration) {
        this.javascriptAgentConfiguration = javascriptAgentConfiguration;
        return this;
    };
    ConfigurationBuilder.prototype.setAndroidAgentConfiguration = function (androidAgentConfiguration) {
        this.androidAgentConfiguration = androidAgentConfiguration;
        return this;
    };
    ConfigurationBuilder.prototype.setIosAgentConfiguration = function (iOSAgentConfiguration) {
        this.iOSAgentConfiguration = iOSAgentConfiguration;
        return this;
    };
    ConfigurationBuilder.prototype.build = function () {
        return new Configuration_1.Configuration(this.cordovaPluginConfiguration, this.javascriptAgentConfiguration, this.androidAgentConfiguration, this.iOSAgentConfiguration);
    };
    return ConfigurationBuilder;
}());
exports.ConfigurationBuilder = ConfigurationBuilder;
