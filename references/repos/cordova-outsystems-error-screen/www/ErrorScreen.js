
var exec = require('cordova/exec');

var PLUGIN_NAME = 'ErrorScreen';

var errorscreen = {
  show: function() {
    exec(null, null, PLUGIN_NAME, 'show', []);
  },
  hide: function() {
    exec(null, null, PLUGIN_NAME, 'hide', []);
  }
};

module.exports = errorscreen;
