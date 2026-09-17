const fs = require('fs');
const path = require('path');
const { ConfigParser } = require('cordova-common');

const CORDOVA_PREFERENCE_NAME = 'InAppBrowserCleartextTrafficPermitted';
const ANDROID_PREFERENCE_NAME = 'cleartextTrafficPermitted';

/**
 * Validates if the cleartextTrafficPermitted option should be enabled
 * @param {object} context Cordova context
 * @returns {boolean} true if the option should be enabled
 */
function shouldEnableCleartextTrafficPermitted(context) {
    const projectRoot = context.opts.projectRoot;
    const configXML = path.join(projectRoot, 'config.xml');
    const configParser = new ConfigParser(configXML);
    const enable = configParser.getPlatformPreference(CORDOVA_PREFERENCE_NAME, 'android');
    return enable.toLowerCase() === 'true';
}

/**
 * Enables the cleartextTrafficPermitted option
 * @param {object} context Cordova context
 */
function enableCleartextTrafficPermitted(context) {
    console.log('Enabling ' + ANDROID_PREFERENCE_NAME + ' option');

    const projectRoot = context.opts.projectRoot;
    const config = path.join(projectRoot, 'res', 'android', 'xml', 'network_security_config.xml');

    if (fs.existsSync(config)) {
        fs.readFile(config, 'utf8', function (err, data) {
            if (err) {
                throw new Error('Unable to find network_security_config.xml: ' + err);
            }

            if (data.indexOf(ANDROID_PREFERENCE_NAME) == -1) {
                const result = data.replace(/<base-config/g, '<base-config ' + ANDROID_PREFERENCE_NAME + '="true"');

                fs.writeFile(config, result, 'utf8', function (err) {
                    if (err) {
                        throw new Error('Unable to write into network_security_config.xml: ' + err);
                    }
                })
            }
        });
    }
}


module.exports = function(context) {
    return new Promise(function(resolve) {

        if (shouldEnableCleartextTrafficPermitted(context)) {
            enableCleartextTrafficPermitted(context);
        }

        return resolve();
    });
};
