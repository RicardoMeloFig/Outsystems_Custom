const path = require("path");
const fs = require("fs");
const ServiceEnum = Object.freeze({ ApplePay: "1", GooglePay: "2" });

const projectRoot = process.env.CAPACITOR_ROOT_DIR;
const platform = process.env.CAPACITOR_PLATFORM_NAME;

if (!platform || !projectRoot) {
    throw new Error("OUTSYSTEMS_PLUGIN_ERROR: Missing required environment variables.");
}

function getAppDir() {
    const dirs = {
        ios: path.join(projectRoot, "ios"),
        android: path.join(projectRoot, "android")
    };
    return dirs[platform];
}

function getPaymentConfig() {
    try {
        const configDir = {
            ios: path.join(getAppDir(), "App", "App", "json-config"),
            android: path.join(getAppDir(), "json-config")
        };
        const filePath = path.join(configDir[platform], "PaymentsPluginConfiguration.json");
        if (!fs.existsSync(filePath)) {
            throw new Error(`OUTSYSTEMS_PLUGIN_ERROR: ${filePath} file not found.`);
        }
        return JSON.parse(fs.readFileSync(filePath, "utf8"));
    } catch (err) {
        throw new Error("OUTSYSTEMS_PLUGIN_ERROR: Unable to read or parse payment configuration.");
    }
  }

function validateAndAssignField(parentObject, fieldKey, errorList, fieldName) {
    if (parentObject && parentObject[fieldKey] && parentObject[fieldKey].length > 0) {
        return parentObject[fieldKey];
    } else {
        errorList.push(fieldName);
        return null;
    }
}

function configureAndroid(paymentConfig) {
    let hasGooglePay = false;

    let merchant_name = "";
    let merchant_country_code = "";
    let payment_allowed_networks = [];
    let payment_supported_capabilities = [];
    let payment_supported_card_countries = [];
    let shipping_supported_contacts = [];
    let shipping_country_codes = [];
    let billing_supported_contacts = [];
    let gateway = "";
    let backend_url = "";
    //only for PSPs other than Stripe
    let gateway_merchant_id = "";
    //only for stripe
    let stripe_version = "";
    let stripe_pub_key = "";

    paymentConfig.app_configurations.forEach((configItem) => {
        if (configItem.service_id == ServiceEnum.GooglePay) {
            hasGooglePay = true;
            let error_list = [];

            merchant_name = validateAndAssignField(configItem, "merchant_name", error_list, "Merchant Name");
            merchant_country_code = validateAndAssignField(configItem, "merchant_country_code", error_list, "Merchant Country");
            payment_allowed_networks = validateAndAssignField(configItem, "payment_allowed_networks", error_list, "Payment Allowed Networks");
            payment_supported_capabilities = validateAndAssignField(configItem, "payment_supported_capabilities", error_list, "Payment Supported Capabilities");
            if(configItem.payment_supported_card_countries && configItem.payment_supported_card_countries.length > 0){
                payment_supported_card_countries = configItem.payment_supported_card_countries;
            }
            if(configItem.shipping_supported_contacts && configItem.shipping_supported_contacts.length > 0){
                shipping_supported_contacts = configItem.shipping_supported_contacts;
            }
            if(configItem.shipping_country_codes && configItem.shipping_country_codes.length > 0){
                shipping_country_codes = configItem.shipping_country_codes;
            }
            if(configItem.billing_supported_contacts && configItem.billing_supported_contacts.length > 0){
                billing_supported_contacts = configItem.billing_supported_contacts;
            }

            if (configItem.tokenization) {
                gateway = configItem.tokenization.gateway;
                backend_url = configItem.tokenization.requestURL;
                if (gateway.toUpperCase() == "STRIPE") {
                    stripe_version = configItem.tokenization.stripeVersion;
                    stripe_pub_key = configItem.tokenization.stripePublishableKey;
                } else {
                    gateway_merchant_id = configItem.tokenization.gatewayMerchantId;
                }
            } else {
                error_list.push("PSP information");
            }

            if (error_list.length > 0) {
                console.error("Missing fields: " + error_list);
                throw new Error("OUTSYSTEMS_PLUGIN_ERROR: Payments configuration is missing some fields. Please check build logs to know more.");
            }
        }
    });

    if (hasGooglePay) {
        const et = require("elementtree");
        const stringsXmlPath = path.join(getAppDir(), "app", "src", "main", "res", "values", "strings.xml");
        if (!fs.existsSync(stringsXmlPath)) {
            throw new Error(`OUTSYSTEMS_PLUGIN_ERROR: ${stringsXmlPath} file not found.`);
        }
        const stringsXmlContents = fs.readFileSync(stringsXmlPath).toString();
        const etreeStrings = et.parse(stringsXmlContents);

        const updateTag = (tagName, value) => {
            const tags = etreeStrings.findall(`./string[@name="${tagName}"]`);
            if (tags.length > 0) {
                tags.forEach((tag) => (tag.text = value));
            } else {
                const newTag = et.Element("string");
                newTag.set("name", tagName);
                newTag.text = value;
                etreeStrings.getroot().append(newTag);
            }
        };

        updateTag("merchant_name", merchant_name);
        updateTag("merchant_country_code", merchant_country_code);
        updateTag("payment_allowed_networks", payment_allowed_networks);
        updateTag("payment_supported_capabilities", payment_supported_capabilities);
        updateTag("payment_supported_card_countries", payment_supported_card_countries);
        updateTag("shipping_supported_contacts", shipping_supported_contacts);
        updateTag("shipping_country_codes", shipping_country_codes);
        updateTag("billing_supported_contacts", billing_supported_contacts);
        updateTag("gateway", gateway);
        updateTag("backend_url", backend_url);
        updateTag("gateway_merchant_id", gateway_merchant_id);
        updateTag("stripe_version", stripe_version);
        updateTag("stripe_pub_key", stripe_pub_key);

        fs.writeFileSync(stringsXmlPath, etreeStrings.write({ xml_declaration: true, indent: 4 }));

        const manifestPath = path.join(getAppDir(), "app", "src", "main", "AndroidManifest.xml");
        if (!fs.existsSync(manifestPath)) {
            throw new Error(`OUTSYSTEMS_PLUGIN_ERROR: ${manifestPath} file not found.`);
        }
        const manifestData = fs.readFileSync(manifestPath, "utf8");
        const etreeManifest = et.parse(manifestData);
        const root = etreeManifest.getroot();
        const application = root.find("application");
        let metaData = application.findall("meta-data");
        const existing = metaData.find(el => el.get("android:name") === "com.google.android.gms.wallet.api.enabled");
        if (existing) {
            existing.set("android:value", "true");
        } else {
            const newMeta = new et.Element("meta-data");
            newMeta.set("android:name", "com.google.android.gms.wallet.api.enabled");
            newMeta.set("android:value", "true");
            application.append(newMeta);
        }
        
        fs.writeFileSync(manifestPath, etreeManifest.write({ xml_declaration: true, indent: 4 }));
    }
}

function configureIOS(paymentConfig) {
    const plist = require("plist");
    const xcode = require('xcode');

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

    paymentConfig.app_configurations.forEach((configItem) => {
        if (configItem.service_id == ServiceEnum.ApplePay) {
            let error_list = [];

            merchant_id = validateAndAssignField(configItem, "merchant_id", error_list, "Merchant Id");
            merchant_name = validateAndAssignField(configItem, "merchant_name", error_list, "Merchant Name");
            merchant_country_code = validateAndAssignField(configItem, "merchant_country_code", error_list, "Merchant Country");
            payment_allowed_networks = validateAndAssignField(configItem, "payment_allowed_networks", error_list, "Payment Allowed Networks");
            payment_supported_capabilities = validateAndAssignField(configItem, "payment_supported_capabilities", error_list, "Payment Supported Capabilities");
            if (configItem.shipping_supported_contacts != null && configItem.shipping_supported_contacts.length > 0) {
                shipping_supported_contacts = configItem.shipping_supported_contacts;
            }
            if (configItem.billing_supported_contacts != null && configItem.billing_supported_contacts.length > 0) {
                billing_supported_contacts = configItem.billing_supported_contacts;
            }
            if (configItem.payment_supported_card_countries != null && configItem.payment_supported_card_countries.length > 0) {
                payment_supported_card_countries = configItem.payment_supported_card_countries;
            }

            if (configItem.tokenization) {
                payment_gateway = validateAndAssignField(configItem.tokenization, "gateway", error_list, "Payment Gateway Name");
                payment_request_url = validateAndAssignField(configItem.tokenization, "requestURL", error_list, "Payment Request URL");
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

    let infoPlistPath = path.join(getAppDir(), "App", "App", "Info.plist");
    if (!fs.existsSync(infoPlistPath)) {
        throw new Error(`OUTSYSTEMS_PLUGIN_ERROR: ${infoPlistPath} file not found.`);
    }
    let infoPlistFile = fs.readFileSync(infoPlistPath, "utf8");
    let infoPlist = plist.parse(infoPlistFile);

    infoPlist["ApplePayMerchantID"] = merchant_id;
    infoPlist["ApplePayMerchantName"] = merchant_name;
    infoPlist["ApplePayMerchantCountryCode"] = merchant_country_code;
    infoPlist["ApplePayPaymentAllowedNetworks"] = payment_allowed_networks;
    infoPlist["ApplePayPaymentSupportedCapabilities"] = payment_supported_capabilities;
    infoPlist["ApplePayPaymentSupportedCardCountries"] = payment_supported_card_countries;
    infoPlist["ApplePayShippingSupportedContacts"] = shipping_supported_contacts;
    infoPlist["ApplePayBillingSupportedContacts"] = billing_supported_contacts;
    
    if (payment_gateway) {
        infoPlist["ApplePayPaymentGateway"] = {
            ApplePayPaymentGatewayName: payment_gateway,
            ...(payment_request_url && { ApplePayRequestURL: payment_request_url }),
            ...(stripe_publishable_key && { ApplePayStripePublishableKey: stripe_publishable_key }),
        };
    } else {
        delete infoPlist["ApplePayPaymentGateway"];
    }

    fs.writeFileSync(infoPlistPath, plist.build(infoPlist, { indent: "\t" }));

    const pbxprojPath = path.join(getAppDir(), "App", "App.xcodeproj", "project.pbxproj");
    if (!fs.existsSync(pbxprojPath)) {
        throw new Error(`OUTSYSTEMS_PLUGIN_ERROR: ${pbxprojPath} file not found.`);
    }
    const entitlementsFileName = 'App.entitlements';
    const entitlementsPath = path.join(getAppDir(), "App", "App", entitlementsFileName);
    let entitlements = {};
    if (fs.existsSync(entitlementsPath)) {
        entitlements = plist.parse(fs.readFileSync(entitlementsPath, 'utf8'));
    }
    entitlements['com.apple.developer.in-app-payments'] = [merchant_id];
    fs.writeFileSync(entitlementsPath, plist.build(entitlements, { indent: "\t" }));

    const project = xcode.project(pbxprojPath);
    project.parseSync();
    const target = project.getFirstTarget().uuid;
    project.addBuildProperty('CODE_SIGN_ENTITLEMENTS', `App/${entitlementsFileName}`, 'Release', target);
    project.addBuildProperty('CODE_SIGN_ENTITLEMENTS', `App/${entitlementsFileName}`, 'Debug', target);
    fs.writeFileSync(pbxprojPath, project.writeSync());
}

if (platform === "ios") {
    configureIOS(getPaymentConfig());
} else if (platform === "android") {
    configureAndroid(getPaymentConfig());
}
