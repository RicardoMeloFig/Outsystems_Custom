module.exports = function (context) {
    var deferral;
    var fs;
    var path;
    function isCordovaAbove (context, version) {
        var cordovaVersion = context.opts.cordova.version;
        console.log(cordovaVersion);
        var sp = cordovaVersion.split('.');
        return parseInt(sp[0]) >= version;
      }
    if(isCordovaAbove(context,8)){
        deferral = require("q").defer();
        fs  = require("fs");
        path  = require("path");
    }else{
        deferral = context.requireCordovaModule("q").defer();
        fs  = context.requireCordovaModule("fs");
        path  = context.requireCordovaModule("path");
    }

    var FILE_CONFIG = "dynatrace.config.js";
    var FOLDER_CONFIG = "dynatraceConfig";

    // OUTSYSTEMS: pick which dynatraceConfig folder to copy into the project root, which is
    // where the Dynatrace build hooks read dynatrace.config.js from.
    //
    // Cordova's own 'prepare' copies www/ into the platform assets folder, so the platform
    // candidate normally exists on every build. What differs is its content: when the app
    // declares a per-environment resource in its extensibility configuration, MABS overwrites
    // that platform copy during 'prepare' - before this before_prepare hook runs on the next
    // cycle - so the platform copy carries the environment-specific values. With no override
    // declared it is a byte-for-byte copy of www/dynatraceConfig, so preferring it is safe.
    //
    // www/dynatraceConfig stays last so behaviour is unchanged when no platform folder exists
    // (for example an iOS-only build, or before the first prepare).
    function resolveConfigSource (projectRoot) {
        var candidates = [
            path.join(projectRoot, "platforms", "android", FOLDER_CONFIG),
            path.join(projectRoot, "platforms", "ios", "www", FOLDER_CONFIG),
            path.join(projectRoot, "www", FOLDER_CONFIG)
        ];
        for(var i = 0; i < candidates.length; i++){
            if(fs.existsSync(path.join(candidates[i], FILE_CONFIG))){
                return { path: candidates[i], isPlatform: i < candidates.length - 1 };
            }
        }
        return undefined;
    }

    var configSource = resolveConfigSource(context.opts.projectRoot);
    if(configSource !== undefined){
        console.log("[Dynatrace][OutSystems] config source: " + configSource.path
            + " (" + (configSource.isPlatform ? "platform override" : "module www") + ")");
        copyFolderRecursiveSync(configSource.path, path.join(context.opts.projectRoot));
        deferral.resolve();
    }else{
        console.log("[Dynatrace][OutSystems] Failed to handle plugin resources: no " + FILE_CONFIG
            + " found under platforms/android assets or www/" + FOLDER_CONFIG);
        deferral.resolve();
    }

    function copyFileSync(source, target){

        var targetFile = target; 

        if(fs.existsSync(target)){
            if(fs.lstatSync(target).isDirectory()){
                targetFile = path.join(target,path.basename(source));
            }
        }

        fs.writeFileSync(targetFile,fs.readFileSync(source));
    }

    function copyFolderRecursiveSync(source, target){
        var files = [];

        var targetFolder = path.join(target);
        if(!fs.existsSync(targetFolder)){
            fs.mkdirSync(targetFolder);
        }

        if(fs.lstatSync(source).isDirectory()){
            files = fs.readdirSync(source);
            files.forEach((file)=>{
                var curSource = path.join(source,file);
                if(fs.lstatSync(curSource).isDirectory()){
                    copyFolderRecursiveSync(curSource,targetFolder);
                }else{
                    copyFileSync(curSource,targetFolder);
                }
            });
        }
    }
    return deferral.promise;
};