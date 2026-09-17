# Cordova Cache Coordinates Plugin

This plugin allows you to get coordinates and store them to return quickly one result when there is a request. The main goal with this plugin is for the situations that the device can lose the GPS signal and for this case return the last coordinates captured.

Although in the global scope, they are not available until after the `deviceready` event.

    document.addEventListener("deviceready", onDeviceReady, false);
    function onDeviceReady() {
        console.log(FileTransfer);
    }

## Installation

    cordova plugin add https://github.com/OutSystems/Cordova-Plugin-Cache-Coordinates.git

## Supported Platforms

- Android


# CacheCoordinates

The `CacheCoordinates` object provides a way to init the tracker to get the coordinates, get coordinates and stop the tracker.

## Methods

- __init__: Start tracker the coordinates.

- __getCoordinates__: Return the last coordinates.

- __stop__: Stop tracker to get Coordinates.


## init

__Parameters__:

- __timeBetweenUpdates__: The minimum time interval between location updates, in milliseconds.

- __metersBetweenUpdates__: The minimum distance between location updates in meters.

- __successCallback__: A callback that is passed a `CacheCoordinates` object. _(Function)_

- __errorCallback__: A callback that executes if an error occurs retrieving the `CacheCoordinates` object. _(Function)_

### Example
    var success = function (r) {
        console.log("Response = " + r);
    }

    var fail = function (error) {
        console.log("init error " + error);
    }
    
    var timeBetweenUpdates = 120000; // 2min
    var metersBetweenUpdates = 100;
    
    cordova.plugins.cacheCoordinates.init(success, fail, timeBetweenUpdates, metersBetweenUpdates);


## getCoordinates

__Parameters__:

- __successCallback__: A callback that is passed a `CacheCoordinates` object. _(Function)_

- __errorCallback__: A callback that executes if an error occurs retrieving the `CacheCoordinates` object. _(Function)_

### Example

    var success = function (r) {
        console.log("Response = " + r);
    }

    var fail = function (error) {
        console.log("coordinates error " + error);
    }
    
    cordova.plugins.cacheCoordinates.getCoordinates(success, fail);

    Sample Response: 
    "{
        timestamp: "2015-08-04 08:04:03.759", 
        longitude: "-19.5333", 
        latitude: "45.96670000000001"
    }"

## stop

__Parameters__:

- __successCallback__: A callback that is passed a `CacheCoordinates` object. _(Function)_

- __errorCallback__: A callback that executes if an error occurs retrieving the `CacheCoordinates` object. _(Function)_

### Example

    var success = function (r) {
        console.log("Response = " + r);
    }

    var fail = function (error) {
        console.log("init error " + error);
    }

    cordova.plugins.cacheCoordinates.stop(success, fail)
        

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