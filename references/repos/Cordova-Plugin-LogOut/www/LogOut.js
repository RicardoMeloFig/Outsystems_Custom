/** 
	OutSystems - Mobile Services
	Vitor Oliveira - 03/03/2015

*/

function LogOut() {
}

exports.logOut = function (successCallback, errorCallback) {
    cordova.exec(successCallback, errorCallback, "LogOut", "logOut", []);
};

exports.logOutWebView = function (successCallback, errorCallback) {
   cordova.exec(successCallback, errorCallback, "LogOut", "logOutWebView", []);
};