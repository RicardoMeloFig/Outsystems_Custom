# Estimote Cordova/PhoneGap plugin

### This Plugin is an extension from: [phonegap-estimotebeacons](https://github.com/evothings/phonegap-estimotebeacons)

---

Plugin forked from Evothings repository to extend its functionalities in order to leverage the following additional features:

* Region monitoring while application is closed
* Local notification if the application is closed and webview-wide DOM event when application is running.
* A region can be registered with associated notification data and deeplink URL.
* Android Estimote SDK updated.
* When the application is opened from a notification, the notification data can be delivered as a deeplink or in the WebView;
* Turning on the bluetooth will start monitoring for regions. Likewise, turing the bluetooth off will shut the moniroting off. (Android) 

---

This plugin makes it easy to develop Cordova apps for Estimote Beacons and Estimote Stickers. Use JavaScript and HTML to develop stunning apps that take advantage of the capabilities of Estimote Beacons and Stickers.

![Estimote Beacons](http://evomedia.evothings.com/2014/09/estimote-beacons-group-small.jpg)

## Updated API

The JavaScript API has been updated. Please note that the new API is not backwards compatible. The original API is available in the branch "0.1.0".

As of version 0.6.0 the API consists of two modules, "estimote.beacons" and "estimote.nearables", with support for Estimote Beacons and Estimote Stickers. "EstimoteBeacons" is kept for backwards compatibility, and points to "estimote.beacons".

A change log is found in file [changelog.md](changelog.md).

## Documentation

The file [documentation.md](documentation.md) contains an overview of the plugin API.

Documentation of all functions is available in the JavaScript API implementation file [EstimoteBeacons.js](plugin/src/js/EstimoteBeacons.js).

---

## Contributors
- OutSystems - Mobility Experts
    - João Gonçalves, <joao.goncalves@outsystems.com>
    - Rúben Gonçalves, <ruben.goncalves@outsystems.com>
    - Vitor Oliveira, <vitor.oliveira@outsystems.com>
    - Danilo Costa, <danilo.costa@outsystems.com>

#### Document author
- João Gonçalves, <joao.goncalves@outsystems.com>

###Copyright OutSystems, 2016

Original Creator: [Evothings AB.](https://github.com/evothings)

---

## Credits

Many thanks goes to [Konrad Dzwinel](https://github.com/kdzwinel) who developed the original version of this plugin and provided valuable support and advice for the redesign of the plugin.

Many thanks also to all contributors! https://github.com/evothings/phonegap-estimotebeacons/pulls?q=is%3Apr+is%3Aclosed
