"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.PackageJson = void 0;
var DoctorConstants_1 = require("../doctor/DoctorConstants");
var PackageJson = (function () {
    function PackageJson(dependencies, devDependencies, scripts) {
        this.dependencies = dependencies;
        this.devDependencies = devDependencies;
        this.scripts = scripts;
    }
    PackageJson.prototype.getPackageVersion = function () {
        var version = this.getDependencyVersion(DoctorConstants_1.DYNATRACE_NPM_PACKAGE_NAME);
        if ((version != null) && version.startsWith('^')) {
            return version.replace('^', '');
        }
        return version;
    };
    PackageJson.prototype.isPackageVersionAvailable = function () {
        return this.isDependencyAvailable(DoctorConstants_1.DYNATRACE_NPM_PACKAGE_NAME);
    };
    PackageJson.prototype.getAllDependencies = function () {
        var allDependencies = {};
        allDependencies = Object.assign(allDependencies, this.dependencies);
        return Object.assign(allDependencies, this.devDependencies);
    };
    PackageJson.prototype.areDependenciesAvailable = function () {
        var allDependencies = this.getAllDependencies();
        return Object.keys(allDependencies).length > 0;
    };
    PackageJson.prototype.isDependencyAvailable = function (dependency) {
        var allDependencies = this.getAllDependencies();
        return allDependencies[dependency] !== undefined;
    };
    PackageJson.prototype.getDependencyVersion = function (dependency) {
        var allDependencies = this.getAllDependencies();
        return allDependencies[dependency];
    };
    PackageJson.prototype.isScriptAvailable = function (script) {
        if (script === undefined) {
            return false;
        }
        else if (this.scripts === undefined) {
            return false;
        }
        return this.scripts[script] !== undefined;
    };
    PackageJson.prototype.getScripts = function () {
        return this.scripts;
    };
    return PackageJson;
}());
exports.PackageJson = PackageJson;
