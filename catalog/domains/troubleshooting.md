# Troubleshooting, performance & monitoring — reference routing

**Consult when**: performance issues (APDEX, slow queries), tracing/logging,
application monitoring, error catalog lookup, debugging (breakpoints,
watches), memory/threads, support diagnostics (dumps, diag tools), custom
error screens, or tech-debt management.

## Primary sources

| Repository | Where to look | What you get |
|---|---|---|
| `docs-product` (O11) | `src/monitor-and-troubleshoot/` (`how-application-performance-is-measured.md`, `the-apdex-performance-score.md`, `troubleshoot-the-performance-of-an-application.md`, `trace-executed-queries.md`, `logging/`, `log-streaming/`, `reactive-app-lifetime-analytics/`, `manage-tech-debt/`, `app-feedback/`), `src/debugging-apps/` (`breakpoints.md`, `watches.md`, `threads.md`, `debugger-ui-reference.md`), `src/ref/errors-and-warnings/`, `src/app-architecture/performance-top-10-rules.md` | The official debugging/performance methodology + error catalog |
| `docs-support` | `src/` | Support/troubleshooting articles for platform-level issues |

## Secondary sources

| Repository | When |
|---|---|
| `techsupp-osdiagtool`, `CollectDumps`, `OutSystems-CollectInfo-wdocker` | Gathering platform diagnostics (support scenarios) |
| `MonitorProbe`, `outsystems-elastic-integration`, `sqs_beats`, `opentelemetry-collector-contrib` | Shipping monitoring data out (Elastic/OTel) |
| `Napal` | Analyzing Windows performance-counter CSV reports |
| `watchmen`, `os-DigitalOSAnalytics` | Client-side JS error/analytics tracking (Forge component sources) |
| `public-custom-404-plugin` | Custom 404/unhandled-error screens |

## Search tips

- Grep `docs-product/toc.yml` "Monitor and troubleshoot" and "Debugging
  apps"; `related.yml` maps titles.
- Error codes: search `src/ref/errors-and-warnings/` for the exact code from
  the Service Studio/Server error.
- Frontmatter `app_type:` distinguishes Reactive lifetime analytics from
  Traditional.

## Related workspace skills

`publishing` (error triage during publish), `os-toolkit-maintenance`.

## Gotchas

- Performance and behavior claims from docs may differ per service center
  version — check your platform version.
- Client-side monitoring (watchmen/os-DigitalOSAnalytics) tracks the web
  app, not backend performance — combine with platform monitoring.
