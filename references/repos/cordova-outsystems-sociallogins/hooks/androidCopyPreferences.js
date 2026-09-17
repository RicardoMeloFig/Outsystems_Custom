const et = require('elementtree');
const path = require('path');
const fs = require('fs');
const { ConfigParser } = require('cordova-common');

const {getJsonFile, ProvidersEnum, ApplicationTypeEnum} = require('./utils');

module.exports = async function (context) {
    let projectRoot = context.opts.cordova.project ? context.opts.cordova.project.root : context.opts.projectRoot;
    let configXML = path.join(projectRoot, 'config.xml');
    let configParser = new ConfigParser(configXML);
    let configuratorURL = configParser.getGlobalPreference("SOCIAL_CONF_API_ENDPOINT");
    
    if(configuratorURL.length == 0)
        throw new Error("OUTSYSTEMS_PLUGIN_ERROR: Missing preference SOCIAL_CONF_API_ENDPOINT. Please make sure this preference is configured");
    
    let appName = configParser.name();
    
    let jsonConfig = await getJsonFile(configuratorURL, appName);
    copyFacebookPreferences(jsonConfig, projectRoot);
    copyLinkedInPreferences(jsonConfig, projectRoot);
};

function copyLinkedInPreferences(jsonConfig, projectRoot) {
    let linkedin_deeplink_url = "";
    let linkedin_deeplink_host = "";
    let linkedin_deeplink_path = "";

    try {
        linkedin_deeplink_url = jsonConfig.app_deeplink.url_scheme.replace(/\s/g, '')
        linkedin_deeplink_host = jsonConfig.app_deeplink.url_host.replace(/\s/g, '')
        linkedin_deeplink_path = jsonConfig.app_deeplink.url_path.replace(/\s/g, '')
    } catch {
        throw new Error("OUTSYSTEMS_PLUGIN_ERROR: Missing configuration file or error trying to obtain the configuration.");
    }
    
    //go inside the AndroidManifest and change values for schema, host and path
    let manifestPath = path.join(projectRoot, 'platforms/android/app/src/main/AndroidManifest.xml');
    let manifestFile = fs.readFileSync(manifestPath).toString();
    let etreeManifest = et.parse(manifestFile);

    let dataTags = etreeManifest.findall('./application/activity/intent-filter/data[@android:scheme="OAUTH_DEEPLINK_SCHEME"]');
    dataTags.forEach((data) => data.set("android:scheme", linkedin_deeplink_url));
    dataTags = null;

    dataTags = etreeManifest.findall('./application/activity/intent-filter/data[@android:host="OAUTH_DEEPLINK_HOST"]');
    dataTags.forEach((data) => data.set("android:host", linkedin_deeplink_host));
    dataTags = null;

    dataTags = etreeManifest.findall('./application/activity/intent-filter/data[@android:path="OAUTH_DEEPLINK_PATH"]');
    dataTags.forEach((data) => data.set("android:path", linkedin_deeplink_path));

    let resultXmlManifest = etreeManifest.write();
    fs.writeFileSync(manifestPath, resultXmlManifest);
}

function copyFacebookPreferences(jsonConfig, projectRoot) {

    let facebook_client_appId = "";
    let facebook_client_token = "";

    try {
        jsonConfig.app_configurations.forEach( (configItem) =>{
            if ((configItem.provider_id == ProvidersEnum.facebook) && 
                (configItem.application_type_id == ApplicationTypeEnum.android)) {
                facebook_client_appId = configItem.client_id;
                facebook_client_token = configItem.client_token;
            }
        });
    } catch {
        throw new Error("OUTSYSTEMS_PLUGIN_ERROR: Missing configuration file or error trying to obtain the configuration.");
    }

    // create XML with correct values directly
    var stringsXmlPath = path.join(projectRoot, 'platforms/android/app/src/main/res/values/os_sociallogins_strings.xml');

    const xmlContent = `<?xml version='1.0' encoding='utf-8'?>
<resources>
    <string name="facebook_app_id">${facebook_client_appId || 'FACEBOOK_SDK_APP_ID'}</string>
    <string name="facebook_client_token">${facebook_client_token || 'FACEBOOK_SDK_CLIENT_TOKEN'}</string>
</resources>`;

    // write XML file directly
    fs.writeFileSync(stringsXmlPath, xmlContent);
};


