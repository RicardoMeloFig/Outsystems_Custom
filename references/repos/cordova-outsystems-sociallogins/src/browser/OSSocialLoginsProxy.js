
function doAppleLogin (success, error, opts) {
    let clientId = "com.outsystems.mobile.plugin.sociallogin.apple";
    let redirectUri = "https://enmobile11-dev.outsystemsenterprise.com/SL_Core/rest/SocialLoginSignin/AuthRedirectOpenId";
    let responseType = "code id_token";
    let scope = "email name";
    let responseMode = "form_post";
    let state = "1333";

    let url = "https://appleid.apple.com/auth/authorize?" +
        "client_id=" + clientId +
        "&redirect_uri=" + redirectUri + 
        "&response_type=" + responseType + 
        "&scope=" + scope +
        "&response_mode=" + responseMode + 
        "&state=" + state;

    let popup = window.open(url);
    window.addEventListener("message", (event) => {
        console.log(event);
        popup.close();
    }, false);
}

module.exports = {
    coolMethod: doAppleLogin,
    cleanup: function () {}
};

require('cordova/exec/proxy').add('OSSocialLogins', module.exports);
    