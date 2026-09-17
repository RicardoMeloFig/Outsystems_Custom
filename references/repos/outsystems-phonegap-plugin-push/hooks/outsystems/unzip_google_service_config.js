module.exports = function (ctx) {
  var Q = ctx.requireCordovaModule("q");
  var fs = ctx.requireCordovaModule("fs");
  var path = ctx.requireCordovaModule("path");
  var CordovaError = ctx.requireCordovaModule("cordova-common").CordovaError;
  var deferral = Q.defer();

  var AdmZip = require("adm-zip");
  var defer = Q.defer();


  // The name of the target directory set on the Service Studio
  // This folder will be located inside those directories:
  // - Android path: platforms/android/assets/www
  // - iOS path: platforms/ios/www/

  /**
   * Returns the absolute path to the resources folder inside "www" folder.
   * Depends on the current running platform.
   * 
   * Example:
   *        - Android path: platforms/android/assets/www
   *        - iOS path: platforms/ios/www/
   * 
   * @param {String} context cordova plugin context
   */
  function getResourcesFolder(context) {
    var platform = context.opts.plugin.platform;
    var projectRoot = context.opts.projectRoot;
    var platformPath = path.join(projectRoot, "platforms", platform);
    var wwwfolder;
    if (platform === "android") {
      wwwfolder = "assets/www";
    } else if (platform === "ios") {
      wwwfolder = "www";
    }

    if (!wwwfolder) {
      return;
    }
    return path.join(platformPath, wwwfolder);
  }

  /**
   * 
   * Searches the resources folder for a zip file with the name equal 
   * to the FCMResourcesFile preference value and resturns an absolute path
   * if found.
   * 
   * @param {String} resourcesFolder - the absolute path to the expected resources folder
   * @param {String} prefZipFilename - the expected name of the zip file 
   *                                 as set on the FCMResourcesFile prefenrece
   * 
   */
  function getZipFile(resourcesFolder, prefZipFilename) {
    try {

      var dirFiles = fs.readdirSync(resourcesFolder);
      var zipFile;
      dirFiles.forEach(function (file) {
        if (file.match(/\.zip$/)) {
          var filename = path.basename(file, ".zip");
          if (filename === prefZipFilename)  {
            zipFile = path.join(resourcesFolder, file);
          }
        }
      });
      return zipFile;
    } catch (error) {
      return undefined;
    }
  }

  /**
   * Attempts to open and extract a zip file
   * @param {String} zipFile - absolute path to the zip file
   * @return {String} Target directory
   */
  function unzip(zipFile, resourcesFolder, prefZipFilename) {
    var zip = new AdmZip(zipFile);
    var targetDir = path.join(resourcesFolder, prefZipFilename);
    zip.extractAllTo(targetDir, true);
    return targetDir;
  }


  function _handleZipFile(context) {
    // Android path: platforms/android/assets/www
    // iOS path: platforms/ios/www/
    var projectRoot = ctx.opts.projectRoot;
    var platform = ctx.opts.plugin.platform;
    var platformPath = path.join(projectRoot, "platforms", platform);
    var wwwfolder;
    if (platform === "android") {
      wwwfolder = "assets/www";
    } else if (platform === "ios") {
      wwwfolder = "www";
    }

    if (!wwwfolder) {
      return;
    }
    var wwwpath = path.join(platformPath, wwwfolder);
    var configPath = path.join(wwwpath, "google-services");

    var prefZipFilename = "google-services";
    // unzip the resources file, if any
    var zipFile = getZipFile(configPath, prefZipFilename);

    // if zip file is present, lets unzip it!
    if (!zipFile)  {
      // For back compatibility, if the zip file isn't found we allow the rest of the hooks to run
      // in an attempt to gather the config files from the older way of doing (having them uploaded separately)
      console.log("Ignoring FCM config resources handling. Reason: Configurations zip file not found.");
      defer.resolve();
      return defer;
    }

    var resourcesFolder = getResourcesFolder(ctx);

    var unzipedResourcesDir = unzip(zipFile, resourcesFolder, prefZipFilename);

    deferral.resolve();
    return deferral.promise;
  }

  return _handleZipFile(ctx);

};
