var LFT = require('leanft');
var Web = LFT.Web;
var whenDone = LFT.whenDone;

var EyesLeanFT = require('../index');
var Eyes = EyesLeanFT.Eyes;
var ConsoleLogHandler = EyesLeanFT.ConsoleLogHandler;
var Target = EyesLeanFT.Target;
var MatchLevel = EyesLeanFT.MatchLevel;
var StitchMode = EyesLeanFT.StitchMode;

describe("Eyes.LeanFT.JavaScript - check-interface", function () {

    this.timeout(5 * 60 * 1000);

    var browser = null, eyes = null;

    before(function (done) {
        LFT.init();

        eyes = new Eyes();
        eyes.setApiKey(process.env.APPLITOOLS_API_KEY);
        eyes.setLogHandler(new ConsoleLogHandler(true));
        eyes.setStitchMode(StitchMode.CSS);
        eyes.setForceFullPageScreenshot(true);

        whenDone(done);
    });

    beforeEach(function (done) {
        LFT.beforeTest();
        var appName = this.test.parent.title;
        var testName = this.currentTest.title;

        Web.Browser.launch(Web.BrowserType.Chrome).then(function(launched_browser){
            return eyes.open(launched_browser, appName, testName, {width: 1000, height: 700});
        }).then(function (launched_browser) {
            browser = launched_browser;
        });

        whenDone(done);
    });

    it("LeanFT check-interface test on Web", function (done) {
        browser.navigate("https://astappev.github.io/test-html-pages/");
        browser.sync();

        // Entire window, equivalent to eyes.checkWindow()
        eyes.check("Entire window", Target.window()
                .matchLevel(MatchLevel.Layout)
                .ignore(Web.Element({id: "overflowing-div" }))
                .ignore({element: browser.$(Web.Element({name: "frame1"}))})
                .ignore({left: 400, top: 100, width: 50, height: 50}, {left: 400, top: 200, width: 50, height: 100})
                .floating({left: 500, top: 100, width: 75, height: 100, maxLeftOffset: 25, maxRightOffset: 10, maxUpOffset: 30, maxDownOffset: 15})
                .floating({element: Web.Element({id: "overflowing-div-image"}), maxLeftOffset: 5, maxRightOffset: 25, maxUpOffset: 10, maxDownOffset: 25})
            // .floating({element: browser.$(By.tagName("h1")), maxLeftOffset: 10, maxRightOffset: 10, maxUpOffset: 10, maxDownOffset: 10})
        );

        // Region by rect, equivalent to eyes.checkFrame()
        eyes.check("Region by rect", Target.region({left: 50, top: 50, width: 200, height: 200})
            // .floating({left: 50, top: 50, width: 60, height: 50, maxLeftOffset: 10, maxRightOffset: 10, maxUpOffset: 10, maxDownOffset: 10})
            // .floating({left: 150, top: 75, width: 60, height: 50, maxLeftOffset: 10, maxRightOffset: 10, maxUpOffset: 10, maxDownOffset: 10})
        );

        // Region by element, equivalent to eyes.checkRegionByElement()
        eyes.check("Region by element", Target.region(browser.$(Web.Element({tagName: "h1"}))));

        // Region by locator, equivalent to eyes.checkRegionBy()
        eyes.check("Region by locator", Target.region(Web.Element({id: "overflowing-div-image"})));

        // Entire element by element, equivalent to eyes.checkElement()
        eyes.check("Entire element by element", Target.region(browser.$(Web.Element({id: "overflowing-div-image"}))).fully());

        // Entire element by locator, equivalent to eyes.checkElementBy()
        eyes.check("Entire element by locator", Target.region(Web.Element({id: "overflowing-div"})).fully().matchLevel(MatchLevel.Exact));

        eyes.close();

        whenDone(done);
    });

    afterEach(function (done) {
        LFT.afterTest();
        if (browser){
            browser.close();
        }
        eyes.abortIfNotClosed();
        whenDone(done);
    });

    after(function (done) {
        LFT.cleanup();
        whenDone(done);
    });
});
