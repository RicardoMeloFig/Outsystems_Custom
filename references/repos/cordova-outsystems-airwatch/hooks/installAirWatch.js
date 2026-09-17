module.exports = function(ctx) {
    // make sure android platform is part of build
    var fs = ctx.requireCordovaModule('fs'),
    path = ctx.requireCordovaModule('path'),
    child_process = ctx.requireCordovaModule('child_process');

    console.log("Installing Air Watch plugin...");
    var projectFolder = path.join(ctx.opts.projectRoot);

    var spawnSync = child_process.spawnSync;
    var pluginInstall = spawnSync('cordova', [ 'plugin','add', 'airwatch-sdk-plugin@1.0.7' ], {
        cwd: projectFolder   });

    if(pluginInstall.stdout){
        console.log(pluginInstall.stdout.toString('utf8'));
    }

    if(pluginInstall.stderr){
        console.error(pluginInstall.stderr.toString('utf8'));        
    }

    if(pluginInstall.error || pluginInstall.signal){
        throw "Failed to install Air Watch plugin."
    }

};