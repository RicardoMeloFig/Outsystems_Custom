var exec = require('cordova/exec');

exports.getToken = function (success, error) {
    exec(success, error, 'OSFirebaseCloudMessaging', 'getToken');
};

exports.getAPNsToken = function (success, error) {
    exec(success, error, 'OSFirebaseCloudMessaging', 'getAPNsToken');
};

exports.subscribe = function (topic, success, error) {
    exec(success, error, 'OSFirebaseCloudMessaging', 'subscribe', [topic]);
};

exports.unsubscribe = function (topic, success, error) {
    exec(success, error, 'OSFirebaseCloudMessaging', 'unsubscribe', [topic]);
};

exports.clearNotifications = function (success, error) {
    exec(success, error, 'OSFirebaseCloudMessaging', 'clearNotifications');
};

exports.setBadge = function (badge, success, error) {
    exec(success, error, 'OSFirebaseCloudMessaging', 'setBadge', [badge]);
};

exports.getBadge = function (success, error) {
    exec(success, error, 'OSFirebaseCloudMessaging', 'getBadge');
};

exports.sendLocalNotification = function (badge, title, body, channelName, channelDescription, color, icon, success, error) {
    exec(success, error, 'OSFirebaseCloudMessaging', 'sendLocalNotification', [badge, title, body, channelName, channelDescription, color, icon]);
};

exports.registerDevice = function (success, error) {
    exec(success, error, 'OSFirebaseCloudMessaging', 'registerDevice');
};

exports.unregisterDevice = function (success, error) {
    exec(success, error, 'OSFirebaseCloudMessaging', 'unregisterDevice');
};

exports.getPendingNotifications = function (clearFromDatabase, success, error) {
    exec(success, error, 'OSFirebaseCloudMessaging', 'getPendingNotifications', [clearFromDatabase]);
};

exports.setDeliveryMetricsExportToBigQuery = function(enable, success, error) {
    exec(success, error, 'OSFirebaseCloudMessaging', 'setDeliveryMetricsExportToBigQuery', [{enable}]);
}

exports.deliveryMetricsExportToBigQueryEnabled = function(success, error) {
    exec(success, error, 'OSFirebaseCloudMessaging', 'deliveryMetricsExportToBigQueryEnabled')
}

// Event listener
exports._listener = {};

/**
 * Register callback for given event.
 *
 * @param [ String ]   event    The name of the event.
 * @param [ Function ] callback The function to be exec as callback.
 *
 * @return [ Void ]
 */
 exports.on = function (event, callback) {
    var type = typeof callback;

    if (type !== 'function' && type !== 'string')
        return;

    if (!this._listener[event]) {
        this._listener[event] = [];
    }

    var item = [callback, window];

    this._listener[event].push(item);
};

/**
 * Unregister callback for given event.
 *
 * @param [ String ]   event    The name of the event.
 * @param [ Function ] callback The function to be exec as callback.
 *
 * @return [ Void ]
 */
 exports.un = function (event, callback) {
    var listener = this._listener[event];

    if (!listener)
        return;

    for (var i = 0; i < listener.length; i++) {
        var fn = listener[i][0];

        if (fn == callback) {
            listener.splice(i, 1);
            break;
        }
    }
};

/**
 * Create a callback function to get executed within a specific scope.
 *
 * @param [ Function ] fn    The function to be exec as the callback.
 * @param [ Object ]   scope The callback function's scope.
 *
 * @return [ Function ]
 */
exports._createCallbackFn = function (fn, scope) {

    if (typeof fn != 'function')
        return;

    return function () {
        fn.apply(scope || this, arguments);
    };
};

/**
 * Execute the native counterpart.
 *
 * @param [ String ]  action   The name of the action.
 * @param [ Array ]   args     Array of arguments.
 * @param [ Function] callback The callback function.
 * @param [ Object ] scope     The scope for the function.
 *
 * @return [ Void ]
 */
exports._exec = function (action, args, callback, scope) {
    var fn     = this._createCallbackFn(callback, scope),
        params = [];

    if (Array.isArray(args)) {
        params = args;
    } else if (args !== null) {
        params.push(args);
    }

    exec(fn, null, 'OSFirebaseCloudMessaging', action, params);
};

/**
 * Fire queued events once the device is ready and all listeners are registered.
 *
 * @return [ Void ]
 */
exports.fireQueuedEvents = function() {
    exports._exec('ready');
};

/**
 * Fire the event with given arguments.
 *
 * @param [ String ] event The event's name.
 * @param [ *Array]  args  The callback's arguments.
 *
 * @return [ Void]
 */
 exports.fireEvent = function (event) {
    var args     = Array.apply(null, arguments).slice(1),
        listener = this._listener[event];

    if (!listener)
        return;

    if (args[0] && typeof args[0].data === 'string') {
        args[0].data = JSON.parse(args[0].data);
    }

    for (var i = 0; i < listener.length; i++) {
        var fn    = listener[i][0],
            scope = listener[i][1];

        if (typeof fn !== 'function') {
            fn = scope[fn];
        }

        fn.apply(scope, args);
    }
};