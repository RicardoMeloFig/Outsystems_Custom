module.exports = function(ctx) {
    // make sure android platform is part of build
    if (ctx.opts.plugin.platform.indexOf('android') < 0) {
        return;
    }

    // make sure android platform is part of build
    var fs = ctx.requireCordovaModule('fs'),
    path = ctx.requireCordovaModule('path');

    var libsFolder = path.join(ctx.opts.projectRoot,'platforms','android','libs');

    var gsonJarFile = path.join(libsFolder,'gson-2.6.2.jar');
    fs.unlinkSync(gsonJarFile)

};