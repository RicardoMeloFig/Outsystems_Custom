## OutSystems' version of AirWatch SDK plugin

This plugin is a wrapper for the AirWatch SDK plugin, allowing OutSystems developers to take full advantage of the AirWatch features on their mobile apps.

The version of this plugin maps the version of the original AirWatch SDK plugin.

You can find more information about the AirWatch SDK plugin [here](https://www.npmjs.com/package/airwatch-sdk-plugin).

## Original AirWatch README information

airwatch-sdk-plugin
===================

This plugin provides Cordova access to features of the AirWatch mobile device management SDK for iOS and Android.

Installation
------------

Installation of the plugin is straightforward; simply type `cordova plugin add airwatch-sdk-plugin` at the command line.

Initialization
--------------

The plugin will auto-start on both Android and iOS devices, and will automatically start the AirWatch SDK. After startup, the functions will be available in the `plugins.airwatch` property of the `window` object. No other initialization is required to use the functions documented below.

To receive events from the SDK, an event listener must be initialized. See "Events" below.

Functions
---------
<dl>

### Functions available for Android and iOS

<dt><code>setSDKEventListener(listener)</code></dt>
<dd style="padding-bottom: 8pt">Set an event-handler function to receive events from the SDK. See "Events" below. This should be called once Cordova fires 'deviceready' event. The listener callback should have two parameters, event and info.</dd>

<dt><code>username(successCallback, errorCallback)</code></dt>
<dd style="padding-bottom: 8pt">Get the enrolled user's username. The username will be returned as a string parameter to the successCallback function.</dd>

<dt><code>groupId(successCallback, errorCallback)</code></dt>
<dd style="padding-bottom: 8pt">Get the enrolled user's group ID. The group ID will be returned as a string parameter to the successCallback function.</dd>

<dt><code>serverName(successCallback, errorCallback)</code></dt>
<dd style="padding-bottom: 8pt">Get the name of the server to which the device is enrolled. The server name will be returned as a string parameter to the successCallback function.</dd>

<dt><code>allowCopyPaste(successCallback, errorCallback)</code></dt>
<dd style="padding-bottom: 8pt">Gets the "allow copy/paste" setting for the profile: true if the user is allowed to copy and paste between managed apps, false otherwise. The value will be returned as a boolean parameter to the successCallback function.</dd>

<dt><code>customSettings(successCallback, errorCallback)</code></dt>
<dd style="padding-bottom: 8pt">Gets any custom settings provided in the app's profile. The value will be returned as a string parameter to the successCallback function.</dd>

### Functions available for iOS only

<dt><code>allowOffline(successCallback, errorCallback)</code></dt>
<dd style="padding-bottom: 8pt">Gets the "allow offline use" setting for the profile: true if the user is allowed to use managed apps when not connected to the network, false otherwise. The value will be returned as a boolean parameter to the successCallback function.</dd>

</dl>

Events
------
The AirWatch SDK sends event notifications to applications that use it when certain conditions arise. To receive these notifications in a Cordova app, the developer must first call `setSDKEventListener(listener)`, where `listener` is a function that accepts two parameters. The first parameter will be a string containing the name of the event, as listed below. The second will be an Object that will contain additional data if relevant to that type of event.

<dl>

### Events available for Android and iOS

<dt><code>initSuccess</code></dt>
<dd style="padding-bottom: 8pt">Sent when the AirWatch SDK is successfully initialized. All the functions (See "Functions" above) of the plugin, other than <code>setSDKEventListener(listener)</code>, will be available once this event is fired. </dd>

<dt><code>initFailure</code></dt>
<dd style="padding-bottom: 8pt">Sent when the AirWatch SDK cannot be successfully initialized. Any future calls to the plugin will fail.</dd>

### Events available for iOS only

<dt><code>wipe</code></dt>
<dd style="padding-bottom: 8pt">Sent when the device receives a "wipe" instruction from the console.</dd>

<dt><code>lock</code></dt>
<dd style="padding-bottom: 8pt">Sent when the device receives a "lock" instruction from the console.</dd>

<dt><code>unlock</code></dt>
<dd style="padding-bottom: 8pt">Sent when the device receives an "unlock" instruction from the console.</dd>

<dt><code>stopNetworkActivity</code></dt>
<dd style="padding-bottom: 8pt">Sent when the device receives a "stopNetworkActivity" instruction from the console. The event data parameter contains a property named <code>status</code> with a numeric value as specified in the table below.</dd>

<dt><code>resumeNetworkActivity</code></dt>
<dd style="padding-bottom: 8pt">Sent when the device receives a "resumeNetworkActivity" instruction from the console.</dd>

</dl>

### Network status values for `stopNetworkActivity`:

Status code | Network state
:----------:|:-------------
 -2         | initializing
 -1         | normal
  0         | bad SSID
  1         | cellular data disabled
  2         | roaming
  3         | proxy failed
  4         | network not reachable


