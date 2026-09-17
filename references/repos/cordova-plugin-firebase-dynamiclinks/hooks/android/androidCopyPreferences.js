const et = require('elementtree');
const path = require('path');
const fs = require('fs');
const { ConfigParser } = require('cordova-common');
const { Console } = require('console');

module.exports = function (context) {
    var projectRoot = context.opts.cordova.project ? context.opts.cordova.project.root : context.opts.projectRoot;
    var configXML = path.join(projectRoot, 'config.xml');
    var configParser = new ConfigParser(configXML);
    var app_domain_name = configParser.getGlobalPreference("FIREBASE_DOMAIN_URL_PREFIX");

    //ANDROID
    //go inside the AndroidManifest and change value for APP_DOMAIN_NAME
    var manifestPath = path.join(projectRoot, 'platforms/android/app/src/main/AndroidManifest.xml');
    var manifestFile = fs.readFileSync(manifestPath).toString();
    var etreeManifest = et.parse(manifestFile);

    var dataTags = etreeManifest.findall('./application/activity/intent-filter/data[@android:host="firebase_domain_url_prefix"]');
    for (var i = 0; i < dataTags.length; i++) {
        var data = dataTags[i];
        data.set("android:host", app_domain_name);
    }

    var resultXmlManifest = etreeManifest.write();
    fs.writeFileSync(manifestPath, resultXmlManifest);
    
    //change the config.xml
    var configAndroidPath = path.join(projectRoot, 'platforms/android/app/src/main/res/xml/config.xml');
    var configAndroidParser = new ConfigParser(configAndroidPath);
    var oldDomainUriPrefix = configAndroidParser.getGlobalPreference("DOMAIN_URI_PREFIX");
    var newDomainUriPrefix = oldDomainUriPrefix.replace('firebase_domain_url_prefix', app_domain_name);
    configAndroidParser.setGlobalPreference("DOMAIN_URI_PREFIX", newDomainUriPrefix);
    configAndroidParser.write();

};
