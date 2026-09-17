var exec = cordova.require('cordova/exec');
var channel = cordova.require('cordova/channel');

function OSAppFeedbackPlugin() {
}

var OSAppFeedback = new OSAppFeedbackPlugin();

module.exports = OSAppFeedback;

OSAppFeedback.isAvailable =  function(successCallBack,errorCallback){
	exec(successCallBack,errorCallback,"OSAppFeedback","isAvailable",[]);
};


OSAppFeedback.open = function(successCallBack,errorCallback){
	exec(successCallBack,errorCallback,"OSAppFeedback","openECT",[]);	
};


channel.deviceready.subscribe(function () {
    var hostname = location.hostname;
    exec(null,null,'OSAppFeedback','deviceready',[hostname]);
});