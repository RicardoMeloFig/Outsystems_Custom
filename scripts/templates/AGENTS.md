# AGENTS.md

Workspace guide for **__APPNAME__** — a thin, linked **consumer project**
for OutSystems 11 automation. This file covers what lives HERE. All tooling
(5 MCP servers, 36 skills, 6 specialist agents, workflow commands, scripts,
and the reference library) is consumed in place from the shared baseline:

**Baseline:** `__BASELINE__`

The baseline `AGENTS.md` is auto-loaded via `instructions` and is the
authority for routing, ground rules, and runbooks. This file only adds
consumer-specific conventions.

## Layout (this project)

| Path | Contents |
|------|----------|
| `OMLs/` | Local `.oml` files (not git-tracked) |
| `open/` | Scratch drop folder |
| `docs/<ModuleName>/` | Extraction outputs (regenerated; not tracked) |
| `docs/*.md`, `docs/site-src/` | Hand-written docs + authored site fragments (tracked) |
| `docs/site/` | Rendered HTML site (generated; not tracked) |
| `__APPNAME__.md` | App notes: modules, conventions |

## Routing (specialists from the baseline)

| Task | Agent |
|------|-------|
| Docs/reference lookup, "How does X work in OutSystems?" | `os-reference` |
| Extract entities/actions/flows/UI from a module open in Service Studio | `os-extract` |
| Edit a saved `.oml` headlessly (no Service Studio needed) | `os-edit-headless` |
| Edit the module open in Service Studio (live, instant tree updates) | `os-edit-live` |
| Generate technical docs / user guide / interactive HTML site | `os-docs` |
| Build/fix the tooling itself (in the baseline workspace) | `os-toolkit` |

Commands: `/extract-module`, `/document-module`, `/research`, `/build`.

## Ground rules

1. **All outputs go under this project root** — extraction docs, edited
   `.oml` artifacts (write them under `OMLs/`), generated sites. Never
   write into the baseline.
2. Baseline rules apply unchanged: never guess opaque keys (probe first);
   succeeded ≠ landed (verify by read-back); confirm destructive actions
   before executing; extraction requires the module fully loaded in Service
   Studio; one editing session per module.
3. Toolkit maintenance (rebuilds, bridge, script/config fixes) happens in
   the baseline workspace — open `__BASELINE__` there; do not edit it from
   here.
4. `opencode.json`, `.opencode/agent/*`, and `.opencode/command/*` are
   GENERATED files (tracked in `.opencode/link-manifest.json`). After
   baseline agent/command/config changes, refresh and restart OpenCode:

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File "__BASELINE__\scripts\New-LinkedProject.ps1" -Target "__TARGET__" -AppName "__APPNAME__" -Refresh
   ```

5. Verify this project any time (must print READY and exit 0):

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File "__BASELINE__\scripts\Verify-LinkedProject.ps1" -Target "__TARGET__"
   ```

6. Launch OpenCode Desktop with THIS folder as the working directory (so
   this project's `opencode.json` is picked up), then check `/mcp` shows
   all five `outsystems-*` servers connected.
