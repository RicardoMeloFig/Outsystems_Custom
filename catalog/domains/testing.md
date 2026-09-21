# Testing & QA — reference routing

**Consult when**: automated tests, API mocking/stubbing, contract testing
(Pact), visual regression (Applitools), UI automation, database benchmarks,
mobile/network condition testing, or setting up CI test stages.

## Primary sources

| Repository | Where to look | What you get |
|---|---|---|
| `docs-product` (O11) | `src/testing-apps/` | What testing looks like in the platform (built-in test framework, app testing) |
| `wiremock` | `README.md` + stubbing config | Java HTTP test double (record/playback, fault injection) |
| `pact-python`, `sdlc-contract-example-consumer`, `sdlc-contract-example-provider` | Examples | Consumer-driven contract testing in CI (OS carries a fork) |

## Secondary sources

| Repository | When |
|---|---|
| `httpmock` | Lightweight Node mock server for integration tests |
| `eyes.sdk.javascript1` | Visual regression with Applitools Eyes |
| `node-saucelabs` | Cross-browser testing via Sauce Labs |
| `SikuliSharp.NetCore`, `netemu` | Image-based UI automation; network condition simulation (RPi) |
| `benchbase` | Multi-DBMS SQL benchmarking (performance studies) |

## Search tips

- OutSystems O11 has no built-in unit test runner — Forge tools and ODC
  Test Framework are the practical routes; the docs `testing-apps/` section
  says what the platform itself offers.
- Contract-test repos show the pattern; wire them into the pipeline repos
  from `devops.md`.

## Related workspace skills / agents

`publishing` (verify before publish), `os-coordinator` (verification via
read-back), `Verify-Project.ps1` / `Verify-LinkedProject.ps1` (workspace
gates).

## Gotchas

- `outsystems-datagrid`/`outsystems-ui` ship their own unit tests — read
  those test folders as behavior documentation before writing tests for
  customizations.
- UI-automation tooling (Sikuli, eyes) targets the running browser, not the
  module — align with the app's actual URLs.
