// Based on: https://github.com/PrismJS/prism/issues/470

var gulp = require('gulp');
var fs = require('fs');
var _ = require("lodash");

var header = require('gulp-header');
var wrapper = require('gulp-wrapper');
var concat = require('gulp-concat');

var prism_outdir = './lib';

gulp.task("build-prism.css", function() {
  // lets try okaidia.css
  return gulp.src("themes/prism.css")
    // .pipe(rename("prism.css"))
    .pipe(gulp.dest(prism_outdir));
});

gulp.task("build-prism.js", function() {
  // make prism/components.js to be a module so we could require it
  var src = fs.readFileSync("./components.js", "utf8");
  src += "\nmodule.exports = components;\n";
  var out = "./prism-components.js";
  fs.writeFileSync(out, src, "utf8");
  var components = require(out);
  fs.unlinkSync(out);

  // replaces {id} tokens
  function replace(format, data) {
    return format.replace(/{(\w+)}/g, function(m, name) {
      return data[name] ? data[name] : "";
    });
  }

  // exclude languages that we won't use
  var excludedLanguages = [];
  var selectedLanguages = ["clike", "markup", "css", "javascript", "js", "csharp", "xml"];

  // TODO add plugins
  var glob = [components.core.meta.path]
  .concat(
    _.pairs(components.languages)
      .filter(function(p) {
        var k = p[0].toLowerCase();
        return k != "meta" && ((excludedLanguages.length == 0 && selectedLanguages.length == 0) || ( excludedLanguages.length > 0 && excludedLanguages.indexOf(k) < 0 ) || ( selectedLanguages.length > 0 && selectedLanguages.indexOf(k) >= 0 ));
      })
      .map(function(p) {
        return replace(components.languages.meta.path, {
          id: p[0]
        }) + ".js";
      })
  );

  var exportFooter = 
    "if (typeof define === 'function' && define.amd) {\n"
    + "\tdefine(function() { return prism({}, typeof window !== 'undefined' ? window : global); });\n"
    + "} else {\n"
    + "\tvar w = typeof window !== 'undefined' ? window : global;\n"
    + "\tprism(this || w, w);\n" // browser export
    + "}\n";

	console.log(glob.toString());
  return gulp.src(glob)
      .pipe(header('\n/* **********************************************\n' +
        '     Begin <%= file.relative %>\n' +
        '********************************************** */\n\n'))
      .pipe(concat('prism.js'))
      .pipe(wrapper({
        // wrap prism code into function
        header: "var prism = function (self, window) {\n",
        footer: "\n\nreturn Prism;\n};\n\n" + exportFooter
      }))
      .pipe(gulp.dest(prism_outdir))
});

gulp.task('default', gulp.parallel('build-prism.js', 'build-prism.css'));
