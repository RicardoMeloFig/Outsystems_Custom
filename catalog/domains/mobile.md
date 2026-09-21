# Mobile — reference routing

**Consult when**: mobile app behavior, offline data sync, push
notifications (FCM/APNs), plugins (camera, barcode, geolocation, in-app
browser, secure storage), MABS/packaging, OTA upgrades, native SDK
integration, mobile best practices.

## Primary sources

| Repository | Where to look | What you get |
|---|---|---|
| `docs-product` (O11) | `src/building-apps/data/offline/` (`sync-implement.md`, `sync-reference.md`, `patterns/`), `src/building-apps/mobile-best-practices/`, `src/integration-with-systems/mobile-plugins/` (`firebase/`, `camera/`, `barcode/`, `cordova-plugin/`), `src/deploying-apps/mobile-app-packaging-delivery/` (`mobile-apps-build-service/`, `generate-distribute-mobile-app/`, `ota-upgrades.md`), `src/monitor-and-troubleshoot/troubleshoot-debugging-mobile/` | Offline sync model, plugin usage, MABS and distribution |
| `outsystems-phonegap-plugin-push` | Push plugin clone backing the FCM Forge component | The shipped push plugin source |
| `os-plugins-base-interface` | Plugin interfaces/AARs that Cordova plugins implement | What a plugin must implement |

## Secondary sources (OutSystems-supported plugins & native libs)

- `cordova-outsystems-*` family — supported plugins: barcode, camera,
  fileviewer, inappbrowser (cookie isolation), logger-proxy, payments,
  secure-sqlite-bundle, sociallogins, firebase-*, healthfitness,
  appfeedback, error-screen, airwatch, google-ar-core.
- Native libs: `OSBarcodeLib-*`, `OSCore-iOS`, `OSInAppBrowserLib-*`,
  `OSPaymentsLib-iOS`, `Latency-AddOn-*`.
- Secure/offline storage chain: `sqlcipher`, `Cordova-sqlcipher-adapter`,
  `cordova-outsystems-secure-sqlite-bundle`, `Android-sqlite-*`.
- Pinning: `TrustKit`, `capacitor-outsystems-sslpinning`.
- Community plugins (search on specific need): `cordova-plugin-*`,
  `phonegap-*`, `OneSignal-*`, `mabs13-plugin-updater`, `csZBar`,
  `WebBarcodePlugin` (in-browser), `OutSystemsNow-*`, `OutBuilding`.

## Search tips

- `docs-product/toc.yml` "Building apps → Data → offline" and
  "Integration → Mobile plugins" sections; `related.yml` maps titles.
- Offline sync is the highest-complexity area — read `sync-implement.md`
  and `sync-reference.md` fully before changing sync patterns.
- Plugin repos: check the Forge component version's plugin fork
  (`cordova-outsystems-*`) before community `cordova-plugin-*`.

## Related workspace skills

`building-lists`/`screen-templates` (mobile UI), `data.md` (offline stores),
`publishing` (build/triage).

## Gotchas

- MABS versions dictate plugin compatibility (Swift Package Manager via
  `mabs13-plugin-updater` for Cordova iOS 8).
- O11 mobile packaging differs from ODC; ODC guidance lives in `docs-odc`.
