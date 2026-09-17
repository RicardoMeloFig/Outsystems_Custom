# Build Actions

This folder contains .json files for configuring build actions to use in a plugin on ODC with Capacitor. The purpose of these build actions is to provide the same functionality as cordova hooks, but on a Capacitor shell.

## Contents

The file allowHttpTraffic.json contains one build action:

- Android - Allows configuring the `cleartextTrafficPermitted` in the `network_security_config.xml`. This is used to block (`false`, which is the default) or allow (`true`) HTTP traffic in the Android app.

- iOS – Allows configuring `NSAllowsArbitraryLoadsInWebContent` in the app’s `Info.plist`. This controls whether HTTP traffic is blocked (`false`, default) or allowed (`true`) inside in-app WebViews.


## Outsystems' Usage

1. Copy the build action json file (which can contain multiple build actions inside) into the ODC Plugin, placing them in "Data" -> "Resources" and set "Deploy Action" to "Deploy to Target Directory", with target directory empty.
2. Update the Plugin's Extensibility configuration to use the build action.

```json
{
    "buildConfigurations": {
        "buildAction": {
            "config": $resources.buildActionFileName.json,
            "parameters": {
                // parameters go here; if there are no parameters then the block can be ommited
            }
        }
    }
}
```