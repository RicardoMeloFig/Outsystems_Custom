var spawn = require('child_process').spawn;
var LFT = require('leanft');
var Keyboard = LFT.Keyboard;
var Desktop = LFT.Desktop;
var StdWin = LFT.StdWin;
var whenDone = LFT.whenDone;

var EyesLeanFT = require('../index');
var Eyes = EyesLeanFT.Eyes;
var ConsoleLogHandler = EyesLeanFT.ConsoleLogHandler;

describe("Eyes.LeanFT.JavaScript - notepad", function () {

    this.timeout(5 * 60 * 1000);

    var notepadWindow = null, eyes = null;

    before(function (done) {
        LFT.init();

        eyes = new Eyes();
        eyes.setApiKey(process.env.APPLITOOLS_API_KEY);
        eyes.setLogHandler(new ConsoleLogHandler(true));

        whenDone(done);
    });

    beforeEach(function (done) {
        LFT.beforeTest();
        whenDone(done);
    });

    // This example shows usage of the low level keyboard and mouse operations
    it("LeanFT Notepad Test", function (done) {
        // Launch the Notepad application.
        spawn('C:/Windows/System32/notepad.exe');

        // Locate the Notepad window and assign it to a Window object.
        notepadWindow = Desktop.$(StdWin.Window({
            windowClassRegExp: "Notepad",
            windowTitleRegExp: " Notepad"
        }));

        var appName = this.test.parent.title;
        var testName = this.test.title;
        eyes.open(notepadWindow, appName, testName);

        // Move the notepad window to the top left area of the screen
        notepadWindow.move(0, 0);

        eyes.checkWindow("Before");

        Keyboard.sendString("Applitools LeanFT Test");

        Keyboard.pressKey(Keyboard.Keys.enter);

        eyes.checkWindow("After");

        eyes.close();

        whenDone(done);
    });

    afterEach(function (done) {
        LFT.afterTest();
        eyes.abortIfNotClosed();
        whenDone(done);
    });

    after(function (done) {
        LFT.cleanup();
        whenDone(done);
    });
});
