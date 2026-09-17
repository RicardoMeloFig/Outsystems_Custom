# Cordova Plugin for OutSystems App Feedback

## OutSystems App Feedback

OutSystems App Feedback allows customers to collect user feedback from their mobile applications. To submit feedback, users can draw on the screen, add voice notes, or type text directly on their tablets or smartphones. All user interactions and annotations are compiled into a comprehensive user story that developers can review through an intuitive interface, enabling them to navigate directly to the relevant screen and begin implementing changes.

## Supported Platforms

- iOS
- Android

## How to Deploy

Test your features in the `master` branch.

To make features available to OutSystems 11 customers, create or update a branch named `mabsX`, where `X` is the major MABS version (e.g., `mabs11` for MABS 11).

When apps that include App Feedback are built in MABS, the plugin is included automatically. The `mabsX` branch name determines which plugin commit MABS will use for the build.

## Update Mobile ECT

### Android

...

### iOS

To update the Mobile ECT framework used by the plugin, replace the `OutSystemsMobileECT.xcframework` file located in the `src/ios` folder with the new version of the framework. The framework can be obtained from the [outsystems-appfeedback-ios-sdk](https://github.com/OutSystems/outsystems-appfeedback-ios-sdk) repository. Follow the instructions in the repository README to build the framework.
