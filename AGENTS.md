# AGENTS.md

Workspace guide for **Outsystems_Custom** — a consolidated OutSystems 11 AI
automation workspace. Read this first. It is a **router**: it tells you where
things live and which specialist agent handles which task. Detailed runbooks
live inside each component (each toolkit has its own scoped `AGENTS.md`).

## Layout

| Path | Contents |
|------|----------|
| `toolkits/extraction/` | Read-only extraction: 3 ClrMD MCP servers (`outsystems-tools`, `outsystems-logic`, `outsystems-ui`) + 7 skills + schemas. Reads module state from a running Service Studio process. |
| `toolkits/editor/` | Editing: 2 MCP servers — `outsystems-omleditor` (headless `.oml` files) and `outsystems-liveeditor` (live in-process via the OsLiveBridge plugin) + 26 skills. |
| `toolkits/html-docs/` | Interactive HTML documentation site generator: `html-docs-generation` skill + `Render-Site.ps1` + templates/assets. |
| `references/repos/` | Local snapshots of ~236 upstream repositories (official docs, UI libraries, plugins, examples). Git histories remain in the original `Outsystems REPO` folders. |
| `catalog/` | `repositories.json` (provenance: origin, commit, branch, license, size per repo) + `topics.md` (curated entry points) + `toolchain.json` (compatibility manifest). |
| `projects/` | Application-specific workspaces (extraction outputs, generated docs/sites). |
| `scripts/` | Unified build/verify/launch tooling + linked-consumer scaffolding (`New-LinkedProject.ps1`, `Verify-LinkedProject.ps1`, `Test-McpHandshake.ps1`) and `scripts/templates/`. |
| `docs/` | Workspace-level documentation (architecture). |

Each toolkit keeps its own `AGENTS.md`, `SETUP.md`, scripts, and `.opencode/skills/`
(scope: read the nested one only when working in that toolkit).

## Routing: which agent for which task

OutSystems router: **`os-coordinator`** (subagent) — the primary agent
calls it for ANY OutSystems task. It classifies the request, gathers
evidence, dispatches specialists with a handoff contract, verifies results
against acceptance checks, and returns one consolidated report. All five
MCP servers are **disabled for the primary agent and the coordinator**
(context stays lean); specialists re-enable only the tools they need:

| Task | Agent | Tools it enables |
|------|-------|------------------|
| Any OutSystems task — route, coordinate, verify (call this first) | `os-coordinator` | none (Task tool only) |
| "How does X work in OutSystems?", docs lookup, API/pattern/CSS reference | `os-reference` | none (catalog + grep/read) |
| Extract entities/actions/flows/UI from a module open in Service Studio | `os-extract` | `outsystems-tools_*`, `outsystems-logic_*`, `outsystems-ui_*` |
| Design before substantial builds (see architect gate below) | `os-architect` | none (read-only, no edits) |
| Edit the module **open in Service Studio** (live, instant tree updates) — **DEFAULT editing path** | `os-edit-live` | `outsystems-liveeditor_*` |
| Edit a saved `.oml` file headlessly (no SS needed) — **ONLY when the user explicitly requests headless/no-SS/file editing** | `os-edit-headless` | `outsystems-omleditor_*` |
| Generate technical docs / user guide / interactive HTML site | `os-docs` | none (skills + files) |
| Build/fix the MCP servers, bridge, scripts, config | `os-toolkit` | none (bash + files) |

Specialists may also be called directly when the task is unambiguous
(e.g. `/research` goes straight to `os-reference`). For multi-stage work
(e.g. "extract and document this module"), chain: `os-extract` → `os-docs`.
For substantial edits: `os-extract` (evidence) → `os-architect` (design
contract) → `os-edit-live` (implement; `os-edit-headless` only when the user
explicitly requested it) → `os-extract`
(independent read-back against the acceptance checks).

### Architect gate

New screens, web blocks, flows/service actions, cross-module changes, data
model changes, and security changes go through `os-architect` BEFORE any
editor. The architect verifies what needs doing first and where, checks
against best practices, and sequences every contract bottom-up by module
dependency: producer (lower) modules first — e.g. CS (entities/data) → BL
(logic/service actions) → UI (screens/blocks) in the classic 4-layer
canvas — and higher (consumer) modules are only built on producers that
exist. Trivial edits (label text, style class, single-expression fix) may
go straight to an editor plus read-back. Planning-only requests stop at the
design contract — nothing is built.

## Ground rules (always)

1. **Never guess opaque keys** (module names, element names, entity Ids,
   node indices). Probe/read first; re-probe after every mutation — an index
   from a previous step is stale.
2. **Succeeded ≠ landed.** Verify values with read-back tools before declaring
   done. Failed mutations may have half-landed — check state before retrying.
3. **Confirm before destructive actions**: deleting elements/widgets/screens,
   overwriting a source `.oml` in place, publishing, installing the bridge DLL
   into Service Studio, changing trust settings. Restate the specific change
   and wait for explicit confirmation.
4. **Extraction prerequisites**: the target module must be fully loaded in a
   running `ServiceStudio.exe` (not just on the recent-projects screen). Call
   `get_open_module` first; pass `pid` when multiple modules/instances exist.
5. **Editing is live-first.** Default to `os-edit-live` (module open in SS,
   instant tree updates). Use headless `.oml` editing (`os-edit-headless`)
   ONLY when the user explicitly requests it (e.g. "edit the .oml file",
   "no SS", "headless"). Never switch to headless on your own initiative; if
   the editing mode is unclear, ask.
6. **Reference library**: go catalog-first (`catalog/topics.md` → repo →
   targeted grep). Never bulk-read or index the whole `references/repos/`
   tree; large binaries and `node_modules` are off-limits for routine search.
7. **Edits to the same module are serialized** — one editing specialist at a
   time per module.

## Build / verify / launch (workspace root)

```powershell
.\scripts\Build-All.ps1        # publish all 5 MCP exes (first run after clone)
.\scripts\Build-Bridge.ps1     # compile the SS plugin (compile-only by default)
.\scripts\Verify-Project.ps1   # integrity gate: must print OK: and exit 0
.\scripts\Start-OpenCode.ps1   # staleness gate + launch OpenCode Desktop here
```

Launch OpenCode from this root (or via `Start-OpenCode.ps1`). Inside opencode,
`/mcp` must show all five servers connected. See `SETUP.md` for prerequisites
and troubleshooting.

## Linked consumer projects (thin per-app references to this baseline)

Application projects (new ones; Arkki/Kopa predate this) should be **thin
consumers**: they hold only app content (`OMLs/`, `docs/`, notes) and consume
every tool — 5 MCP servers, 36 skills, 8 agents (coordinator + architect +
6 specialists), 5 commands — from this
baseline in place, via absolute paths in their `opencode.json`. Their
`AGENTS.md` plus this file are auto-loaded there (`instructions`), so the
routing table above applies unchanged in consumers.

```powershell
# create a consumer (run from this root):
.\scripts\New-LinkedProject.ps1 -Target "C:\Users\<you>\Documents\NewAppAutomation" -AppName "NewApp"

# after baseline agent/command/config changes (regenerates only unmodified
# generated files — user edits are preserved, tracked via a hash manifest):
.\scripts\New-LinkedProject.ps1 -Target <target> -AppName <app> -Refresh

# consumer integrity gate (must print READY and exit 0):
.\scripts\Verify-LinkedProject.ps1 -Target <target>

# live MCP handshake probe (initialize + tools/list per exe, no SS needed):
.\scripts\Test-McpHandshake.ps1
```

Generated in the consumer: `opencode.json`, `.opencode/agent/*`,
`.opencode/command/*` (GENERATED — refresh-managed), plus `AGENTS.md`,
`.gitignore`, `<AppName>.md`, `OMLs/`, `open/`, `docs/` (user-owned).
All outputs (extraction docs, edited `.oml`, rendered sites) stay in the
consumer; toolkit maintenance stays here. Consumer agents re-enable their
specialty tools via per-agent `permission` (the primary agent there, as here,
sees none of the `outsystems-*` tools).
