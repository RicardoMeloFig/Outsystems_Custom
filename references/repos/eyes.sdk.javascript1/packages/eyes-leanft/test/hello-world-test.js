var LFT = require('leanft');
var Web = LFT.Web;
var whenDone = LFT.whenDone;

var EyesLeanFT = require('../index');
var Eyes = EyesLeanFT.Eyes;
var ConsoleLogHandler = EyesLeanFT.ConsoleLogHandler;

describe("Eyes.LeanFT.JavaScrip - hello world", function () {

    this.timeout(5 * 60 * 1000);

    var browser = null, eyes = null;

    before(function (done) {
        LFT.init();

        eyes = new Eyes();
        eyes.setApiKey(process.env.APPLITOOLS_API_KEY);
        eyes.setLogHandler(new ConsoleLogHandler(true));
        eyes.setForceFullPageScreenshot(true);

        whenDone(done);
    });

    beforeEach(function (done) {
        LFT.beforeTest();
        var appName = this.test.parent.title;
        var testName = this.currentTest.title;

        Web.Browser.launch(Web.BrowserType.Chrome).then(function(launched_browser){
            return eyes.open(launched_browser, appName, testName);
        }).then(function (launched_browser) {
            browser = launched_browser;
        });

        whenDone(done);
    });

    it("LeanFT Hello World", function (done) {
        browser.navigate("https://www.applitools.com/helloworld");
        browser.sync();

        eyes.checkWindow("Hello!");

        var button = browser.$(Web.Element({tagName: "button"}));
        button.click();

        eyes.checkWindow("Click!");

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
