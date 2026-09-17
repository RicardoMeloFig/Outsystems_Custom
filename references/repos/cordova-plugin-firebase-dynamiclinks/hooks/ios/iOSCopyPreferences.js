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

    //IOS
    //change config.xml
    var appNamePath = path.join(projectRoot, 'config.xml');
    var appNameParser = new ConfigParser(appNamePath);
    var appName = appNameParser.name();

    var configiOSPath = path.join(projectRoot, 'platforms/ios/' + appName + '/config.xml');
    var configiOSParser = new ConfigParser(configiOSPath);
    var oldDomainUriPrefix = configiOSParser.getGlobalPreference("DOMAIN_URI_PREFIX");
    var newDomainUriPrefix = oldDomainUriPrefix.replace('firebase_domain_url_prefix', app_domain_name);
    configiOSParser.setGlobalPreference("DOMAIN_URI_PREFIX", newDomainUriPrefix);
    configiOSParser.write();

    //Change info.plist
    var infoPlistPath = path.join(projectRoot, 'platforms/ios/' + appName + '/'+ appName +'-info.plist');
    var infoPlistFile = fs.readFileSync(infoPlistPath).toString();
    var etreeInfoPlist = et.parse(infoPlistFile);
    var infoPlistTags = etreeInfoPlist.findall('./dict/array/string');

    for (var i = 0; i < infoPlistTags.length; i++) {
        if (infoPlistTags[i].text.includes("firebase_domain_url_prefix")) {
            infoPlistTags[i].text = infoPlistTags[i].text.replace('firebase_domain_url_prefix', app_domain_name)
        }
    }

    var resultXmlInfoPlist = etreeInfoPlist.write();
    fs.writeFileSync(infoPlistPath, resultXmlInfoPlist);

    //Change Entitlements-Debug.plist
    var debugPlistPath = path.join(projectRoot, 'platforms/ios/' + appName + '/Entitlements-Debug.plist');
    var debugPlistFile = fs.readFileSync(debugPlistPath).toString();
    var etreeDebugPlist = et.parse(debugPlistFile);
    var debugPlistTags = etreeDebugPlist.findall('./dict/array/string');

    for (var i = 0; i < debugPlistTags.length; i++) {
        if (debugPlistTags[i].text.includes("firebase_domain_url_prefix")) {
            debugPlistTags[i].text = debugPlistTags[i].text.replace('firebase_domain_url_prefix', app_domain_name)
        }
    }

    var resultXmlDebugPlist = etreeDebugPlist.write();
    fs.writeFileSync(debugPlistPath, resultXmlDebugPlist);

    //Change Entitlements-Release.plist
    var releasePlistPath = path.join(projectRoot, 'platforms/ios/' + appName + '/Entitlements-Release.plist');
    var releasePlistFile = fs.readFileSync(releasePlistPath).toString();
    var etreeReleasePlist = et.parse(releasePlistFile);
    var releasePlistTags = etreeReleasePlist.findall('./dict/array/string');

    for (var i = 0; i < releasePlistTags.length; i++) {
        if (releasePlistTags[i].text.includes("firebase_domain_url_prefix")) {
            releasePlistTags[i].text = releasePlistTags[i].text.replace('firebase_domain_url_prefix', app_domain_name)
        }
    }

    var resultXmlReleasePlist = etreeReleasePlist.write();
    fs.writeFileSync(releasePlistPath, resultXmlReleasePlist);

};
