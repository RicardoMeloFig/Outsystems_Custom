exports.logOut = function (success, error) {
    var nav = WinJS.Navigation;
    nav.back();
};

require('cordova/exec/proxy').add('LogOut', exports);