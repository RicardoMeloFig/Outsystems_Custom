(function() {
    'use strict';

    var EyesUtils = require('eyes.utils');
    var EyesSDK = require('eyes.sdk');
    var EyesLeanFTUtils = require('./EyesLeanFTUtils').EyesLeanFTUtils;
    var PositionProvider = EyesSDK.PositionProvider,
        ArgumentGuard = EyesUtils.ArgumentGuard;

    /**
     * @constructor
     * @param {Logger} logger A Logger instance.
     * @param {EyesWebBrowser} executor
     * @param {PromiseFactory} promiseFactory
     * @augments PositionProvider
     */
    function ScrollPositionProvider(logger, executor, promiseFactory) {
        ArgumentGuard.notNull(logger, "logger");
        ArgumentGuard.notNull(executor, "executor");

        this._logger = logger;
        this._driver = executor;
        this._promiseFactory = promiseFactory;
    }

    ScrollPositionProvider.prototype = new PositionProvider();
    ScrollPositionProvider.prototype.constructor = ScrollPositionProvider;

    /**
     * @return {Promise<{x: number, y: number}>} The scroll position of the current frame.
     */
    ScrollPositionProvider.prototype.getCurrentPosition = function () {
        var that = this;
        that._logger.verbose("getCurrentScrollPosition()");
        return EyesLeanFTUtils.getCurrentScrollPosition(this._driver, this._promiseFactory).then(function (result) {
            that._logger.verbose("Current position: ", result);
            return result;
        });
    };

    /**
     * Go to the specified location.
     * @param {{x: number, y: number}} location The position to scroll to.
     * @return {Promise<void>}
     */
    ScrollPositionProvider.prototype.setPosition = function (location) {
        var that = this;
        that._logger.verbose("Scrolling to:", location);
        return EyesLeanFTUtils.scrollTo(this._driver, location, this._promiseFactory).then(function () {
            that._logger.verbose("Done scrolling!");
        });
    };

    /**
     * @return {Promise<{width: number, height: number}>} The entire size of the container which the position is relative to.
     */
    ScrollPositionProvider.prototype.getEntireSize = function () {
        var that = this;
        return EyesLeanFTUtils.getEntirePageSize(this._driver, this._promiseFactory).then(function (result) {
            that._logger.verbose("Entire size: ", result);
            return result;
        });
    };

    /**
     * @return {Promise<{x: number, y: number}>}
     */
    ScrollPositionProvider.prototype.getState = function () {
        return this.getCurrentPosition();
    };

    /**
     * @param {{x: number, y: number}} state The initial state of position
     * @return {Promise<void>}
     */
    ScrollPositionProvider.prototype.restoreState = function (state) {
        var that = this;
        return this.setPosition(state).then(function () {
            that._logger.verbose("Position restored.");
        });
    };

    exports.ScrollPositionProvider = ScrollPositionProvider;
}());
