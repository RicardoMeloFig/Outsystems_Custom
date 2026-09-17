
console.log("Running hook to install NodeJS requirements");

module.exports = function (context) {
  var child_process = require('child_process'),
      deferral = require('q').defer();

  child_process.exec('npm install', {cwd: __dirname}, function (error) {
    if (error !== null) {
      console.log('exec error: ' + error);
      throw new Error("OUTSYSTEMS_PLUGIN_ERROR: Couldn't finish npm install.");
    }
    else {
      deferral.resolve();
    }
  });

  return deferral.promise;
};
