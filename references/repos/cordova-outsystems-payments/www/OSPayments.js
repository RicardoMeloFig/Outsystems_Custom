var exec = require('cordova/exec');

exports.setupConfiguration = function(paymentEnvironment, success, error) {
    exec(success, error, 'OSPayments', 'setupConfiguration', [paymentEnvironment]);
};

exports.checkWalletSetup = function(success, error) {
    exec(success, error, 'OSPayments', 'checkWalletSetup');
};

exports.setDetails = function(paymentDetails, accessToken, success, error) {
    exec(success, error, 'OSPayments', 'setDetails', [paymentDetails, accessToken]);
}