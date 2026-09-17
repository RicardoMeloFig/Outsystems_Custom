/** 
	OutSystems - Mobile Services
	Vitor Oliveira - 04/08/2015
*/

function CacheCoordinates() {
}

exports.init = function (successCallback, errorCallback, timeBetweenUpdates, metersBetweenUpdates) {
	cordova.exec(successCallback, errorCallback, "CacheCoordinatesPlugin", "initCacheLocation", [timeBetweenUpdates, metersBetweenUpdates]);
};

exports.getCoordinates = function (successCallback, errorCallback) {
    cordova.exec(successCallback, errorCallback, "CacheCoordinatesPlugin", "getCacheLocation", []);
};

exports.stop = function (successCallback, errorCallback) {
    cordova.exec(successCallback, errorCallback, "CacheCoordinatesPlugin", "stopCacheLocation", []);
};