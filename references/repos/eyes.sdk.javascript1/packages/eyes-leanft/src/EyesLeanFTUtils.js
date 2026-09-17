(function () {
    'use strict';

    var EyesSDK = require('eyes.sdk'),
        EyesUtils = require('eyes.utils');
    var MutableImage = EyesSDK.MutableImage,
        CoordinatesType = EyesSDK.CoordinatesType,
        GeneralUtils = EyesUtils.GeneralUtils,
        GeometryUtils = EyesUtils.GeometryUtils,
        ImageUtils = EyesUtils.ImageUtils;

    /**
     * Handles browser related functionality.
     * @constructor
     */
    function EyesLeanFTUtils () {}

    /**
     * @private
     * @type {string}
     */
    var JS_GET_VIEWPORT_SIZE =
        "var height = undefined; " +
        "var width = undefined; " +
        "if (window.innerHeight) { height = window.innerHeight; } " +
        "else if (document.documentElement && document.documentElement.clientHeight) { height = document.documentElement.clientHeight; } " +
        "else { var b = document.getElementsByTagName('body')[0]; if (b.clientHeight) {height = b.clientHeight;} }; " +
        "if (window.innerWidth) { width = window.innerWidth; } " +
        "else if (document.documentElement && document.documentElement.clientWidth) { width = document.documentElement.clientWidth; } " +
        "else { var b = document.getElementsByTagName('body')[0]; if (b.clientWidth) { width = b.clientWidth;} }; " +
        "return [width, height];";

    /**
     * @private
     * @type {string}
     */
    var JS_GET_CURRENT_SCROLL_POSITION =
        "var doc = document.documentElement; " +
        "var x = window.scrollX || ((window.pageXOffset || doc.scrollLeft) - (doc.clientLeft || 0)); " +
        "var y = window.scrollY || ((window.pageYOffset || doc.scrollTop) - (doc.clientTop || 0)); " +
        "return [x, y];";

    /**
     * @private
     * @type {string}
     */
    var JS_GET_CONTENT_ENTIRE_SIZE =
        "var scrollWidth = document.documentElement.scrollWidth; " +
        "var bodyScrollWidth = document.body.scrollWidth; " +
        "var totalWidth = Math.max(scrollWidth, bodyScrollWidth); " +
        "var clientHeight = document.documentElement.clientHeight; " +
        "var bodyClientHeight = document.body.clientHeight; " +
        "var scrollHeight = document.documentElement.scrollHeight; " +
        "var bodyScrollHeight = document.body.scrollHeight; " +
        "var maxDocElementHeight = Math.max(clientHeight, scrollHeight); " +
        "var maxBodyHeight = Math.max(bodyClientHeight, bodyScrollHeight); " +
        "var totalHeight = Math.max(maxDocElementHeight, maxBodyHeight); " +
        "return [totalWidth, totalHeight];";

    /**
     * @private
     * @type {string[]}
     */
    var JS_TRANSFORM_KEYS = ["transform", "-webkit-transform"];

    /**
     * Executes a script using the browser's executeScript function - and optionally waits a timeout.
     *
     * @param {EyesWebBrowser} browser The driver using which to execute the script.
     * @param {string} script The code to execute on the given driver.
     * @param {PromiseFactory} promiseFactory
     * @param {number|undefined} [stabilizationTimeMs] The amount of time to wait after script execution to
     *        let the browser a chance to stabilize (e.g., finish rendering).
     * @return {Promise<void>} A promise which resolves to the result of the script's execution on the tab.
     */
    EyesLeanFTUtils.executeScript = function executeScript(browser, script, promiseFactory, stabilizationTimeMs) {
        return promiseFactory.makePromise(function (resolve, reject) {
            try {
                browser.executeScript(script).then(function (result) {
                    if (stabilizationTimeMs) {
                        return GeneralUtils.sleep(stabilizationTimeMs, promiseFactory).then(function () {
                            resolve(result);
                        });
                    }
                    resolve(result);
                });
            } catch (e) {
                reject(e);
            }
        });
    };

    EyesLeanFTUtils.promiseArrayToArray = function (promiseArray, length, promiseFactory, resultArray) {
        var promise = promiseFactory.makePromise(function (resolve) {
            resolve();
        });

        if (!length && resultArray) {
            return promise.then(function () {
                return resultArray.reverse();
            });
        }

        return promise.then(function () {
            if (!resultArray) {
                resultArray = [];
            }

            length--;

            return promiseArray[length];
        }).then(function (value) {
            resultArray.push(value);
        }, function () {
            resultArray.push(undefined);
        }).then(function () {
            return EyesLeanFTUtils.promiseArrayToArray(promiseArray, length, promiseFactory, resultArray);
        });
    };

    EyesLeanFTUtils.promiseObjectToObject = function (promiseObject, keys, length, promiseFactory, resultObject) {
        var promise = promiseFactory.makePromise(function (resolve) {
            resolve();
        });

        if (!length && resultObject) {
            return promise.then(function () {
                return resultObject;
            });
        }

        return promise.then(function () {
            if (!resultObject) {
                resultObject = {};
            }

            length--;

            return promiseObject[keys[length]];
        }).then(function (value) {
            resultObject[keys[length]] = value;
        }, function () {
            resultObject[keys[length]] = undefined;
        }).then(function () {
            return EyesLeanFTUtils.promiseObjectToObject(promiseObject, keys, length, promiseFactory, resultObject);
        });
    };

    /**
     * Gets the device pixel ratio.
     *
     * @param {Web.Browser} browser The driver which will execute the script to get the ratio.
     * @param {PromiseFactory} promiseFactory
     * @return {Promise<number>} A promise which resolves to the device pixel ratio (float type).
     */
    EyesLeanFTUtils.getDevicePixelRatio = function getDevicePixelRatio(browser, promiseFactory) {
        return EyesLeanFTUtils.executeScript(browser, 'return window.devicePixelRatio;', promiseFactory, undefined).then(function (results) {
            return parseFloat(results);
        }, function (err) {
            console.error(err);
        });
    };

    /**
     * Get the current transform of page.
     *
     * @param {Web.Browser} browser The driver which will execute the script to get the scroll position.
     * @param {PromiseFactory} promiseFactory
     * @return {Promise<Map<string, string>>} A promise which resolves to the current transform value.
     */
    EyesLeanFTUtils.getCurrentTransform = function getCurrentTransform(browser, promiseFactory) {
        var script = "return { ";
        for (var i = 0, l = JS_TRANSFORM_KEYS.length; i < l; i++) {
            script += "'" + JS_TRANSFORM_KEYS[i] + "': document.documentElement.style['" + JS_TRANSFORM_KEYS[i] + "'],";
        }
        script += " }";

        return EyesLeanFTUtils.executeScript(browser, script, promiseFactory, undefined).then(function (results) {
            return EyesLeanFTUtils.promiseObjectToObject(results, JS_TRANSFORM_KEYS, JS_TRANSFORM_KEYS.length, promiseFactory);
        });
    };

    /**
     * Sets transforms for document.documentElement according to the given map of style keys and values.
     *
     * @param {Web.Browser} browser The browser to use.
     * @param {Map<string, string>} transforms The transforms to set. Keys are used as style keys and values are the values for those styles.
     * @param {PromiseFactory} promiseFactory
     * @return {Promise<void>}
     */
    EyesLeanFTUtils.setTransforms = function (browser, transforms, promiseFactory) {
        var script = "";
        for (var key in transforms) {
            if (transforms.hasOwnProperty(key)) {
                script += "document.documentElement.style['" + key + "'] = '" + transforms[key] + "';";
            }
        }

        return EyesLeanFTUtils.executeScript(browser, script, promiseFactory, 250);
    };

    /**
     * Set the given transform to document.documentElement for all style keys defined in {@link JS_TRANSFORM_KEYS}
     *
     * @param {Web.Browser} browser The driver which will execute the script to set the transform.
     * @param {string} transformToSet The transform to set.
     * @param {PromiseFactory} promiseFactory
     * @return {Promise<void>} A promise which resolves to the previous transform once the updated transform is set.
     */
    EyesLeanFTUtils.setTransform = function setTransform(browser, transformToSet, promiseFactory) {
        var transforms = {};
        if (!transformToSet) {
            transformToSet = '';
        }

        for (var i = 0, l = JS_TRANSFORM_KEYS.length; i < l; i++) {
            transforms[JS_TRANSFORM_KEYS[i]] = transformToSet;
        }

        return EyesLeanFTUtils.setTransforms(browser, transforms, promiseFactory);
    };

    /**
     * CSS translate the document to a given location.
     *
     * @param {Web.Browser} browser The driver which will execute the script to set the transform.
     * @param {{x: number, y: number}} point
     * @param {PromiseFactory} promiseFactory
     * @return {Promise<void>} A promise which resolves to the previous transform when the scroll is executed.
     */
    EyesLeanFTUtils.translateTo = function translateTo(browser, point, promiseFactory) {
        return EyesLeanFTUtils.setTransform(browser, 'translate(-' + point.x + 'px, -' + point.y + 'px)', promiseFactory);
    };

    /**
     * Scroll to the specified position.
     *
     * @param {Web.Browser} browser - The driver which will execute the script to set the scroll position.
     * @param {{x: number, y: number}} point Point to scroll to
     * @param {PromiseFactory} promiseFactory
     * @return {Promise<void>} A promise which resolves after the action is performed and timeout passed.
     */
    EyesLeanFTUtils.scrollTo = function scrollTo(browser, point, promiseFactory) {
        return EyesLeanFTUtils.executeScript(browser,
            'window.scrollTo(' + parseInt(point.x, 10) + ', ' + parseInt(point.y, 10) + ');',
            promiseFactory, 250);
    };

    /**
     * Gets the current scroll position.
     *
     * @param {Web.Browser} browser The driver which will execute the script to get the scroll position.
     * @param {PromiseFactory} promiseFactory
     * @return {Promise<{x: number, y: number}>} A promise which resolves to the current scroll position.
     */
    EyesLeanFTUtils.getCurrentScrollPosition = function getCurrentScrollPosition(browser, promiseFactory) {
        return EyesLeanFTUtils.executeScript(browser, JS_GET_CURRENT_SCROLL_POSITION, promiseFactory, undefined).then(function (results) {
            return EyesLeanFTUtils.promiseArrayToArray(results, 2, promiseFactory);
        }).then(function (results) {
            // If we can't find the current scroll position, we use 0 as default.
            var x = parseInt(results[0], 10) || 0;
            var y = parseInt(results[1], 10) || 0;
            return {x: x, y: y};
        });
    };

    /**
     * Get the entire page size.
     *
     * @param {Web.Browser} browser The driver used to query the web page.
     * @param {PromiseFactory} promiseFactory
     * @return {Promise<{width: number, height: number}>} A promise which resolves to an object containing the width/height of the page.
     */
    EyesLeanFTUtils.getEntirePageSize = function getEntirePageSize(browser, promiseFactory) {
        // IMPORTANT: Notice there's a major difference between scrollWidth
        // and scrollHeight. While scrollWidth is the maximum between an
        // element's width and its content width, scrollHeight might be
        // smaller (!) than the clientHeight, which is why we take the
        // maximum between them.
        return EyesLeanFTUtils.executeScript(browser, JS_GET_CONTENT_ENTIRE_SIZE, promiseFactory).then(function (results) {
            return EyesLeanFTUtils.promiseArrayToArray(results, 2, promiseFactory);
        }).then(function (results) {
            var totalWidth = results[0] || 0;
            var totalHeight = results[1] || 0;
            return {width: totalWidth, height: totalHeight};
        });
    };

    /**
     * Updates the document's documentElement "overflow" value (mainly used to remove/allow scrollbars).
     *
     * @param {Web.Browser} browser The driver used to update the web page.
     * @param {string} overflowValue The values of the overflow to set.
     * @param {PromiseFactory} promiseFactory
     * @return {Promise<string>} A promise which resolves to the original overflow of the document.
     */
    EyesLeanFTUtils.setOverflow = function setOverflow(browser, overflowValue, promiseFactory) {
        var script;
        if (overflowValue === null) {
            script =
                "var origOverflow = document.documentElement.style.overflow; " +
                "document.documentElement.style.overflow = undefined; " +
                "return origOverflow";
        } else {
            script =
                "var origOverflow = document.documentElement.style.overflow; " +
                "document.documentElement.style.overflow = \"" + overflowValue + "\"; " +
                "return origOverflow";
        }

        return EyesLeanFTUtils.executeScript(browser, script, promiseFactory, 100);
    };

    /**
     * Hides the scrollbars of the current context's document element.
     *
     * @param {Web.Browser} browser The browser to use for hiding the scrollbars.
     * @param {PromiseFactory} promiseFactory
     * @return {Promise<string>} The previous value of the overflow property (could be {@code null}).
     */
    EyesLeanFTUtils.hideScrollbars = function (browser, promiseFactory) {
        return EyesLeanFTUtils.setOverflow(browser, "hidden", promiseFactory);
    };

    /**
     * Tries to get the viewport size using Javascript. If fails, gets the entire browser window size!
     *
     * @param {Web.Browser} browser The browser to use.
     * @param {PromiseFactory} promiseFactory
     * @return {Promise<{width: number, height: number}>} The viewport size.
     */
    EyesLeanFTUtils.getViewportSize = function (browser, promiseFactory) {
        return promiseFactory.makePromise(function (resolve, reject) {
            return EyesLeanFTUtils.executeScript(browser, JS_GET_VIEWPORT_SIZE, promiseFactory, undefined).then(function (results) {
                return EyesLeanFTUtils.promiseArrayToArray(results, 2, promiseFactory);
            }).then(function (results) {
                if (isNaN(results[0]) || isNaN(results[1])) {
                    reject("Can't parse values.");
                } else {
                    resolve({
                        width: results[0] || 0,
                        height: results[1] || 0
                    });
                }
            }, function (err) {
                reject(err);
            });
        });
    };

    /**
     * @param {Logger} logger
     * @param {Web.Browser} browser The browser to use.
     * @param {PromiseFactory} promiseFactory
     * @return {Promise<{width: number, height: number}>} The viewport size of the current context, or the display size if the viewport size cannot be retrieved.
     */
    EyesLeanFTUtils.getViewportSizeOrDisplaySize = function (logger, browser, promiseFactory) {
        logger.verbose("getViewportSizeOrDisplaySize()");

        return EyesLeanFTUtils.getViewportSize(browser, promiseFactory).then(function (results) {
            return results;
        }, function (err) {
            logger.verbose("Failed to extract viewport size using Javascript:", err);

            // If we failed to extract the viewport size using JS, will use the window size instead.
            logger.verbose("Using window size as viewport size.");
            return browser.size().then(function (size) {
                logger.verbose("Done! Size is", size);
                return size;
            });
        });
    };

    /**
     * @param {Logger} logger
     * @param {Web.Browser} browser The browser to use.
     * @param {{width: number, height: number}} requiredSize
     * @param {PromiseFactory} promiseFactory
     * @return {Promise<boolean>}
     */
    EyesLeanFTUtils.setBrowserSize = function (logger, browser, requiredSize, promiseFactory) {
        return setBrowserSize(logger, browser, requiredSize, 3, promiseFactory).then(function () {
            return true;
        }, function () {
            return false;
        });
    };

    function setBrowserSize(logger, browser, requiredSize, retries, promiseFactory) {
        return promiseFactory.makePromise(function (resolve, reject) {
            logger.verbose("Trying to set browser size to:", requiredSize);

            return browser.resizeTo(requiredSize.width, requiredSize.height).then(function () {
                return GeneralUtils.sleep(1000, promiseFactory);
            }).then(function () {
                return browser.size();
            }).then(function (currentSize) {
                logger.log("Current browser size:", currentSize);
                if (currentSize.width === requiredSize.width && currentSize.height === requiredSize.height) {
                    resolve();
                    return;
                }

                if (retries === 0) {
                    reject("Failed to set browser size: retries is out.");
                    return;
                }

                setBrowserSize(logger, browser, requiredSize, retries - 1, promiseFactory).then(function () {
                    resolve();
                }, function (err) {
                    reject(err);
                });
            });
        });
    }

    /**
     * @param {Logger} logger
     * @param {Web.Browser} browser The browser to use.
     * @param {{width: number, height: number}} actualViewportSize
     * @param {{width: number, height: number}} requiredViewportSize
     * @param {PromiseFactory} promiseFactory
     * @return {Promise<boolean>}
     */
    EyesLeanFTUtils.setBrowserSizeByViewportSize = function (logger, browser, actualViewportSize, requiredViewportSize, promiseFactory) {
        return browser.size().then(function (browserSize) {
            logger.verbose("Current browser size:", browserSize);
            var requiredBrowserSize = {
                width: browserSize.width + (requiredViewportSize.width - actualViewportSize.width),
                height: browserSize.height + (requiredViewportSize.height - actualViewportSize.height)
            };
            return EyesLeanFTUtils.setBrowserSize(logger, browser, requiredBrowserSize, promiseFactory);
        });
    };

    /**
     * Tries to set the viewport
     *
     * @param {Logger} logger
     * @param {Web.Browser} browser The browser to use.
     * @param {{width: number, height: number}} requiredSize The viewport size.
     * @param {PromiseFactory} promiseFactory
     * @return {Promise<void>}
     */
    EyesLeanFTUtils.setViewportSize = function (logger, browser, requiredSize, promiseFactory) {
        // First we will set the window size to the required size.
        // Then we'll check the viewport size and increase the window size accordingly.
        logger.verbose("setViewportSize(", requiredSize, ")");
        return promiseFactory.makePromise(function (resolve, reject) {
            try {
                var actualViewportSize;
                EyesLeanFTUtils.getViewportSize(browser, promiseFactory).then(function (viewportSize) {
                    actualViewportSize = viewportSize;
                    logger.verbose("Initial viewport size:", actualViewportSize);

                    // If the viewport size is already the required size
                    if (actualViewportSize.width === requiredSize.width && actualViewportSize.height === requiredSize.height) {
                        resolve();
                        return;
                    }

                    // We move the window to (0,0) to have the best chance to be able to
                    // set the viewport size as requested.
                    EyesLeanFTUtils.scrollTo(browser, {x: 0, y: 0}, promiseFactory).catch(function () {
                        logger.verbose("Warning: Failed to move the browser window to (0,0)");
                    }).then(function () {
                        return EyesLeanFTUtils.setBrowserSizeByViewportSize(logger, browser, actualViewportSize, requiredSize, promiseFactory);
                    }).then(function () {
                        return EyesLeanFTUtils.getViewportSize(browser, promiseFactory);
                    }).then(function (actualViewportSize) {
                        if (actualViewportSize.width === requiredSize.width && actualViewportSize.height === requiredSize.height) {
                            resolve();
                            return;
                        }

                        // Additional attempt. This Solves the "maximized browser" bug
                        // (border size for maximized browser sometimes different than non-maximized, so the original browser size calculation is wrong).
                        logger.verbose("Trying workaround for maximization...");
                        return EyesLeanFTUtils.setBrowserSizeByViewportSize(logger, browser, actualViewportSize, requiredSize, promiseFactory).then(function () {
                            return EyesLeanFTUtils.getViewportSize(browser, promiseFactory);
                        }).then(function (viewportSize) {
                            actualViewportSize = viewportSize;
                            logger.verbose("Current viewport size:", actualViewportSize);

                            if (actualViewportSize.width === requiredSize.width && actualViewportSize.height === requiredSize.height) {
                                resolve();
                                return;
                            }

                            return browser.size().then(function (browserSize) {
                                var MAX_DIFF = 3;
                                var widthDiff = actualViewportSize.width - requiredSize.width;
                                var widthStep = widthDiff > 0 ? -1 : 1; // -1 for smaller size, 1 for larger
                                var heightDiff = actualViewportSize.height - requiredSize.height;
                                var heightStep = heightDiff > 0 ? -1 : 1;

                                var currWidthChange = 0;
                                var currHeightChange = 0;
                                // We try the zoom workaround only if size difference is reasonable.
                                if (Math.abs(widthDiff) <= MAX_DIFF && Math.abs(heightDiff) <= MAX_DIFF) {
                                    logger.verbose("Trying workaround for zoom...");
                                    var retriesLeft = Math.abs((widthDiff === 0 ? 1 : widthDiff) * (heightDiff === 0 ? 1 : heightDiff)) * 2;
                                    var lastRequiredBrowserSize = null;
                                    return setWindowSize(logger, browser, requiredSize, actualViewportSize, browserSize,
                                        widthDiff, widthStep, heightDiff, heightStep, currWidthChange, currHeightChange,
                                        retriesLeft, lastRequiredBrowserSize, promiseFactory).then(function () {
                                        resolve();
                                    }, function () {
                                        reject("Failed to set viewport size: zoom workaround failed.");
                                    });
                                }

                                reject("Failed to set viewport size!");
                            });
                        });
                    });
                }).catch(function (err) {
                    reject(err);
                });
            } catch (err) {
                reject(new Error(err));
            }
        });
    };

    /**
     * @private
     * @param {Logger} logger
     * @param {Web.Browser} browser
     * @param {{width: number, height: number}} requiredSize
     * @param actualViewportSize
     * @param browserSize
     * @param widthDiff
     * @param widthStep
     * @param heightDiff
     * @param heightStep
     * @param currWidthChange
     * @param currHeightChange
     * @param retriesLeft
     * @param lastRequiredBrowserSize
     * @param {PromiseFactory} promiseFactory
     * @return {Promise<void>}
     */
    function setWindowSize(logger,
                            browser,
                            requiredSize,
                            actualViewportSize,
                            browserSize,
                            widthDiff,
                            widthStep,
                            heightDiff,
                            heightStep,
                            currWidthChange,
                            currHeightChange,
                            retriesLeft,
                            lastRequiredBrowserSize,
                            promiseFactory) {
        return promiseFactory.makePromise(function (resolve, reject) {
            logger.verbose("Retries left: " + retriesLeft);
            // We specifically use "<=" (and not "<"), so to give an extra resize attempt
            // in addition to reaching the diff, due to floating point issues.
            if (Math.abs(currWidthChange) <= Math.abs(widthDiff) && actualViewportSize.width !== requiredSize.width) {
                currWidthChange += widthStep;
            }

            if (Math.abs(currHeightChange) <= Math.abs(heightDiff) && actualViewportSize.height !== requiredSize.height) {
                currHeightChange += heightStep;
            }

            var requiredBrowserSize = {
                width: browserSize.width + currWidthChange,
                height: browserSize.height + currHeightChange
            };

            if (lastRequiredBrowserSize && requiredBrowserSize.width === lastRequiredBrowserSize.width && requiredBrowserSize.height === lastRequiredBrowserSize.height) {
                logger.verbose("Browser size is as required but viewport size does not match!");
                logger.verbose("Browser size: " + requiredBrowserSize + " , Viewport size: " + actualViewportSize);
                logger.verbose("Stopping viewport size attempts.");
                resolve();
                return;
            }

            return EyesLeanFTUtils.setBrowserSize(logger, browser, requiredBrowserSize, promiseFactory).then(function () {
                lastRequiredBrowserSize = requiredBrowserSize;
                return EyesLeanFTUtils.getViewportSize(browser, promiseFactory);
            }).then(function (actualViewportSize) {
                logger.verbose("Current viewport size:", actualViewportSize);
                if (actualViewportSize.width === requiredSize.width && actualViewportSize.height === requiredSize.height) {
                    resolve();
                    return;
                }

                if ((Math.abs(currWidthChange) <= Math.abs(widthDiff) || Math.abs(currHeightChange) <= Math.abs(heightDiff)) && (--retriesLeft > 0)) {
                    return setWindowSize(logger, browser, requiredSize, actualViewportSize, browserSize,
                        widthDiff, widthStep, heightDiff, heightStep, currWidthChange, currHeightChange,
                        retriesLeft, lastRequiredBrowserSize, promiseFactory).then(function () {
                        resolve();
                    }, function (err) {
                        reject(err);
                    });
                }

                reject("Failed to set window size!");
            });
        });
    }

    /**
     * @private
     * @param {{left: number, top: number, width: number, height: number}} part
     * @param {{position: {x: number, y: number}, size: {width: number, height: number}, image: Buffer}[]} parts
     * @param {{imageBuffer: Buffer, width: number, height: number}} imageObj
     * @param {Web.Browser} browser
     * @param {Promise<void>} promise
     * @param {PromiseFactory} promiseFactory
     * @param {{width: number, height: number}} viewportSize
     * @param {PositionProvider} positionProvider
     * @param {ScaleProviderFactory} scaleProviderFactory
     * @param {CutProvider} cutProvider
     * @param {{width: number, height: number}} entirePageSize
     * @param {number} pixelRatio
     * @param {number} rotationDegrees
     * @param {boolean} automaticRotation
     * @param {number} automaticRotationDegrees
     * @param {boolean} isLandscape
     * @param {number} waitBeforeScreenshots
     * @param {{left: number, top: number, width: number, height: number}} regionInScreenshot
     * @param {boolean} [saveDebugScreenshots=false]
     * @param {string} [debugScreenshotsPath=null]
     * @return {Promise<void>}
     */
    var _processPart = function (part,
                                 parts,
                                 imageObj,
                                 browser,
                                 promise,
                                 promiseFactory,
                                 viewportSize,
                                 positionProvider,
                                 scaleProviderFactory,
                                 cutProvider,
                                 entirePageSize,
                                 pixelRatio,
                                 rotationDegrees,
                                 automaticRotation,
                                 automaticRotationDegrees,
                                 isLandscape,
                                 waitBeforeScreenshots,
                                 regionInScreenshot,
                                 saveDebugScreenshots,
                                 debugScreenshotsPath) {
        return promise.then(function () {
            return promiseFactory.makePromise(function (resolve) {
                // Skip 0,0 as we already got the screenshot
                if (part.left === 0 && part.top === 0) {
                    parts.push({
                        image: imageObj.imageBuffer,
                        size: {width: imageObj.width, height: imageObj.height},
                        position: {x: 0, y: 0}
                    });

                    resolve();
                    return;
                }

                var partPosition = {x: part.left, y: part.top};
                return positionProvider.setPosition(partPosition).then(function () {
                    return positionProvider.getCurrentPosition();
                }).then(function (currentPosition) {
                    return _captureViewport(browser, promiseFactory, viewportSize, scaleProviderFactory, cutProvider, entirePageSize,
                        pixelRatio, rotationDegrees, automaticRotation, automaticRotationDegrees, isLandscape,
                        waitBeforeScreenshots, regionInScreenshot, saveDebugScreenshots, debugScreenshotsPath).then(function (partImage) {
                        return partImage.asObject();
                    }).then(function (partObj) {
                        parts.push({
                            image: partObj.imageBuffer,
                            size: {width: partObj.width, height: partObj.height},
                            position: {x: currentPosition.x, y: currentPosition.y}
                        });

                        resolve();
                    });
                });
            });
        });
    };

    function formatDate(date) {
        if (date === null || date === undefined) {
            date = new Date();
        }

        var day = date.getDate();
        var month = date.getMonth() + 1;
        var year = date.getFullYear();
        var hour = date.getHours();
        var minute = date.getMinutes();
        var second = date.getSeconds();

        if (day < 10) day = '0' + day;
        if (month < 10) month = '0' + month;
        if (year < 10) year = '0' + year;
        if (hour < 10) hour = '0' + hour;
        if (minute < 10) minute = '0' + minute;
        if (second < 10) second = '0' + second;

        return '' + year + '_' + month + '_' + day + '-' + hour + '_' + minute + '_' + second;
    }

    /**
     * @private
     * @param {EyesWebBrowser|EyesStdWinWindow} browser
     * @param {PromiseFactory} promiseFactory
     * @param {{width: number, height: number}} viewportSize
     * @param {ScaleProviderFactory} scaleProviderFactory
     * @param {CutProvider} cutProvider
     * @param {{width: number, height: number}} entirePageSize
     * @param {number} pixelRatio
     * @param {number} rotationDegrees
     * @param {boolean} automaticRotation
     * @param {number} automaticRotationDegrees
     * @param {boolean} isLandscape
     * @param {number} waitBeforeScreenshots
     * @param {{left: number, top: number, width: number, height: number}} [regionInScreenshot]
     * @param {boolean} [saveDebugScreenshots=false]
     * @param {string} [debugScreenshotsPath=null]
     * @return {Promise<MutableImage>}
     */
    var _captureViewport = function (browser,
                                     promiseFactory,
                                     viewportSize,
                                     scaleProviderFactory,
                                     cutProvider,
                                     entirePageSize,
                                     pixelRatio,
                                     rotationDegrees,
                                     automaticRotation,
                                     automaticRotationDegrees,
                                     isLandscape,
                                     waitBeforeScreenshots,
                                     regionInScreenshot,
                                     saveDebugScreenshots,
                                     debugScreenshotsPath) {
        var mutableImage, scaleRatio = 1;
        return GeneralUtils.sleep(waitBeforeScreenshots, promiseFactory).then(function () {
            return browser.takeScreenshot().then(function (screenshot64) {
                return new MutableImage(new Buffer(screenshot64, 'base64'), promiseFactory);
            }).then(function (image) {
                mutableImage = image;
                if (saveDebugScreenshots) {
                    var filename = "screenshot " + formatDate() + " original.png";
                    return mutableImage.saveImage(debugScreenshotsPath + filename.replace(/ /g, '_'));
                }
            }).then(function () {
                if (cutProvider) {
                    return cutProvider.cut(mutableImage, promiseFactory).then(function (image) {
                        mutableImage = image;
                    });
                }
            }).then(function () {
                return mutableImage.getSize();
            }).then(function (imageSize) {
                if (isLandscape && automaticRotation && imageSize.height > imageSize.width) {
                    rotationDegrees = automaticRotationDegrees;
                }

                if (scaleProviderFactory) {
                    var scaleProvider = scaleProviderFactory.getScaleProvider(imageSize.width);
                    scaleRatio = scaleProvider.getScaleRatio();
                }

                if (regionInScreenshot) {
                    var scaledRegion = GeometryUtils.scaleRegion(regionInScreenshot, 1 / scaleRatio);
                    return mutableImage.cropImage(scaledRegion);
                }
            }).then(function () {
                if (saveDebugScreenshots) {
                    var filename = "screenshot " + formatDate() + " cropped.png";
                    return mutableImage.saveImage(debugScreenshotsPath + filename.replace(/ /g, '_'));
                }
            }).then(function () {
                if (scaleRatio !== 1) {
                    return mutableImage.scaleImage(scaleRatio);
                }
            }).then(function () {
                if (saveDebugScreenshots) {
                    var filename = "screenshot " + formatDate() + " scaled.png";
                    return mutableImage.saveImage(debugScreenshotsPath + filename.replace(/ /g, '_'));
                }
            }).then(function () {
                if (rotationDegrees !== 0) {
                    return mutableImage.rotateImage(rotationDegrees);
                }
            }).then(function () {
                return mutableImage.getSize();
            }).then(function (imageSize) {
                // If the image is a viewport screenshot, we want to save the current scroll position (we'll need it for check region).
                if (imageSize.width <= viewportSize.width && imageSize.height <= viewportSize.height) {
                    return EyesLeanFTUtils.getCurrentScrollPosition(browser, promiseFactory).then(function (scrollPosition) {
                        return mutableImage.setCoordinates(scrollPosition);
                    }, function () {
                        // Failed to get Scroll position, setting coordinates to default.
                        return mutableImage.setCoordinates({x: 0, y: 0});
                    });
                }
            }).then(function () {
                return mutableImage;
            });
        });
    };

    /**
     * Capture screenshot from given driver
     *
     * @param {Web.Browser} browser
     * @param {PromiseFactory} promiseFactory
     * @param {{width: number, height: number}} viewportSize
     * @param {PositionProvider} positionProvider
     * @param {ScaleProviderFactory} scaleProviderFactory
     * @param {CutProvider} cutProvider
     * @param {boolean} fullPage
     * @param {boolean} hideScrollbars
     * @param {boolean} useCssTransition
     * @param {number} rotationDegrees
     * @param {boolean} automaticRotation
     * @param {number} automaticRotationDegrees
     * @param {boolean} isLandscape
     * @param {number} waitBeforeScreenshots
     * @param {boolean} checkFrameOrElement
     * @param {RegionProvider} [regionProvider]
     * @param {boolean} [saveDebugScreenshots=false]
     * @param {string} [debugScreenshotsPath=null]
     * @return {Promise<MutableImage>}
     */
    EyesLeanFTUtils.getScreenshot = function getScreenshot(browser,
                                                           promiseFactory,
                                                           viewportSize,
                                                           positionProvider,
                                                           scaleProviderFactory,
                                                           cutProvider,
                                                           fullPage,
                                                           hideScrollbars,
                                                           useCssTransition,
                                                           rotationDegrees,
                                                           automaticRotation,
                                                           automaticRotationDegrees,
                                                           isLandscape,
                                                           waitBeforeScreenshots,
                                                           checkFrameOrElement,
                                                           regionProvider,
                                                           saveDebugScreenshots,
                                                           debugScreenshotsPath) {
        var MIN_SCREENSHOT_PART_HEIGHT = 10,
            MAX_SCROLLBAR_SIZE = 50;
        var originalPosition,
            originalOverflow,
            entirePageSize,
            regionInScreenshot,
            pixelRatio,
            imageObject,
            screenshot;

        hideScrollbars = hideScrollbars === null ? useCssTransition : hideScrollbars;

        // step #1 - get entire page size for future use (scaling and stitching)
        return positionProvider.getEntireSize().then(function (pageSize) {
            entirePageSize = pageSize;
        }, function () {
            // Couldn't get entire page size, using viewport size as default.
            entirePageSize = viewportSize;
        }).then(function () {
            // step #2 - get the device pixel ratio (scaling)
            return EyesLeanFTUtils.getDevicePixelRatio(browser, promiseFactory).then(function (ratio) {
                pixelRatio = ratio;
            }, function () {
                // Couldn't get pixel ratio, using 1 as default.
                pixelRatio = 1;
            });
        }).then(function () {
            // step #3 - hide the scrollbars if instructed
            if (hideScrollbars) {
                return EyesLeanFTUtils.setOverflow(browser, "hidden", promiseFactory).then(function (originalVal) {
                    originalOverflow = originalVal;
                });
            }
        }).then(function () {
            // step #4 - if this is a full page screenshot we need to scroll to position 0,0 before taking the first
            if (fullPage) {
                return positionProvider.getState().then(function (state) {
                    originalPosition = state;
                    return positionProvider.setPosition({x: 0, y: 0});
                }).then(function () {
                    return positionProvider.getCurrentPosition();
                }).then(function (location) {
                    if (location.x !== 0 || location.y !== 0) {
                        throw new Error("Could not scroll to the x/y corner of the screen");
                    }
                });
            }
        }).then(function () {
            if (regionProvider) {
                return _captureViewport(browser, promiseFactory, viewportSize, scaleProviderFactory, cutProvider, entirePageSize, pixelRatio,
                    rotationDegrees, automaticRotation, automaticRotationDegrees, isLandscape, waitBeforeScreenshots).then(function (image) {
                    return regionProvider.getRegionInLocation(image, CoordinatesType.SCREENSHOT_AS_IS, promiseFactory);
                }).then(function (region) {
                    regionInScreenshot = region;
                });
            }
        }).then(function () {
            // step #5 - Take screenshot of the 0,0 tile / current viewport
            return _captureViewport(browser, promiseFactory, viewportSize, scaleProviderFactory, cutProvider, entirePageSize, pixelRatio, rotationDegrees,
                automaticRotation, automaticRotationDegrees, isLandscape, waitBeforeScreenshots,
                checkFrameOrElement ? regionInScreenshot : null, saveDebugScreenshots, debugScreenshotsPath)
                .then(function (image) {
                    screenshot = image;
                    return screenshot.asObject();
                }).then(function (imageObj) {
                    imageObject = imageObj;
                });
        }).then(function () {
            return promiseFactory.makePromise(function (resolve) {
                if (!fullPage && !checkFrameOrElement) {
                    resolve();
                    return;
                }
                // IMPORTANT This is required! Since when calculating the screenshot parts for full size,
                // we use a screenshot size which is a bit smaller (see comment below).
                if (imageObject.width >= entirePageSize.width && imageObject.height >= entirePageSize.height) {
                    resolve();
                    return;
                }

                // We use a smaller size than the actual screenshot size in order to eliminate duplication
                // of bottom scroll bars, as well as footer-like elements with fixed position.
                var screenshotPartSize = {
                    width: imageObject.width,
                    height: Math.max(imageObject.height - MAX_SCROLLBAR_SIZE, MIN_SCREENSHOT_PART_HEIGHT)
                };

                var screenshotParts = GeometryUtils.getSubRegions({
                    left: 0, top: 0, width: entirePageSize.width,
                    height: entirePageSize.height
                }, screenshotPartSize, false);

                var parts = [];
                var promise = promiseFactory.makePromise(function (resolve) {
                    resolve();
                });

                screenshotParts.forEach(function (part) {
                    promise = _processPart(part, parts, imageObject, browser, promise, promiseFactory,
                        viewportSize, positionProvider, scaleProviderFactory, cutProvider, entirePageSize, pixelRatio, rotationDegrees, automaticRotation,
                        automaticRotationDegrees, isLandscape, waitBeforeScreenshots, checkFrameOrElement ? regionInScreenshot : null, saveDebugScreenshots, debugScreenshotsPath);
                });
                promise.then(function () {
                    return ImageUtils.stitchImage(entirePageSize, parts, promiseFactory).then(function (stitchedBuffer) {
                        screenshot = new MutableImage(stitchedBuffer, promiseFactory);
                        resolve();
                    });
                });
            });
        }).then(function () {
            if (hideScrollbars) {
                return EyesLeanFTUtils.setOverflow(browser, originalOverflow, promiseFactory);
            }
        }).then(function () {
            if (fullPage) {
                return positionProvider.restoreState(originalPosition);
            }
        }).then(function () {
            if (!checkFrameOrElement && regionInScreenshot) {
                return screenshot.cropImage(regionInScreenshot);
            }
        }).then(function () {
            return screenshot;
        });
    };

    exports.EyesLeanFTUtils = EyesLeanFTUtils;
}());
