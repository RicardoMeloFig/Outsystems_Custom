const et = require('elementtree');
const path = require('path');
const fs = require('fs');
const plist = require('plist');
const { ConfigParser } = require('cordova-common');

module.exports = function (context) {

    const ServiceEnum = Object.freeze({"ApplePay":"1", "GooglePay":"2"})
    let projectRoot = context.opts.cordova.project ? context.opts.cordova.project.root : context.opts.projectRoot;

    let merchant_id = "";
    let merchant_name = "";
    let merchant_country_code = "";
    let payment_allowed_networks = [];
    let payment_supported_capabilities = [];
    let payment_supported_card_countries = [];
    let shipping_supported_contacts = [];
    let billing_supported_contacts = [];
    let payment_gateway = "";
    let payment_request_url = "";
    let stripe_publishable_key = "";

    let appNamePath = path.join(projectRoot, 'config.xml');
    let appNameParser = new ConfigParser(appNamePath);
    let appName = appNameParser.name();
    let platformPath = path.join(projectRoot, "platforms/ios");
    // Cordova iOS 8 uses 'App' as the fixed project folder;
    //  earlier versions used the app name
    const isCordovaIos8OrHigher = fs.existsSync(path.join(platformPath, 'App'));
    if (isCordovaIos8OrHigher) {
        appName = 'App';
    }

    // first we look for the platforms/ios/<AppName>/Resources directory
    let resourcesPath = fs.existsSync(path.join(platformPath, appName, "Resources/json-config"))
        ? path.join(platformPath, appName, "Resources")
        : path.join(platformPath, "www")

    // if that directory doesn't exist then we look for platforms/ios/www/json-config
    if (!fs.existsSync(path.join(resourcesPath, "json-config"))) {
        // if that doesn't exist then we try to use platforms/ios/<AppName>/Resources/www
        resourcesPath = path.join(platformPath, appName, "Resources/www");
    }

    // if that doesn't exist then we try to use root/www directly
    if (!fs.existsSync(path.join(resourcesPath, "json-config"))) {
        resourcesPath = path.join(projectRoot, "www");
    }

    //read json config file
    let jsonConfig = "";
    let jsonParsed;
    try {
        jsonConfig = path.join(resourcesPath, 'json-config/PaymentsPluginConfiguration.json');
        let jsonConfigFile = fs.readFileSync(jsonConfig, 'utf8');
        jsonParsed = JSON.parse(jsonConfigFile);
    } catch {
        throw new Error("OUTSYSTEMS_PLUGIN_ERROR: Missing configuration file or error trying to obtain the configuration.");
    }
    
    jsonParsed.app_configurations.forEach((configItem) => {
        if (configItem.service_id == ServiceEnum.ApplePay) {
            let error_list = [];
            
            if (configItem.merchant_id != null && configItem.merchant_id !== "") {
                merchant_id = configItem.merchant_id;
            } else {
                error_list.push('Merchant Id');
            }

            if (configItem.merchant_name != null && configItem.merchant_name !== "") {
                merchant_name = configItem.merchant_name;
            } else {
                error_list.push('Merchant Name');
            }

            if (configItem.merchant_country_code != null && configItem.merchant_country_code !== "") {
                merchant_country_code = configItem.merchant_country_code;
            } else {
                error_list.push('Merchant Country');
            }

            if (configItem.payment_allowed_networks != null && configItem.payment_allowed_networks.length > 0) {
                payment_allowed_networks = configItem.payment_allowed_networks;
            } else {
                error_list.push('Payment Allowed Networks');
            }

            if (configItem.payment_supported_capabilities != null && configItem.payment_supported_capabilities.length > 0) {
                payment_supported_capabilities = configItem.payment_supported_capabilities;
            } else {
                error_list.push('Payment Supported Capabilities');
            }

            if (configItem.shipping_supported_contacts != null && configItem.shipping_supported_contacts.length > 0) {
                shipping_supported_contacts = configItem.shipping_supported_contacts;
            }
            if (configItem.billing_supported_contacts != null && configItem.billing_supported_contacts.length > 0) {
                billing_supported_contacts = configItem.billing_supported_contacts;
            }
            if (configItem.payment_supported_card_countries != null && configItem.payment_supported_card_countries.length > 0) {
                payment_supported_card_countries = configItem.payment_supported_card_countries;
            }

            if (configItem.tokenization != null) {
                if (configItem.tokenization.gateway != null && configItem.tokenization.gateway !== "") {
                    payment_gateway = configItem.tokenization.gateway;
                } else {
                    error_list.push('Payment Gateway Name');
                }

                if (configItem.tokenization.requestURL != null && configItem.tokenization.requestURL !== "") {
                    payment_request_url = configItem.tokenization.requestURL;
                } else {
                    error_list.push('Payment Request URL');
                }

                if (configItem.tokenization.stripePublishableKey != null && configItem.tokenization.stripePublishableKey !== "") {
                    stripe_publishable_key = configItem.tokenization.stripePublishableKey;
                } else if (payment_gateway.toLowerCase() === "stripe") {
                    error_list.push('Stripe\'s Publishable Key');
                }
            }                   
        
            if (error_list.length > 0) {
                console.error("Missing fields: " + error_list);
                throw new Error("OUTSYSTEMS_PLUGIN_ERROR: Payments configuration is missing some fields. Please check build logs to know more.");
            }
        }
    });
    

    //Change info.plist
    // Cordova always names the generated file '<name>-Info.plist' (capital I), on every
    //  version — the previous lowercase 'i' only ever worked by accident on case-insensitive
    //  filesystems. `appName` above is already resolved to 'App' (8+) or the real app name (<8).
    let infoPlistPath = path.join(platformPath, appName, appName + '-Info.plist');
    let infoPlistFile = fs.readFileSync(infoPlistPath, 'utf8');
    let infoPlist = plist.parse(infoPlistFile);

    infoPlist['ApplePayMerchantID'] = merchant_id;
    infoPlist['ApplePayMerchantName'] = merchant_name;
    infoPlist['ApplePayMerchantCountryCode'] = merchant_country_code;
    infoPlist['ApplePayPaymentAllowedNetworks'] = payment_allowed_networks;
    infoPlist['ApplePayPaymentSupportedCapabilities'] = payment_supported_capabilities;
    infoPlist['ApplePayPaymentSupportedCardCountries'] = payment_supported_card_countries;
    infoPlist['ApplePayShippingSupportedContacts'] = shipping_supported_contacts;
    infoPlist['ApplePayBillingSupportedContacts'] = billing_supported_contacts;
    if (payment_gateway !== "") {
        infoPlist['ApplePayPaymentGateway']['ApplePayPaymentGatewayName'] = payment_gateway;    

        if (payment_request_url !== "") {
            infoPlist['ApplePayPaymentGateway']['ApplePayRequestURL'] = payment_request_url;    
        }

        if (stripe_publishable_key !== "") {
            infoPlist['ApplePayPaymentGateway']['ApplePayStripePublishableKey'] = stripe_publishable_key;    
        }
    } else {
        delete infoPlist['ApplePayPaymentGateway'];
    }

    fs.writeFileSync(infoPlistPath, plist.build(infoPlist, { indent: '\t' }));

    // Change Entitlements files
    let debugEntitlementsPath = path.join(platformPath, appName + '/'+ 'Entitlements-Debug.plist');
    let debugEntitlementsFile = fs.readFileSync(debugEntitlementsPath, 'utf8');
    let debugEntitlements = plist.parse(debugEntitlementsFile);

    debugEntitlements['com.apple.developer.in-app-payments'] = [merchant_id];

    fs.writeFileSync(debugEntitlementsPath, plist.build(debugEntitlements, { indent: '\t' }));

    let releaseEntitlementsPath = path.join(platformPath, appName + '/' + 'Entitlements-Release.plist');
    let releaseEntitlementsFile = fs.readFileSync(releaseEntitlementsPath, 'utf8');
    let releaseEntitlements = plist.parse(releaseEntitlementsFile);

    releaseEntitlements['com.apple.developer.in-app-payments'] = [merchant_id];

    fs.writeFileSync(releaseEntitlementsPath, plist.build(releaseEntitlements, { indent: '\t' }));
};
