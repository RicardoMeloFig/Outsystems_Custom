# capacitor-outsystems-sslpinning

Enables SSL Pinning for OutSystems Capacitor mobile applications (MABS 12 or higher - ODC only).

## How to use

This plugin is meant to be used only in OutSystems mobile applications.
Refer to our [customer-facing OutSystems docs for more information](https://success.outsystems.com/documentation/outsystems_developer_cloud/integration_with_external_systems/mobile_plugins/ssl_pinning_plugin/).

The Certificate Pins get injected via [build actions](./build-actions/) by the consuming apps on ODC.

## Credits

- TrustKit authors and collaborators, [https://github.com/datatheorem/TrustKit](https://github.com/datatheorem/TrustKit) - Used For pinning 
- OkHTTP authors and collaborators, [https://github.com/square/okhttp](https://github.com/square/okhttp)

## LICENSE

This plugin is released under MIT License. See [LICENSE](./LICENSE) for details.