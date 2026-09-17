# Cordova OutSystems Firebase Core

###### Powered by  
![OutSystems][oslogo-image]

[oslogo-image]:https://www.outsystems.com/-/media/themes/outsystems/website/site-theme/imgs/logo.svg

Main dependency for all OutSystems Firebase plugins, that handles the Google Firebase configurations files for iOS and Android. 


## How to use it


#### Add the plugin dependency
The Firebase Core plugin must be added as a dependency in the plugin.xml as shown below:
```
<dependency id="cordova-outsystems-firebase-core" url="https://github.com/OutSystems/cordova-outsystems-firebase-core.git#1.0.0"/>
```
Please, always check if there is a newer tag version available, and update it accordingly.

#### Remove the resource files
Both iOS & Android resource files must be REMOVED from plugin.xml, because the Core plugin will import these files already. That being said, remove the lines below from your plugin.xml:
```
 <resource-file src="GoogleService-Info.plist" target="./GoogleService-Info.plist" />
 <resource-file src="google-services.json" target="./google-services.json" />
```

## Notes
The configuration files will be imported by the Firebase Core plugin's hook, and there will be no need to have multiple configuration files for different Firebase components anymore.
