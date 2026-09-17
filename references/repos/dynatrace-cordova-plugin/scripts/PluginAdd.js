"use strict";
module.exports = function (context) {
    if (context !== undefined && context.opts !== undefined && context.opts.plugins !== undefined) {
        var plugins = context.opts.plugins;
        for (var i = 0; i < plugins.length; i++) {
            if (plugins[i].endsWith('@dynatrace/cordova-plugin')) {
                delete plugins[i];
                return;
            }
        }
    }
};
