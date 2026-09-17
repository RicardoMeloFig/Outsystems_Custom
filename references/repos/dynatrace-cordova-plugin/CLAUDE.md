# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

This is an OutSystems wrapper of the Dynatrace Cordova plugin that enables mobile application monitoring. It wraps [@dynatrace/cordova-plugin](https://www.npmjs.com/package/@dynatrace/cordova-plugin) version 2.309.1 and adds OutSystems-specific customizations for MABS (Mobile Apps Build Service) 11.

**Agent Versions:**
- Android: 8.309.2.1011
- iOS: 8.309.1.1009

## Related Documentation

- **[ARCHITECTURE.md](./ARCHITECTURE.md)** - System architecture, external integrations table, and architectural tenets including build-time vs runtime separation, platform-specific native bridge pattern, and OutSystems customization layer
- **[CONTRIBUTING.md](./CONTRIBUTING.md)** - Development setup, workflow, code standards, linting/formatting configuration, and the complete wrapper update procedure including native code modifications for iOS and Android
- **[README.md](./README.md)** - User-facing documentation with installation, usage, and plugin overview

## Quick Command Reference

### Development
```bash
npm install              # Install dependencies and run install hooks
npm run lint             # Lint TypeScript source files
npm run lint:spec        # Lint test files
npm run prettier:check   # Check code formatting
npm run prettier:write   # Auto-format code
```

### TypeScript
```bash
npm run tsc              # Compile TypeScript (development)
npm run tsc:prod         # Compile TypeScript for release
```

### Testing
```bash
npm test                 # Run Jest tests
npm run test:debug       # Run tests with Node inspector
```

### Diagnostics
```bash
npx doctorDynatrace      # Plugin configuration diagnostic tool
```

Note: There is no traditional "build" step for this Cordova plugin. The plugin hooks execute during Cordova build processes in consuming applications.

## Repository Structure

```
.
├── assets/                  # Plugin assets
├── files/                   # iOS frameworks (Dynatrace.xcframework) and config template
│   └── default.config.js    # Configuration template for dynatrace.config.js
├── model/                   # Data models
├── networking/              # Network interceptor implementations
│   ├── NativeNetworkInterceptorUtils.js
│   └── MobileFirstNetworkInterceptor.js
├── other/                   # Native bridge implementations
│   ├── DynatraceCordovaPlugin.java    # Android native bridge
│   ├── DynatraceCordovaPlugin.m/h     # iOS native bridge (Objective-C)
│   ├── DynatraceCordovaPlugin.js      # Unified JavaScript API
│   └── IdentifyUserNative.js          # OutSystems custom user identification
├── scripts/                 # Cordova hooks and build-time instrumentation
│   ├── Instrument.js        # Main build-time HTML injection hook
│   ├── Install.js           # Plugin installation hook
│   ├── Android.js           # Android Gradle configuration generation
│   ├── Ios.js              # iOS plist modification
│   ├── DownloadAgent.js    # JavaScript agent download from Dynatrace API
│   ├── Doctor.js           # Diagnostic CLI tool
│   ├── Outsystems/         # OutSystems-specific hooks
│   │   ├── copyConfig.js   # Copies dynatrace.config.js to project root (platform folder first, www/ fallback)
│   │   ├── copyScript.js   # Copies a JavaScript agent override into the Android platform assets
│   │   └── npmInstall.js   # npm dependency installation for older Cordova
│   ├── config/             # Configuration reading/parsing
│   ├── helpers/            # Instrumentation helpers
│   ├── html/               # HTML parsing and script injection
│   └── utils/              # Utility functions
├── typings/                 # TypeScript definitions for plugin API
│   └── main.d.ts
├── plugin.xml               # Cordova plugin manifest with hooks and platform config
└── package.json             # NPM package configuration
```

See [ARCHITECTURE.md](./ARCHITECTURE.md) for architectural patterns and design constraints.

## Important Context

### This is a Wrapper Plugin

This repository wraps the official `@dynatrace/cordova-plugin` and adds OutSystems-specific functionality. When modifying:

1. **Core Dynatrace functionality** lives in the upstream package - do not modify unless adding OutSystems integration points
2. **OutSystems customizations** include:
   - `scripts/Outsystems/` directory with build hooks
   - `other/IdentifyUserNative.js` for native user identification
   - Configuration file copying to the project root, preferring the platform build folder so
     MABS per-environment resource overrides are picked up (`copyConfig.js`)
   - JavaScript agent override copying into the Android platform assets (`copyScript.js`)
   - npm installation handling for older MABS Cordova versions

### Build-Time vs Runtime Execution

This plugin operates in two phases with different execution contexts:

- **Build-time** - Cordova hooks (JavaScript running in Node.js) inject monitoring code during compilation
  - Downloads JavaScript agent from Dynatrace API
  - Injects monitoring scripts into HTML files
  - Configures Android Gradle and iOS plist files
  - Key files: `scripts/Instrument.js`, `scripts/DownloadAgent.js`, `scripts/html/HtmlInstrumentation.js`

- **Runtime** - Native bridges execute on mobile devices to send telemetry
  - JavaScript API calls bridge to native Dynatrace SDKs
  - Telemetry sent to Dynatrace Beacon/Cluster
  - Key files: `other/DynatraceCordovaPlugin.java`, `other/DynatraceCordovaPlugin.m`

See [ARCHITECTURE.md](./ARCHITECTURE.md) for the build-time vs runtime separation tenet.

### Configuration File

Applications configure monitoring via `dynatrace.config.js` (not checked into this repo). See `files/default.config.js` for the template structure showing beacon URLs and application IDs for Android/iOS.

### Platform-Specific Native Code

When modifying native bridges:
- **Android**: `other/DynatraceCordovaPlugin.java` - Action constants and `execute()` method route JavaScript calls to native Android SDK
- **iOS**: `other/DynatraceCordovaPlugin.h` and `.m` - Objective-C methods bridge to Dynatrace iOS XCFramework
- **JavaScript**: `other/DynatraceCordovaPlugin.js` - Unified API that abstracts platform differences

### Updating the Wrapper

Complete step-by-step procedure in [CONTRIBUTING.md](./CONTRIBUTING.md#updating-the-plugin-wrapper). High-level steps:

1. Install new `@dynatrace/cordova-plugin` version via Cordova
2. Copy OutSystems customizations (`scripts/Outsystems/`, `other/IdentifyUserNative.js`)
3. Add OutSystems hooks to `plugin.xml`
4. Modify native code (Android Java, iOS Objective-C) to add `identifyUser` action
5. Update version numbers in `package.json`, `plugin.xml`, and README.md

**Always consult CONTRIBUTING.md for the authoritative update procedure.**

### Versioning and Branching

- **Branch naming**: MABS version updates use pattern `MABS[version]` (e.g., `MABS11`, `MABS10`)
- **Version numbers**: Keep `package.json` and `plugin.xml` versions synchronized
- **Tags**: Create git tags for releases (e.g., `v11.0`)
- See [CONTRIBUTING.md](./CONTRIBUTING.md) for commit message conventions

### Code Quality Enforcement

Pre-commit hooks (via Husky) automatically run ESLint and Prettier on staged files. See [CONTRIBUTING.md](./CONTRIBUTING.md) for complete code standards including ESLint plugins configuration.

### No TypeScript Source Files

Despite TypeScript tooling in package.json, the repository contains only JavaScript files. TypeScript type definitions exist at `typings/main.d.ts` for API consumers. The `tsc` commands are inherited from the upstream package but not actively used in this wrapper.

## Common Tasks

### Adding a New Native API Method

To expose a new native Dynatrace SDK method to JavaScript:

1. Add JavaScript binding in `other/DynatraceCordovaPlugin.js` (or create separate file like `IdentifyUserNative.js`)
2. Register js-module in `plugin.xml`
3. Add action constant and handler in `other/DynatraceCordovaPlugin.java` (Android)
4. Add method declaration in `other/DynatraceCordovaPlugin.h` and implementation in `.m` (iOS)
5. Test on both platforms

See [CONTRIBUTING.md - "Identify users in the native side"](./CONTRIBUTING.md#identify-users-in-the-native-side) section for a complete worked example.

### Diagnosing Plugin Issues

Run `npx doctorDynatrace` in a consuming application to diagnose configuration problems. Source: `scripts/Doctor.js`.

### Finding Hook Execution Order

Check `plugin.xml` for hook registration. Current order:
1. `before_plugin_install` - OutSystems npm installation
2. `after_plugin_add` - Standard installation
3. `before_prepare` - OutSystems config copy (`Outsystems/copyConfig.js`)
4. `after_prepare` - HTML instrumentation (`Instrument.js`)
5. `after_prepare` - OutSystems JavaScript agent copy (`Outsystems/copyScript.js`), registered after `Instrument.js`
6. `after_build` / `after_run` - Log cleanup

## External Dependencies

- **Dynatrace API Server** (build-time): Downloads JavaScript monitoring agent via HTTPS
- **Dynatrace Beacon/Cluster** (runtime): Receives telemetry data from instrumented apps
- **Native SDKs** (build-time):
  - Android: Maven dependency `com.dynatrace.agent:agent-android:8.309.2.1011`
  - iOS: XCFramework bundle at `files/iOS/Dynatrace.xcframework`

See [ARCHITECTURE.md](./ARCHITECTURE.md) external integrations table for communication types and purposes.
