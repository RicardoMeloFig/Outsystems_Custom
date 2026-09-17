"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.DoctorBuilder = void 0;
var Framework_1 = require("../model/Framework");
var Doctor_1 = require("./Doctor");
var DoctorBuilder = (function () {
    function DoctorBuilder() {
        this.platforms = [];
        this.framework = Framework_1.Framework.Cordova;
    }
    DoctorBuilder.prototype.setLatestPluginVersion = function (version) {
        if (version !== undefined) {
            if (version.startsWith('^')) {
                this.latestPluginVersion = version.replace('^', '');
            }
            else {
                this.latestPluginVersion = version;
            }
        }
        return this;
    };
    DoctorBuilder.prototype.setFramework = function (framework) {
        this.framework = framework;
        return this;
    };
    DoctorBuilder.prototype.setPlatforms = function (platforms) {
        this.platforms = platforms;
        return this;
    };
    DoctorBuilder.prototype.setPackageJson = function (packageJson) {
        this.packageJson = packageJson;
        return this;
    };
    DoctorBuilder.prototype.setDynatraceConfigFile = function (configuration) {
        this.dynatraceConfiguration = configuration;
        return this;
    };
    DoctorBuilder.prototype.build = function () {
        return new Doctor_1.Doctor(this.platforms, this.framework, this.latestPluginVersion, this.packageJson, this.dynatraceConfiguration);
    };
    return DoctorBuilder;
}());
exports.DoctorBuilder = DoctorBuilder;
