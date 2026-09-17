const et = require('elementtree');
const path = require('path');
const fs = require('fs');
const plist = require('plist');
const child_process = require('child_process');
const { ConfigParser } = require('cordova-common');
const { Console } = require('console');

module.exports = function (context) {
    var projectRoot = context.opts.cordova.project ? context.opts.cordova.project.root : context.opts.projectRoot;

    let configPath = path.join(projectRoot, 'config.xml');
    let configParser = new ConfigParser(configPath);

    let appName = configParser.name();
    let applicationSchemes = configParser.getPreference("APPLICATION_SCHEMES", "ios");

    if (applicationSchemes != null && applicationSchemes !== "") {
        let applicationQueriesSchemes = applicationSchemes.split(",");

        let platformPath = path.join(projectRoot, 'platforms/ios');

        //Change info.plist
        let infoPlistPath = path.join(platformPath, appName + '/'+ appName +'-info.plist');
        let infoPlistFile = fs.readFileSync(infoPlistPath, 'utf8');
        var infoPlist = plist.parse(infoPlistFile);

        let currentValue = infoPlist['LSApplicationQueriesSchemes'];
        if (currentValue != null && currentValue.length > 0) {
            applicationQueriesSchemes = currentValue.concat(applicationQueriesSchemes);
        }
        infoPlist['LSApplicationQueriesSchemes'] = [...new Set(applicationQueriesSchemes)];

        fs.writeFileSync(infoPlistPath, plist.build(infoPlist, { indent: '\t' }));
    }
};
