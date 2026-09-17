/** 
	OutSystems - Mobility Experts
	Vitor Oliveira - 14/10/2015
*/

function BlinkId() {
}

exports.readCardId = function (successCallback, errorCallback, licenseKey) {
    cordova.exec(successCallback, errorCallback, "BlinkIdPlugin", "readCardId", [licenseKey]);
};

exports.scannDocument = function (successCallback, errorCallback) {
   // cordova.exec(successCallback, errorCallback, "BlinkIdPlugin", "scannDocument", []);
   alert ('Comming soon!');
};