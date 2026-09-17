# Contributing to Dynatrace Cordova Plugin

This plugin is a wrapper of the Dynatrace Cordova plugin for OutSystems. It wraps the [@dynatrace/cordova-plugin](https://www.npmjs.com/package/@dynatrace/cordova-plugin) NPM package and adds OutSystems-specific customizations.

## Development Setup

### Prerequisites

- Node.js >= 18 (see `engines` in package.json)
- Cordova CLI: `npm install -g cordova`
- Git
- For Android development: Android SDK
- For iOS development: Xcode and CocoaPods

### Installation

1. Clone the repository:
```bash
git clone https://github.com/OutSystems/dynatrace-cordova-plugin.git
cd dynatrace-cordova-plugin
```

2. Install dependencies:
```bash
npm install
```

This will automatically run the `install` hook which executes `scripts/InstallCap.js` and sets up Husky for git hooks.

## Development Workflow

### Branch Naming

Branch names follow a descriptive pattern based on the target MABS (Mobile Apps Build Service) version or feature:

- MABS version updates: `MABS[version]` (e.g., `MABS11`, `MABS10`, `MABS9`)
- Feature branches: descriptive names (e.g., `configFile`, `importDynatrace`)

Check existing branches for reference:
```bash
git branch -r
```

### Commit Messages

Commits follow a simple descriptive format. Examples from the repository:

- Feature additions: `Add hook to copy configuration file`
- Updates: `Update dynatrace plugin code (#9)`, `MABS9 Update (#7)`
- Fixes: `Fix: replace @dynatrace mentions with plugin name`
- Upgrades: `Upgraded to last Dynatrace Version and MABS 10`

Reference recent commits for style:
```bash
git log --oneline -20
```

### Making Changes

1. Create a feature branch from `main`
2. Make your changes
3. Run linting and formatting:
```bash
npm run lint
npm run prettier:check
```
4. Fix any issues:
```bash
npm run prettier:write
```
5. Commit your changes
6. Push and create a pull request

## Building and Testing

### Code Quality

| Command | Description |
|---------|-------------|
| `npm run lint` | Lint TypeScript source files with ESLint |
| `npm run lint:spec` | Lint test files |
| `npm run prettier:check` | Check code formatting |
| `npm run prettier:write` | Auto-format code with Prettier |

### TypeScript Compilation

| Command | Description |
|---------|-------------|
| `npm run tsc` | Compile TypeScript (development) |
| `npm run tsc:prod` | Compile TypeScript for release |

### Testing

| Command | Description |
|---------|-------------|
| `npm test` | Run Jest tests |
| `npm run test:debug` | Run tests with Node inspector |

### Plugin Utilities

| Command | Description |
|---------|-------------|
| `npx doctorDynatrace` | Diagnostic tool for plugin configuration |

## Code Standards

### Linting

The project uses ESLint with TypeScript support. Configuration is embedded in package.json with these plugins:

- `@typescript-eslint/eslint-plugin`
- `eslint-plugin-import`
- `eslint-plugin-jsdoc`
- `eslint-plugin-prefer-arrow`
- `eslint-plugin-unicorn`
- `eslint-config-prettier` (to avoid conflicts with Prettier)

Linting is enforced via lint-staged on commit.

### Formatting

Prettier is configured for consistent code formatting. All TypeScript files are automatically formatted on commit via Husky and lint-staged.

### Git Hooks

Pre-commit hooks (via Husky) automatically run:
- ESLint with auto-fix on `*.ts` files
- Prettier on all files

This is configured in `package.json` under `lint-staged`.

## Pull Request Process

1. Ensure all linting and formatting checks pass
2. Verify TypeScript compilation succeeds
3. Update the README.md if you've made changes that affect:
   - Plugin version
   - Agent versions (Android/iOS)
   - MABS version
   - Installation or usage instructions

4. Create a pull request to `main` branch
5. PR titles should be descriptive and reference any issue numbers

### After Merge

When changes are merged to `main`:

1. Update the version number in:
   - `package.json`
   - `plugin.xml`
2. Create a git tag following the versioning pattern (e.g., `v11.0`)
3. Create a GitHub release

Check existing tags for versioning reference:
```bash
git tag
```

## Updating the Plugin Wrapper

This section details the complete procedure for updating this wrapper with a new version of the upstream Dynatrace plugin.

### Install the NPM and prepare folders
1. Create a new branch of the following repository and clone it into your PC: https://github.com/OutSystems/dynatrace-cordova-plugin
2. Create a new temp folder in another location
3. Run the following command inside your temp folder:
```cordova create cordovaapp dynatraceRUM```
4. Enter the newly created folder called cordovaapp, move all its content to the temp folder you created before.
5. In your temp folder run this command:
```cordova plugin add @dynatrace/cordova-plugin --save```

### Copy files
1. Go to the folder for the repo you just cloned. Copy the folder *OutSystems* under folder *scripts*.
2. Go back to the folder *node_modules\\@dynatrace\cordova-plugin\scripts* under your temp folder and paste the folder *OutSystems* there

### Add Hooks to plugin.xml
1. Go to the folder *node_modules\\@dynatrace\cordova-plugin* under your temp folder and open file *plugin.xml* in a code editor
2. Add these three lines
```
  <hook src="scripts/Outsystems/npmInstall.js" type="before_plugin_install"/>
  <hook src="scripts/Outsystems/copyConfig.js" type="before_prepare"/>
  <hook src="scripts/Outsystems/copyScript.js" type="after_prepare"/>

```

> **All three are required.** `copyScript.js` must be registered *after* the
> `instrument.js` `after_prepare` hook - it places the per-environment JavaScript agent
> into the platform assets folder, and omitting it does not fail the build. The app simply
> ships the JavaScript agent baked into the module, so every environment silently reports
> to whichever Dynatrace application that copy points at.

Before
```
  <hook src="scripts/pluginAdd.js" type="before_plugin_add"/>
  <hook src="scripts/install.js" type="after_plugin_add"/>
  <hook src="scripts/uninstall.js" type="before_plugin_rm"/>
  <hook src="scripts/instrument.js" type="after_prepare"/>
  <hook src="scripts/close-log.js" type="after_build"/>
  <hook src="scripts/close-log.js" type="after_run"/>
```

After
```
  <hook src="scripts/pluginAdd.js" type="before_plugin_add"/>
  <hook src="scripts/Outsystems/npmInstall.js" type="before_plugin_install"/>
  <hook src="scripts/install.js" type="after_plugin_add"/>
  <hook src="scripts/uninstall.js" type="before_plugin_rm"/>
  <hook src="scripts/Outsystems/copyConfig.js" type="before_prepare"/>
  <hook src="scripts/instrument.js" type="after_prepare"/>
  <hook src="scripts/Outsystems/copyScript.js" type="after_prepare"/>
  <hook src="scripts/close-log.js" type="after_build"/>
  <hook src="scripts/close-log.js" type="after_run"/>
```

### Add the new code to the repo
1. Go to your temp folder and copy the content of *node_modules\\@dynatrace\cordova-plugin* (except for file *README.md*) into the folder where you cloned the *dynatrace-cordova-plugin* repo
2. Commit your changes to *origin*

## Identify users in the native side

### Add JavaScript bindings
1. Create a new file in your repo called *IdentifyUserNative.js* under folder *other*
2. Add the following content
```
// Empty constructor
function IdentifyUserNative() {}

// The function that passes work along to native shells
IdentifyUserNative.prototype.identifyUserNative = function(userId, successCallback, errorCallback) {
 var options = {};
 options.userId = userId;
 cordova.exec(successCallback, errorCallback, 'DynatraceCordovaPlugin', 'identifyUser', [options]);
}

// Installation constructor that binds IdentifyUserNative to window
IdentifyUserNative.install = function() {
 if (!window.plugins) {
   window.plugins = {};
 }
 window.plugins.identifyUserNative = new IdentifyUserNative();
 return window.plugins.identifyUserNative;
};
cordova.addConstructor(IdentifyUserNative.install);
```
3. Modify file plugin.xml and add the following
```
 <js-module src="other/IdentifyUserNative.js" name="identifyUserNative">
     <clobbers target="window.plugins.identifyUserNative" />
 </js-module>
```

Before
```
  <js-module src="other/DynatraceCordovaPlugin.js" name="dynatraceMobile">
	  <clobbers target="dynatraceMobile"/>
  </js-module>

  <platform name="ios">
```

After
```
  <js-module src="other/DynatraceCordovaPlugin.js" name="dynatraceMobile">
	  <clobbers target="dynatraceMobile"/>
  </js-module>

  <js-module src="other/IdentifyUserNative.js" name="identifyUserNative">
      <clobbers target="window.plugins.identifyUserNative" />
  </js-module>

  <platform name="ios">
```

### Add Android code
1. Open file *DynatraceCordovaPlugin.java* in folder *other*
2. Go to the top of *DynatraceCordovaPlugin* class declaration, where there are some constants declared, and add a new one called *ACTION_UEM_IDENTIFY_USER* with value *identifyUser*
```
public static final String ACTION_UEM_IDENTIFY_USER = "identifyUser";
```

Before
```
  public static final String ACTION_UEM_END_SESSION = "endVisit";
  public static final String ACTION_UEM_GET_USERPRIVACYOPTIONS = "getUserPrivacyOptions";
  public static final String ACTION_UEM_APPLY_USERPRIVACYOPTIONS = "applyUserPrivacyOptions";
```

After
```
  public static final String ACTION_UEM_END_SESSION = "endVisit";
  public static final String ACTION_UEM_GET_USERPRIVACYOPTIONS = "getUserPrivacyOptions";
  public static final String ACTION_UEM_APPLY_USERPRIVACYOPTIONS = "applyUserPrivacyOptions";
  public static final String ACTION_UEM_IDENTIFY_USER = "identifyUser";
```

3. Go to the last *else if* of method execute and add the following code
```
else if (action.equals(ACTION_UEM_IDENTIFY_USER)) {
  String userId = args.getJSONObject(0).getString("userId");

  Dynatrace.identifyUser(userId);
  callbackContext.success("UserId: " + userId);
  return true;
}
```

Before
```
...
  } else if (action.equals(ACTION_UEM_APPLY_USERPRIVACYOPTIONS)) {
    UserPrivacyOptions.Builder optionsBuilder = UserPrivacyOptions.builder();
    optionsBuilder.withDataCollectionLevel(DataCollectionLevel.values()[(args.getJSONObject(0).getInt("_dataCollectionLevel"))]);
    optionsBuilder.withCrashReportingOptedIn(args.getJSONObject(0).getBoolean("_crashReportingOptedIn"));

    Dynatrace.applyUserPrivacyOptions(optionsBuilder.build());
    callbackContext.success("Privacy settings updated!");

    return true;
  }
} catch(Exception e) {
  System.err.println("Exception: " + e.getMessage());
  callbackContext.error(e.getMessage());
  return false;
}
...
```

After
```
...
  } else if (action.equals(ACTION_UEM_APPLY_USERPRIVACYOPTIONS)) {
    UserPrivacyOptions.Builder optionsBuilder = UserPrivacyOptions.builder();
    optionsBuilder.withDataCollectionLevel(DataCollectionLevel.values()[(args.getJSONObject(0).getInt("_dataCollectionLevel"))]);
    optionsBuilder.withCrashReportingOptedIn(args.getJSONObject(0).getBoolean("_crashReportingOptedIn"));

    Dynatrace.applyUserPrivacyOptions(optionsBuilder.build());
    callbackContext.success("Privacy settings updated!");

    return true;
  } else if (action.equals(ACTION_UEM_IDENTIFY_USER)) {
    String userId = args.getJSONObject(0).getString("userId");

    Dynatrace.identifyUser(userId);
    callbackContext.success("UserId: " + userId);
    return true;
  }
} catch(Exception e) {
  System.err.println("Exception: " + e.getMessage());
  callbackContext.error(e.getMessage());
  return false;
}
...
```

### Adding iOS code
1. Open the file *DynatraceCordovaPlugin.h* in folder *other*
2. Add the following line of code at the bottom of it before the *@end* keyword
```
- (void)identifyUser:(CDVInvokedUrlCommand*)command;
```

Before
```
@interface DynatraceCordovaPlugin : CDVPlugin

- (void)endVisit:(CDVInvokedUrlCommand*)command;

- (void) getUserPrivacyOptions:(CDVInvokedUrlCommand*)command;

- (void) applyUserPrivacyOptions:(CDVInvokedUrlCommand*)command;

@end
```

After
```
@interface DynatraceCordovaPlugin : CDVPlugin

- (void)endVisit:(CDVInvokedUrlCommand*)command;

- (void) getUserPrivacyOptions:(CDVInvokedUrlCommand*)command;

- (void) applyUserPrivacyOptions:(CDVInvokedUrlCommand*)command;

- (void)identifyUser:(CDVInvokedUrlCommand*)command;

@end
```

3. Open the file *DynatraceCordovaPlugin.m* in folder other
4. Add the following code at the bottom of it before the *@end* keyword
```
- (void)identifyUser:(CDVInvokedUrlCommand*)command
{
    CDVPluginResult* pluginResult;

    if ([command.arguments objectAtIndex:0]) {

        NSString* userId = [[command.arguments objectAtIndex:0] valueForKey:@"userId"];

        DTX_StatusCode result = [Dynatrace identifyUser:userId];

        pluginResult = [CDVPluginResult resultWithStatus:CDVCommandStatus_OK messageAsString:@("Success")];
    } else {
        pluginResult = [CDVPluginResult resultWithStatus:CDVCommandStatus_ERROR];
    }

    [self.commandDelegate sendPluginResult:pluginResult callbackId:command.callbackId];
}
```

Before
```
...
    [self.commandDelegate sendPluginResult:pluginResult callbackId:command.callbackId];
}

@end
```

After
```
...
    [self.commandDelegate sendPluginResult:pluginResult callbackId:command.callbackId];
}

- (void)identifyUser:(CDVInvokedUrlCommand*)command
{
    CDVPluginResult* pluginResult;

    if ([command.arguments objectAtIndex:0]) {

        NSString* userId = [[command.arguments objectAtIndex:0] valueForKey:@"userId"];

        DTX_StatusCode result = [Dynatrace identifyUser:userId];

        pluginResult = [CDVPluginResult resultWithStatus:CDVCommandStatus_OK messageAsString:@("Success")];
    } else {
        pluginResult = [CDVPluginResult resultWithStatus:CDVCommandStatus_ERROR];
    }

    [self.commandDelegate sendPluginResult:pluginResult callbackId:command.callbackId];
}

@end
```

### Access this code from your plugin
In order to access this code from your plugin, you can execute the JavaScript function:
```
window.plugins.identifyUserNative.identifyUserNative($parameters.Value);
```

## Merge code into main branch
To finish the process
1. Commit and push all your code to your fork
2. Create a release with a tag
3. Create a PR to merge your code into main, or point your plugin to your private repo by:
   * Opening it with Service Studio
   * Clicking on the plugin name in the right-side panel
   * Going to extensibility
   * And changing the URL of the repository

## Project Structure

For a detailed directory structure with file annotations, see [CLAUDE.md - Repository Structure](./CLAUDE.md#repository-structure).

Key directories:
- `scripts/` - Cordova hooks and build-time instrumentation
- `scripts/Outsystems/` - OutSystems-specific hooks
- `other/` - Native bridge code (Java, Objective-C, JavaScript)
- `files/` - iOS frameworks and configuration templates

## Platform Support

This plugin supports:
- Android (requires Android SDK)
- iOS (requires Xcode)
- Windows (limited support)

Agent versions are defined in:
- Android: `plugin.xml` (see `framework` tag with `com.dynatrace.agent:agent-android`)
- iOS: `files/iOS/Dynatrace.xcframework`

## Getting Help

- Check the doctorDynatrace diagnostic tool: `npx doctorDynatrace`
- Review the upstream documentation: https://www.npmjs.com/package/@dynatrace/cordova-plugin
- Open an issue in the GitHub repository
