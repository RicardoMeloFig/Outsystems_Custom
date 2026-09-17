var exec = require('cordova/exec');

exports.login = function (success, error, provider) {
    exec(success, error, 'OSSocialLogins', 'login', [provider]);
};

exports.loginApple = function (success, error, state, clientId, redirectUrl) {
    exec(success, error, 'OSSocialLogins', 'loginApple', [state, clientId, redirectUrl]);
};

exports.loginGoogle = function (success, error, clientId, redirectUrl) {
    exec(success, error, 'OSSocialLogins', 'loginGoogle', [clientId, redirectUrl]);
};

exports.loginLinkedIn = function (success, error, state, clientId, redirectUrl) {
    exec(success, error, 'OSSocialLogins', 'loginLinkedIn', [state, clientId, redirectUrl]);
};

exports.loginFacebook = function (success, error) {
    exec(success, error, 'OSSocialLogins', 'loginFacebook');
};

exports.logout = function (arg0, success, error) {
    exec(success, error, 'OSSocialLogins', 'logout', [arg0]);
};

exports.getLoginData = function (arg0, success, error) {
    exec(success, error, 'OSSocialLogins', 'getLoginData', [arg0]);
};

exports.checkLoginStatus = function (success, error, provider) {
    exec(success, error, 'OSSocialLogins', 'checkLoginStatus', [provider]);
};
