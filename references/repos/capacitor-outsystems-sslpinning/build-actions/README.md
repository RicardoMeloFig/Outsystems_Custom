# Build Actions

This folder contains .json files for configuring build actions to use in a plugin on ODC with Capacitor. The purpose of these build actions is to provide the same functionality as cordova hooks, but on a Capacitor shell.

In the case of the SSL Pinning plugin, the values to add in the build actions are defined by the developer. Because of this, the build actions file should be configured throught the application's Extensibility Configurations file, instead of the plugin's. Still, this README provides an example of a build actions file that can be used.

## Contents

The file `pinning_build_action.json` contains two build actions:

- Android specific. Adds mulitple `<domain-config>` entries to the app's `network_security_config.xml` file, configuring the different hosts and hashes that should be allowed.

- iOS specific. Adds a `TSKConfiguration` entry in the app's Info.plist file with mulitple host and hash pairs, configuring the different hosts and hashes that should be allowed.

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