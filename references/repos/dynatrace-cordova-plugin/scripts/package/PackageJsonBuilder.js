"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.PackageJsonBuilder = void 0;
var PackageJson_1 = require("./PackageJson");
var PackageJsonBuilder = (function () {
    function PackageJsonBuilder() {
    }
    PackageJsonBuilder.prototype.setDependencies = function (dependencies) {
        this.dependencies = dependencies;
        return this;
    };
    PackageJsonBuilder.prototype.setDevDependencies = function (devDependencies) {
        this.devDependencies = devDependencies;
        return this;
    };
    PackageJsonBuilder.prototype.setScripts = function (scripts) {
        this.scripts = scripts;
        return this;
    };
    PackageJsonBuilder.prototype.build = function () {
        return new PackageJson_1.PackageJson(this.dependencies, this.devDependencies, this.scripts);
    };
    return PackageJsonBuilder;
}());
exports.PackageJsonBuilder = PackageJsonBuilder;
