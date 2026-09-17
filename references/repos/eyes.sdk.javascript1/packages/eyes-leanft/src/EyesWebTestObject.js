(function () {
    'use strict';

    var EyesSDK = require('eyes.sdk'),
        EyesUtils = require('eyes.utils');
    var MouseAction = EyesSDK.Triggers.MouseAction,
        GeneralUtils = EyesUtils.GeneralUtils,
        GeometryUtils = EyesUtils.GeometryUtils;

    var JS_GET_SCROLL_LEFT = "return element.scrollLeft;";

    var JS_GET_SCROLL_TOP = "return element.scrollTop;";

    var JS_GET_SCROLL_WIDTH = "return element.scrollWidth;";

    var JS_GET_SCROLL_HEIGHT = "return element.scrollHeight;";

    var JS_GET_LOCATION = "var rect = element.getBoundingClientRect(); return [rect.left, rect.top]";

    /**
     * @param {string} xpath
     * @return {string}
     */
    var JS_SELECT_ELEMENT_BY_XPATH = function (xpath) {
        return "var element = document.evaluate('" + xpath + "', document, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null).singleNodeValue;";
    };

    /**
     * @param {number} scrollLeft
     * @param {number} scrollTop
     * @return {string}
     */
    var JS_SCROLL_TO_COMMAND = function (scrollLeft, scrollTop) {
        return "element.scrollLeft = " + scrollLeft + "; element.scrollTop = " + scrollTop + ";";
    };

    /**
     * @param {string} overflow
     * @return {string}
     */
    var JS_SET_OVERFLOW_COMMAND = function (overflow) {
        return "element.style.overflow = '" + overflow + "'";
    };

    /**
     * Wraps a Remote Web Element.
     *
     * @constructor
     * @param {WebBaseTestObject} remoteWebElement
     * @param {EyesWebBrowser} browser
     * @param {Logger} logger
     * @augments WebBaseTestObject
     **/
    function EyesWebTestObject(remoteWebElement, browser, logger) {
        this._element = remoteWebElement;
        this._logger = logger;
        this._browser = browser;
        GeneralUtils.mixin(this, remoteWebElement);
    }

    function getRectangle(location, size) {
        size = size || {height: 0, width: 0};
        location = location || {x: 0, y: 0};

        var left = location.x,
            top = location.y,
            width = size.width,
            height = size.height;

        if (left < 0) {
            width = Math.max(0, width + left);
            left = 0;
        }

        if (top < 0) {
            height = Math.max(0, height + top);
            top = 0;
        }

        return {
            top: top,
            left: left,
            width: width,
            height: height
        };
    }

    function getBounds(element) {
        return element.location().then(function (location) {
            return element.size().then(function (size) {
                return getRectangle(location, size);
            }, function () {
                return getRectangle(location);
            });
        }, function () {
            return getRectangle();
        });
    }

    EyesWebTestObject.registerSendKeys = function (element, eyesDriver, logger, args) {
        var text = args.join('');
        logger.verbose("registerSendKeys: text is", text);
        return getBounds(element).then(function (rect) {
            eyesDriver.getEyes().addKeyboardTrigger(rect, text);
        });
    };

    EyesWebTestObject.prototype.sendKeys = function () {
        var that = this, args = Array.prototype.slice.call(arguments, 0);
        return EyesWebTestObject.registerSendKeys(that._element, that._browser, that._logger, args).then(function () {
            return that._element.sendKeys.call(that._element, args);
        });
    };

    EyesWebTestObject.registerClick = function (element, eyesDriver, logger) {
        logger.verbose("apply click on element");
        return getBounds(element).then(function (rect) {
            var offset = {x: rect.width / 2, y: rect.height / 2};
            eyesDriver.getEyes().addMouseTrigger(MouseAction.Click, rect, offset);
        });
    };

    EyesWebTestObject.prototype.click = function () {
        var that = this;
        that._logger.verbose("click on element");
        return EyesWebTestObject.registerClick(that._element, that._browser, that._logger).then(function () {
            return that._element.click();
        });
    };

    EyesWebTestObject.prototype.findElement = function (locator) {
        var that = this;
        return this._element.findElement(locator).then(function (element) {
            return new EyesWebTestObject(element, that._browser, that._logger);
        });
    };

    EyesWebTestObject.prototype.findElements = function (locator) {
        var that = this;
        return this._element.findElements(locator).then(function (elements) {
            return elements.map(function (element) {
                return new EyesWebTestObject(element, that._browser, that._logger);
            });
        });
    };

    /**
     * Returns the computed value of the style property for the current element.
     * @param {string} propStyle The style property which value we would like to extract.
     * @return {promise.Promise<string>} The value of the style property of the element, or {@code null}.
     */
    EyesWebTestObject.prototype.getComputedStyle = function (propStyle) {
        return this._element.getComputedStyle(propStyle);
    };

    /**
     * @return {promise.Promise<number>} The integer value of a computed style.
     */
    EyesWebTestObject.prototype.getComputedStyleInteger = function (propStyle) {
        return this.getComputedStyle(propStyle).then(function (value) {
            return Math.round(parseFloat(value.trim().replace("px", "")));
        });
    };

    /**
     * @return {promise.Promise<number>} The value of the scrollLeft property of the element.
     */
    EyesWebTestObject.prototype.getScrollLeft  = function () {
        return this.executeScript(JS_GET_SCROLL_LEFT).then(function (value) {
            return parseInt(value, 10);
        });
    };

    /**
     * @return {promise.Promise<number>} The value of the scrollTop property of the element.
     */
    EyesWebTestObject.prototype.getScrollTop  = function () {
        return this.executeScript(JS_GET_SCROLL_TOP).then(function (value) {
            return parseInt(value, 10);
        });
    };

    /**
     * @return {promise.Promise<number>} The value of the scrollWidth property of the element.
     */
    EyesWebTestObject.prototype.getScrollWidth  = function () {
        return this.executeScript(JS_GET_SCROLL_WIDTH).then(function (value) {
            return parseInt(value, 10);
        });
    };

    /**
     * @return {promise.Promise<number>} The value of the scrollHeight property of the element.
     */
    EyesWebTestObject.prototype.getScrollHeight  = function () {
        return this.executeScript(JS_GET_SCROLL_HEIGHT).then(function (value) {
            return parseInt(value, 10);
        });
    };

    /**
     * @return {promise.Promise<number>} The width of the left border.
     */
    EyesWebTestObject.prototype.getBorderLeftWidth = function () {
        return this.getComputedStyleInteger("border-left-width");
    };

    /**
     * @return {promise.Promise<number>} The width of the right border.
     */
    EyesWebTestObject.prototype.getBorderRightWidth = function () {
        return this.getComputedStyleInteger("border-right-width");
    };

    /**
     * @return {promise.Promise<number>} The width of the top border.
     */
    EyesWebTestObject.prototype.getBorderTopWidth = function () {
        return this.getComputedStyleInteger("border-top-width");
    };

    /**
     * @return {promise.Promise<number>} The width of the bottom border.
     */
    EyesWebTestObject.prototype.getBorderBottomWidth = function () {
        return this.getComputedStyleInteger("border-bottom-width");
    };

    /**
     * @return {!promise.Thenable<{width: number, height: number}>} element's size
     */
    EyesWebTestObject.prototype.getSize = function () {
        return this._element.size().then(function (value) {
            return GeometryUtils.createSize(value.width, value.height);
        });
    };

    /**
     * @return {!promise.Thenable<{x: number, y: number}>} element's location
     */
    EyesWebTestObject.prototype.getLocation = function () {
        var results, x, y;
        return this.executeScript(JS_GET_LOCATION).then(function (value) {
            results = value;
            return results[0];
        }).then(function (value) {
            x = value;
            return results[1];
        }).then(function (value) {
            x = Math.ceil(x) || 0;
            y = Math.ceil(value) || 0;
            return GeometryUtils.createLocation(x, y);
        });
    };

    /**
     * Scrolls to the specified location inside the element.
     * @param {{x: number, y: number}} location The location to scroll to.
     * @return {promise.Promise<void>}
     */
    EyesWebTestObject.prototype.scrollTo = function (location) {
        return this.executeScript(JS_SCROLL_TO_COMMAND(location.x, location.y));
    };

    /**
     * @return {promise.Promise<string>} The overflow of the element.
     */
    EyesWebTestObject.prototype.getOverflow = function () {
        return this.getComputedStyle("overflow");
    };

    /**
     * @param {string} overflow The overflow to set
     * @return {promise.Promise<void>} The overflow of the element.
     */
    EyesWebTestObject.prototype.setOverflow = function (overflow) {
        return this.executeScript(JS_SET_OVERFLOW_COMMAND(overflow));
    };

    /**
     * @param {string} script
     * @return {promise.Promise<void>} The overflow of the element.
     */
    EyesWebTestObject.prototype.executeScript = function (script) {
        var that = this;
        return this.xpath().then(function (xpath) {
            return that._browser.executeScript(JS_SELECT_ELEMENT_BY_XPATH(xpath) + script);
        });
    };

    /**
     * @return {WebElement} The original element object
     */
    EyesWebTestObject.prototype.getRemoteWebElement = function () {
        return this._element;
    };

    exports.EyesWebTestObject = EyesWebTestObject;
}());
