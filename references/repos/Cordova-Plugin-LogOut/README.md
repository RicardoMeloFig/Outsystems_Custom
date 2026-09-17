# Cordova LogOut Plugin


This plugins is to be integrated with OutSystems Now and allows users to log out on the application and back to Native Login Screen. It is useful when the applications doesn't has the Toolbar on the WebView. 
This plugin allows too, only remove the session cookies of the WebView and reload the page.

Although in the global scope, they are not available until after the `deviceready` event.

    document.addEventListener("deviceready", onDeviceReady, false);
    function onDeviceReady() {
        console.log(FileTransfer);
    }

## Installation

    cordova plugin add https://github.com/OutSystems/Cordova-Plugin-LogOut.git

## Supported Platforms

- Android
- iOS
- Windows


# LogOut

The `LogOut` object provides a way to do logout on the app without need to use the Toolbar on OutSystems Now.

## Methods

- __logOut__: Close the WebApp and go back to Login Screen.

- __logOutWebView__: Clear Session Cookies and redirect the WebApp to the Login Page.


## logOut

__Parameters__:

- __successCallback__: A callback that is passed a `LogOut` object. _(Function)_

- __errorCallback__: A callback that executes if an error occurs retrieving the `LogOut` object. _(Function)_

### Example
    var success = function (r) {
        console.log("Response = " + r);
    }

    var fail = function (error) {
        console.log("Error " + error);
    }
    
    cordova.plugins.logOut.logOut(success, fail);


## logOutWebView

__Parameters__:

- __successCallback__: A callback that is passed a `LogOut` object. _(Function)_

- __errorCallback__: A callback that executes if an error occurs retrieving the `LogOut` object. _(Function)_

### Example

    var success = function (r) {
        console.log("Response = " + r);
    }

    var fail = function (error) {
        console.log("Error " + error);
    }
    
    cordova.plugins.logOut.logOutWebView(success, fail);
    
---
#### Contributors
- OutSystems - Mobility Experts
    - João Gonçalves, <joao.goncalves@outsystems.com>
    - Rúben Gonçalves, <ruben.goncalves@outsystems.com>
    - Vitor Oliveira, <vitor.oliveira@outsystems.com>

#### Document author
- Vitor Oliveira, <vitor.oliveira@outsystems.com>

###Copyright OutSystems, 2015

---

LICENSE
=======


[The MIT License (MIT)](http://www.opensource.org/licenses/mit-license.html)

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.