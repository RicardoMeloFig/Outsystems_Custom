module.exports = function (context) {
    var fs;
    var path;
    function isCordovaAbove(context, version) {
        var cordovaVersion = context.opts.cordova.version;
        var sp = cordovaVersion.split('.');
        return parseInt(sp[0]) >= version;
    }
    if (isCordovaAbove(context, 8)) {
        fs = require("fs");
        path = require("path");
    } else {
        fs = context.requireCordovaModule("fs");
        path = context.requireCordovaModule("path");
    }

    var FILE_SCRIPT = "dynatraceScript.js";
    var FOLDER_SCRIPT = "dynatraceJs";

    // OUTSYSTEMS: the JavaScript agent is loaded by the app at runtime, by web URL
    // ("dynatraceJs/dynatraceScript.js"), so it must physically sit inside the packaged
    // assets. Unlike dynatrace.config.js nothing in this plugin reads it, which means the
    // extensibility override has to land exactly where the WebView will look.
    //
    // This hook makes a short override target usable: MABS writes to
    // platforms/android/dynatraceJs/, we copy it into the assets folder Gradle packages.
    // It runs after_prepare, i.e. after Cordova's own www sync and after MABS has applied
    // the override, so the copy is deterministic within a single prepare pass.
    var projectRoot = context.opts.projectRoot;
    var source = path.join(projectRoot, "platforms", "android", FOLDER_SCRIPT, FILE_SCRIPT);
    var targetDir = path.join(projectRoot, "platforms", "android", "app", "src", "main",
        "assets", "www", FOLDER_SCRIPT);

    if (fs.existsSync(source)) {
        if (!fs.existsSync(targetDir)) {
            fs.mkdirSync(targetDir, { recursive: true });
        }
        fs.writeFileSync(path.join(targetDir, FILE_SCRIPT), fs.readFileSync(source));
        console.log("[Dynatrace][OutSystems] JS agent override applied: "
            + path.join(targetDir, FILE_SCRIPT));
    } else {
        console.log("[Dynatrace][OutSystems] No JS agent override at " + source
            + " - using the copy shipped with the module");
    }

    // OUTSYSTEMS: everything above is synchronous, so there is nothing to defer. Returning a
    // resolved built-in Promise keeps the hook contract Cordova expects without pulling in the
    // deprecated "q" module.
    return Promise.resolve();
};