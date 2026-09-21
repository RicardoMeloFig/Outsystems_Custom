# AGENTS.md

Workspace guide for **__APPNAME__** — a thin, linked **consumer project**
for OutSystems 11 automation. This file covers what lives HERE. All tooling
(5 MCP servers, 36 skills, 8 agents (coordinator + architect + 6
specialists), 5 workflow commands, scripts,
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

## Routing (agents from the baseline)

For ANY OutSystems task, call **`os-coordinator`** — it classifies the
task, dispatches the right specialists, verifies results, and returns one
consolidated report. Direct routes when the task is unambiguous:

| Task | Agent |
|------|-------|
| Any OutSystems task — coordinate end-to-end (call this first) | `os-coordinator` |
| Docs/reference lookup, "How does X work in OutSystems?" | `os-reference` |
| Extract entities/actions/flows/UI from a module open in Service Studio | `os-extract` |
| Design a screen/flow/cross-module change BEFORE implementation | `os-architect` |
| Edit the module open in Service Studio (live, instant tree updates) — **DEFAULT** | `os-edit-live` |
| Edit a saved `.oml` headlessly (no Service Studio needed) — **ONLY on explicit user request** | `os-edit-headless` |
| Generate technical docs / user guide / interactive HTML site | `os-docs` |
| Build/fix the tooling itself (in the baseline workspace) | `os-toolkit` |

Commands: `/extract-module`, `/document-module`, `/research`, `/build`,
`/design`.

## Ground rules

1. **All outputs go under this project root** — extraction docs, edited
   `.oml` artifacts (write them under `OMLs/`), generated sites. Never
   write into the baseline.
2. Baseline rules apply unchanged: never guess opaque keys (probe first);
   succeeded ≠ landed (verify by read-back); confirm destructive actions
   before executing; extraction requires the module fully loaded in Service
   Studio; one editing session per module. **Editing is live-first**: use
   `os-edit-live` by default; headless `.oml` editing only when the user
   explicitly requests it — never switch to headless on your own initiative.
3. **Architect gate**: new screens, flows/service actions, cross-module,
   data-model, and security changes go through `os-architect` BEFORE any
   editor. The architect verifies what needs doing first and where, checks
   against best practices, and sequences every contract bottom-up by module
   dependency: producer (lower) modules first — e.g. CS (entities/data) →
   BL (logic/service actions) → UI (screens/blocks) in the classic 4-layer
   canvas — and higher (consumer) modules are only built on producers that
   exist. Trivial edits (label text, style class, single-expression fix)
   go straight to an editor plus read-back. Planning-only requests stop at
   the design contract.
4. Toolkit maintenance (rebuilds, bridge, script/config fixes) happens in
   the baseline workspace — open `__BASELINE__` there; do not edit it from
   here.
5. `opencode.json`, `.opencode/agent/*`, and `.opencode/command/*` are
   GENERATED files (tracked in `.opencode/link-manifest.json`). After
   baseline agent/command/config changes, refresh and restart OpenCode:

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File "__BASELINE__\scripts\New-LinkedProject.ps1" -Target "__TARGET__" -AppName "__APPNAME__" -Refresh
   ```

6. Verify this project any time (must print READY and exit 0):

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File "__BASELINE__\scripts\Verify-LinkedProject.ps1" -Target "__TARGET__"
   ```

7. Launch OpenCode Desktop with THIS folder as the working directory (so
   this project's `opencode.json` is picked up), then check `/mcp` shows
   all five `outsystems-*` servers connected.
