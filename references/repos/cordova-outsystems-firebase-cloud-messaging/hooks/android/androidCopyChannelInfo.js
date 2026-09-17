const path = require('path');
const fs = require('fs');
const { ConfigParser } = require('cordova-common');

module.exports = function (context) {
    // get notification channel name and description from config.xml file
    var projectRoot = context.opts.cordova.project ? context.opts.cordova.project.root : context.opts.projectRoot;
    var configXML = path.join(projectRoot, 'config.xml');
    var configParser = new ConfigParser(configXML);
    var channelName = configParser.getPlatformPreference("NotificationChannelDefaultName", "android");
    var channelDescription = configParser.getPlatformPreference("NotificationChannelDefaultDescription", "android");

    // create XML with correct values directly
    var stringsXmlPath = path.join(projectRoot, 'platforms/android/app/src/main/res/values/os_fcm_strings.xml');

    const xmlContent = `<?xml version='1.0' encoding='utf-8'?>
<resources>
    <string name="notification_channel_name">${channelName || 'Channel name'}</string>
    <string name="notification_channel_description">${channelDescription || 'Channel description'}</string>
</resources>`;

    // write XML file directly
    fs.writeFileSync(stringsXmlPath, xmlContent);
};
