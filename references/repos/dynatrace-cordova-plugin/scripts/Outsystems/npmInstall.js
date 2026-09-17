var child_process = require('child_process');


function isCordovaAbove (context, version) {
  var cordovaVersion = context.opts.cordova.version;
  console.log(cordovaVersion);
  var sp = cordovaVersion.split('.');
  return parseInt(sp[0]) >= version;
}

module.exports = function (context) {
    var cordovaAbove8 = isCordovaAbove(context, 8);
    if (!cordovaAbove8) {
      var deferral = context.requireCordovaModule("q").defer();
      child_process.exec('npm install', {cwd:__dirname},
        function (error) {
          if (error !== null) {
            console.log('exec error: ' + error);
            deferral.reject('npm installation failed');
          }
          deferral.resolve();
      });

      return deferral.promise;
    }
    return;

	
};
