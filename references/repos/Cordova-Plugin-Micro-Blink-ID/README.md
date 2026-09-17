# Cordova Blink ID Plugin

BlinkID SDK is a delightful component for quick and easy scanning of ID cards, passports and drivers licenses.
 The SDK is powered with [MicroBlink's](http://www.microblink.com) industry-proven and world leading OCR and barcode scanning technology, and offers:

- integrated camera management
- layered API, allowing everything from simple integration to complex UX customizations.
- lightweight and no internet connection required
- enteprise-level security standards
- data parsing from ID barcode standards

BlinkID is a part of family of SDKs developed by [MicroBlink](http://www.microblink.com) for optical text recognition, barcode scanning, ID document scanning and many others.

## Plugin

The purpose of this plugin is to leverage Hybrid Applications with BlinkID SDK features. With this plugin the users can read the Card ID within a Web Application.

As with all the cordova plugins, the plugin isn't available until the execution of `deviceready` event.

```javascript
document.addEventListener("deviceready", onDeviceReady, false);
function onDeviceReady() {
    // ...
}
```

## Supported Platforms

- iOS
- Android


## Installation
- Run the following command:

```shell
    cordova plugin add https://github.com/OutSystems/Cordova-Plugin-Micro-Blink-ID.git
``` 

# Blink ID

The `Blink ID Plugin` provides an easy way to read ID cards and get a JSON with the result in your WebApp.

## Methods

- __readCardId__: Method to read ID Cards, passports and drivers licenses.

- __scanDocuments__: ***** Comming Soon *****


## readCardId

__Parameters__:

- __licenseKey__: License generated on Micro Blink Dashboard. License key is locked to a bundle ID, a unique identifier of your app.
To learn more about bundle ID see the [official documentation](https://developer.apple.com/library/ios/documentation/IDEs/Conceptual/AppDistributionGuide/ConfiguringYourApp/ConfiguringYourApp.html).

- __successCallback__: A callback that is passed when the service is started with success. _(Function)_

- __errorCallback__: A callback that executes if an error occurs retrieving the `Blink ID Plugin` readCardId. _(Function)_

### Example

```javascript
var success = function (r) {
    console.log(r);
}

var fail = function (error) {
    console.log(error);
}
    
    
var licenseKey = "YOUR_LICENSE";
    
cordova.plugins.blinkId.readCardId(success, fail, licenseKey);

```
---

The success callback gets a JSON object with the following contents:
```javascript
{
  "dateOfBirth" : "",
  "nationality" : "",
  "documentCode" : "",
  "sex" : "",
  "dateOfExpiry" : "",
  "issuer" : "",
  "primaryId" : "",
  "opt2" : "",
  "mrzText" : "",
  "isParsed" : "",
  "secondaryId" : "",
  "opt1" : "",
  "documentNumber" : ""
}
 
```


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
