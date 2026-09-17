/**
 * copy_google_service_config.js
 * Plugin hook to deal specifically with the FCM license files (Google-Info.plist/google-service.json) (icons, sound) 
 * on the OutSystems platform. 
 *
 * @license MIT
 * @version 1.0
 * @author  João Gonçalves, joao.goncalves@outsystems.com
 * @updated 29/09/2017
 * @link    www.outsystems.com
 *
 */
module.exports = function (ctx) {
    var Q = ctx.requireCordovaModule("q");
    var fs = ctx.requireCordovaModule("fs");
    var path = ctx.requireCordovaModule("path");
    var CordovaError = ctx.requireCordovaModule("cordova-common").CordovaError;
    var deferral = Q.defer();
    
    // Android path: platforms/android/assets/www
    // iOS path: platforms/ios/www/
    var projectRoot = ctx.opts.projectRoot;
    var platform = ctx.opts.plugin.platform;
    var platformPath = path.join(projectRoot, "platforms", platform);
    var wwwfolder;
    if(platform === "android") {
        wwwfolder = "assets/www";
    } else if (platform === "ios") {
        wwwfolder = "www";
    }

    if(!wwwfolder) {
        return;
    }
    var wwwpath = path.join(platformPath, wwwfolder);
    var configPath = path.join(wwwpath, "google-services");

    fs.readdir(configPath, function(err, files){

        if(err) {
            deferral.reject(new CordovaError("An error occurred while trying to copy google service configuration file. " + err));
            return;
        }

        var fileExtension = platform === "android" ? ".json" : platform === "ios" ? ".plist" : undefined;
        var filename = files.find(function(val){
            return val.endsWith(fileExtension);
        });

        if(!filename) {
            deferral.reject(new CordovaError("The google service configuration file for " + platform + 
                " wasn't found in the google-services folder. Make sure to upload it on the resources."));
        }
        var originalFile = path.join(configPath, filename);
        var destinationFile = path.join(ctx.opts.plugin.dir, filename);

        fs.createReadStream(originalFile)
        .pipe(fs.createWriteStream(destinationFile))
        .on("error", function(err){
            deferral.reject(new CordovaError("Operation failed " + err));
        })
        .on("close", function(){
            deferral.resolve();
        });
    });
    return deferral.promise;
  };