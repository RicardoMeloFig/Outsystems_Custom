# AGENTS.md

Canonical entry point AND runbook for any AI agent working in this repo. Read
this **first**, before touching tools or skill files. It is written to be
followed step-by-step: each numbered sequence is deterministic (do X, then Y).

## Project overview

**Extraction-only** baseline for **OutSystems 11** (Service Studio). Three local
ClrMD MCP servers (declared in `opencode.json`) and **7 skills**
(`.opencode/skills/`, auto-discovered) that **read** module state from process
memory (bypassing the `.oml` productKey signature):

| Server | Backend | Purpose |
|--------|---------|---------|
| `outsystems-tools` | ClrMD (memory) | Detect open module, parse `.oml` headers, live entity/structure read |
| `outsystems-logic` | ClrMD (memory) | Extract actions, entities, structures, site properties, full flow-traced reports |
| `outsystems-ui` | ClrMD (memory) | Extract the presentation layer: themes + CSS, screens, web blocks, widget tree, client actions (granular, split files) |

This baseline **only reads**. **Editing** (live in-process + headless `.oml`) lives
in the separate `AI Outsystems Automation Editor` baseline. The two are independent;
consumers that need both reading and editing link **both** baselines.

The 7 skills (auto-discovered from `.opencode/skills/*/SKILL.md`; each loads only
when its responsibility is triggered, to keep context lean):
- **Extraction (ClrMD, read):** `outsystems-tools`, `outsystems-logic`,
  `ui-extraction`, `module-extraction`, `multi-module-extraction`
- **Docs (synthesis):** `documentation-generation`, `user-guide-generation`

## Prerequisites (confirm before any tool call)

### On the machine
- **Git** — https://git-scm.com
- **.NET 8+ SDK** — `dotnet --list-sdks` must show an `8.0.x` (or newer) entry. All
  three MCP servers are `net8.0` (ClrMD). No `net10.0-windows`/FlaUI/Ollama needed
  here — editing lives in the `AI Outsystems Automation Editor` baseline.
- **opencode CLI** — https://opencode.ai
- **OutSystems Service Studio 11** — installed, able to open your module.
- **Windows x64**.

### For extraction tools (ClrMD: `outsystems-tools_*`, `outsystems-logic_*`, `outsystems-ui_*`)
- The target module must be **open and finished loading** in a running
  `ServiceStudio.exe` process. A module listed only on the recent-projects
  screen does **not** count — it must be loaded into memory.
- **UI focus is not required.** The window may be in the background, the module
  tab does not need to be active, and no clicking is needed.
- **Multiple modules may be open at once.** All loaded modules are reachable
  via the managed heap regardless of which is visually selected. (See
  "Multi-module targeting" below for how this affects output.)

## Which tool family do I need?

| Goal | Family | Example tool |
|------|--------|--------------|
| Read logic/data (entities, actions, flows, structures, site props) | Extractors (ClrMD) | `get_module_report`, `live_reader`, `list_entities` |
| Read UI layer (themes, CSS, screens, web blocks, widget tree, client actions) | Extractors (ClrMD) | `extract_themes`, `extract_ui`, `extract_web_blocks` |
| **Mutate module state** (create/edit elements, flows) | **Editor baseline** (`AI Outsystems Automation Editor`) | `live_create_service_action`, `live_clone_service_action`, `live_*` flow tools |
| Synthesize a written app-level document from extractions | Skill (synthesis) | `documentation-generation` skill |
| Synthesize a plain-language end-user guide from extractions | Skill (synthesis) | `user-guide-generation` skill |

## Canonical runbook: from fresh clone to documented module

Follow these stages in order. Each stage is deterministic.

### Stage 0 — One-time setup on a new PC (skip if already built)
1. `git clone <repo>` then `cd` into the repo root.
2. `.\scripts\Build-All.ps1` — builds the 3 gitignored MCP exes into each
   `<project>/publish/` folder. (Use `-SelfContained` on a PC with no .NET
   runtime.)
3. `.\scripts\Verify-Project.ps1` — hard-fail integrity gate. **Must print
   `OK:` and exit 0.** If it lists `MISSING:` lines, stop and fix them (the
   message says what) — do not proceed.
4. Launch opencode **from the repo root** (where `opencode.json` lives): `opencode`.
   Or use the **Start Menu shortcut** (runs `scripts\start-opencode.ps1`, which
   checks for stale exes and rebuilds automatically before launching).
5. Inside opencode, run `/mcp`. All three servers must show active/connected.
   - If a server is inactive: the `.exe` is missing or a runtime is missing →
     re-run `Build-All.ps1` (or `-SelfContained`), then restart opencode.

### Stage 0b — Staleness gate (automatic via launcher script)
After any MCP source edit (or after a `git pull` that touches `mcp/*/Program.cs`),
the built exes go stale: the source is newer than the exe, so the running exe
lacks the latest fixes (e.g., `ReadStrManual` for ClrMD 4096-char truncation,
`ownerESpace` for per-module attribution). Stale exes silently produce
truncated or single-module output.

**The Start Menu shortcut for OpenCode points to `scripts\start-opencode.ps1`,
which runs `Ensure-Built.ps1` automatically before launching OpenCode.** This
means: when you launch OpenCode from the Start Menu, stale exes are detected and
rebuilt before any DLLs are locked. You do not need to run `Ensure-Built.ps1`
manually.

If OpenCode is launched directly (bypassing the shortcut), or if a rebuild is
needed mid-session:
```
.\scripts\Ensure-Built.ps1
```
- Exit 0 (all `FRESH`/`REBUILT`): safe to proceed.
- Exit 1 (`STALE-LOCKED`): opencode is holding the DLLs open. Close opencode,
  re-run `Ensure-Built.ps1` in a terminal, restart opencode. The AI must tell
  the user this and halt extraction until fresh.

`Verify-Project.ps1 -Strict` also catches staleness as a hard failure; the
default `Verify-Project.ps1` reports it as a warning only. To avoid mid-session
restarts, always launch via the Start Menu shortcut (which runs the gate
automatically).

### Stage 1 — Identify what is open
1. Confirm Service Studio is running and the target module is **fully loaded**
   (not just on the recent-projects screen). If the module is not yet open in
   SS, **open it manually** (DevEnv "Recent modules" list: select + Enter) —
   extractors only see modules loaded into memory. (Opening is a manual user
   step; automated editing/opening lives in the Editor baseline.)
2. Call `get_open_module` (no args). It lists every open module **with its
   PID** (reads the AutoSave cache). Note the target module name + PID.

### Stage 2 — Extract per-module content
See "Multi-module targeting" below. In short:
- Always run `parse_oml_header` per `.oml` file for authoritative per-module
  metadata.
- All modules can stay open in one PID — the extractors attribute content
  per module automatically via `ownerESpace` (no isolation needed).
- If output shows all content under one module name, the exe is stale —
  rebuild with `.\scripts\Build-All.ps1` or `dotnet publish`.

### Stage 3 — Extract
Call the tool(s) matching your goal (table above) with `pid` when needed.
Persist output under `docs/<ModuleName>/` (gitignored via `.gitignore` `docs/*/`).

### Stage 4 — Synthesize documentation
Two modes (see the `documentation-generation` skill for full details):

- **Per-module (DEFAULT):** triggered by "extract documentation for all open
  modules", "document all modules", "generate documentation". Produces
  `docs/<ModuleName>/TECHNICAL_DOCUMENTATION.md` per module in a tree-format
  technical reference (Module Overview → Interface → Logic → Theme → Data &
  Variables) with full action flow traces, entities, structures, site
  properties. Content is attributed per module automatically via `ownerESpace`.
- **App-level (ONLY on explicit request):** triggered by "application
  architecture document", "architecture canvas", "module interactions", "overall
  document". Produces a single `docs/TECHNICAL_DOCUMENTATION.md` overview with
  the Architecture Canvas and cross-module interactions. References per-module docs.

## Multi-module targeting (per-module attribution via ownerESpace)

When more than one module is open (or more than one `ServiceStudio.exe` is
running), auto-detection can pick the wrong target. Always call `get_open_module`
first and pass `pid` explicitly when there is any ambiguity.

**Key fact:** all three ClrMD extractors (`outsystems-logic`, `outsystems-ui`,
`outsystems-tools`) use **`ownerESpace` back-references** to attribute every
model object to its owning module. `BuildEspaceMap()` finds all
`ServiceStudio.Model.ESpace` objects on the heap and maps their address →
module name. `ResolveModule()` reads each object's `ownerESpace` field to
look up the owning module. **No isolation needed** — all modules can stay
open as tabs in one instance.

**Important:** if output shows all content under a single module name (usually
the first `OmlHeader` found), the exe is **stale** — it was built before the
`BuildEspaceMap`/`ResolveModule` code was added. Rebuild with
`.\scripts\Build-All.ps1` or `dotnet publish`.

### Path A — per-module METADATA (always do this first)
- **Tool:** `parse_oml_header` (`outsystems-tools`), one call per `.oml` file
  on disk. Arg: `path`.
- **Returns:** module name, eSpaceKey, IsExtension, SS/platform versions,
  description, saved time, source path. Fully **offline** (reads the file
  header, not the heap) — no Service Studio, no isolation needed.
- **Use:** establish authoritative per-module identity for every module.

### Path B — per-module CONTENT (default, no isolation needed)
- **Condition:** any number of modules open in one or more
  `ServiceStudio.exe` instances. The extractors attribute content per module
  automatically via `ownerESpace`.
- **Tools:** `get_module_report`, `list_entities`, `list_structures`,
  `list_actions`, `list_client_actions`, `list_site_properties`,
  `extract_themes`, `extract_screens`, `extract_web_blocks`,
  `extract_ui_tree`, `extract_client_actions`, `extract_ui`, `live_reader` —
  all with explicit `pid`.
- **Result:** output includes `MODULE: <name>` sections grouping actions,
  entities, structures, site properties, screens, blocks, etc. under their
  correct owning module. UI extraction writes files under
  `docs/<ModuleName>/` automatically.
- **When to isolate anyway:** (a) a referenced-but-not-open module's private
  content is needed — open it directly; (b) heap walk times out with too many
  modules — reduce modules per instance; (c) stale exe — rebuild.

## Quick-start workflows

### Extract a full module report
1. Confirm the module is open and loaded in Service Studio (focus not needed).
2. (If multiple instances open) `get_open_module` → note the target PID.
3. `get_module_report` (with `pid`) → full text report with per-module sections.
4. Persist results under `docs/<ModuleName>/` (gitignored) if needed.

### Extract the UI layer
1. Confirm the module is open and loaded (focus not needed).
2. (If multiple instances open) `get_open_module` → note the target PID.
3. Call the granular `outsystems-ui` tool you need with `pid`:
   - `extract_themes` → `<ThemeName>.css` (one per theme) + `theme-values.json`
   - `extract_screens` / `extract_web_blocks` → `screens.json` / `web-blocks.json`
   - `extract_ui_tree` → widget-tree `.txt` maps
   - `extract_client_actions` → link-traced flows per screen/block
   - `extract_ui` → all of the above + `summary.json`
4. Files land under `docs/<ModuleName>/` as split files (call one tool to get
   just that category — no need to extract the whole module).

### Mutate module state (create/edit elements, flows)
This baseline is **read-only**. To create/edit elements, use the **Editor baseline**
(`AI Outsystems Automation Editor`): live in-process editing (`live_*` tools via the
OsLiveBridge plugin) or headless `.oml` editing (`outsystems-omleditor`). Verify edits
with this baseline's ClrMD tools (`list_entities`, `list_actions`, `get_action_detail`).

## Ground-of-truth reference repo (`references/`)

The toolkit links to a local **OutSystems ground-of-truth repo** (OutSystems'
public repositories collection) for reference material that makes extraction
output interpretable:

- `references/outsystems-ui/patterns.md` — committed pattern catalog
  (CSS classes, CSS API vars, JS API, providers) for identifying OutSystems UI
  patterns in extracted modules; `classic-theme-o11.css` — base O11 theme CSS.
- `references/source-path.txt` — gitignored, machine-specific path to the repo;
  read by `scripts/Build-UiReference.ps1` and the docs skills.
- The `documentation-generation` / `user-guide-generation` skills optionally
  consult the official docs corpus (`docs-product/src`, `docs-howtos/src`)
  inside that repo for terminology grounding.

Setup: `references/README.md`. Regenerate after the ground-of-truth repo
updates: `.\scripts\Build-UiReference.ps1`.

## Official OutSystems MCP server (optional, ODC cloud)

`opencode.json` also declares a disabled placeholder entry `outsystems` — the
**official OutSystems MCP** (`OutSystems/outsystems-mcp`), a remote HTTP server
for **ODC cloud tenants** (edit, publish, deploy apps). It is complementary,
not a replacement: this baseline's 3 ClrMD servers read **OS11 local Service
Studio**; the official server drives an **ODC tenant** over OAuth.

To activate:
1. Replace `YOUR-TENANT.outsystems.dev` with your tenant hostname.
2. Set `"enabled": true`, restart opencode, complete the OAuth sign-in via `/mcp`.
3. First tool call authenticates. If a call fails with `403 tenant_not_allowed`,
   the tenant is not allowlisted for MCP by OutSystems — no local fix; request
   enablement. Do NOT re-register or reinstall to "fix" it.

Read its conventions skill (`Outsystems REPO\outsystems-mcp\SKILL.md` in the
ground-of-truth repo) before driving it — it defines confirm-before-mutate,
polling, and error-category rules that differ from local tooling.

## Agent conventions (adapted from OutSystems/outsystems-mcp)

These conventions are adapted from the official OutSystems MCP conventions doc
(`outsystems-mcp/SKILL.md`) and apply to all OutSystems work in this toolkit,
including the Editor baseline:

- **Confirm before state mutation.** Before any tool that changes state (Editor
  baseline `live_*` operations, file deletes/overwrites outside `docs/`,
  anything touching a module or Service Studio), restate the planned change and
  wait for explicit confirmation. A generic "go ahead" earlier in the
  conversation does not authorize a specific destructive call.
- **Surface stable identifiers.** When reporting on anything you looked up
  (module, entity, action, screen, PID), give the stable identifier alongside
  the display name (module name + eSpaceKey, action name, PID). Names can
  collide; identifiers cannot.
- **Retry on structured signals, not message text.** Scripts and tools here
  report outcomes via exit codes and JSON fields (`Ensure-Built.ps1` exit 1,
  `summary.json`, `css-diag.txt`). Decide retries from those, not by parsing
  prose. Known failure → known fix: stale exe → rebuild gate, missing module →
  open it in Service Studio; never blind-retry a failing extractor.
- **State limits plainly.** If an operation is impossible with available tools
  (e.g. extraction needs the module loaded in Service Studio; ClrMD cannot read
  a module that is only on the recent-projects screen), say so explicitly and
  surface the closest answerable portion — do not silently reroute extraction
  work through ad-hoc tools or invent the missing data.
- **Read live state, don't trust memory.** Tool sets and module state change
  between sessions: check `/mcp` connectivity, `get_open_module`, and
  `Ensure-Built.ps1` freshness before extraction rather than assuming.

## Error handling / troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| MCP servers inactive in `/mcp` after clone | `.exe` files not built (gitignored) | Run `.\scripts\Build-All.ps1`; restart opencode from repo root. If a runtime is missing, use `-SelfContained`. |
| `Verify-Project.ps1` prints `MISSING:` | Toolkit incomplete (missing skill/MCP/script/exe) | Re-clone the full repo or re-scaffold with `New-OutSystemsProject.ps1`. Never fix by selective file writing. |
| `dotnet publish` says SDK missing | .NET 8+ SDK not installed | Install the .NET 8+ SDK (`dotnet --list-sdks` must show `8.0.x`+). |
| Extractor returns "Service Studio is not running" | SS not running, or module only on recent-projects screen | Open the module in SS and wait until it finishes loading. |
| `get_open_module` shows "no AutoSave file" for a module | Module just opened and never saved | Module is still extractable. Find its PID via Task Manager / `Get-Process ServiceStudio` and pass `pid` to the extractor; the module name appears in output. |
| Extractor output label is the wrong module / counts are too high / paths nested | Stale exe — source newer than exe (the `ownerESpace` attribution code is in source but not compiled into the running exe) | Run `.\scripts\Ensure-Built.ps1`. If `STALE-LOCKED`, close opencode, re-run, restart. See "Multi-module targeting" below. |
| `Ensure-Built.ps1` exits 1 (`STALE-LOCKED`) | opencode is running and holding the publish DLLs open | Close opencode, run `.\scripts\Ensure-Built.ps1` in a terminal, restart opencode. Or just use the Start Menu shortcut (runs `start-opencode.ps1` which handles this automatically). |
| Extractor times out on a large module set | Heap walk too large (many modules in one process) | Isolate: open fewer modules per instance, or extract one at a time. |
| Referenced module shows only public elements | It is referenced but not open | Open that module directly and extract it for its private content. |
| `Build-All.ps1` warns "locked DLLs" | opencode is running and holding the exes open | Close opencode, re-run `Build-All.ps1` for a clean rebuild. Existing exes remain valid. |
| Theme CSS truncated mid-statement (e.g. `color: var(--color-n`) | ClrMD 3.0 `ReadString` truncates strings ≥ 4096 chars; the `ReadStrManual` fix is in source but not compiled into the running exe | Rebuild the `outsystems-ui` exe: close opencode, run `.\scripts\Ensure-Built.ps1`, restart. Check `css-diag.txt` for `ReadStr: ClrMD truncated` confirmation lines. |
| `Build-UiReference.ps1` exits 1 ("no source repo configured" / "not found") | `references/source-path.txt` missing or pointing at a moved repo | Create `references/source-path.txt` with one line (absolute path to `Outsystems REPO`), or call the script with `-SourceRepo "C:\path\to\Outsystems REPO"`. |
| Official `outsystems` MCP server fails with `403 tenant_not_allowed` | Server-side per-tenant allowlist by OutSystems | No local fix; do not re-register/reinstall. Request tenant enablement from OutSystems or your OutSystems contact. |
| Official `outsystems` MCP server fails to authenticate | OAuth flow not completed, or placeholder tenant still configured | Replace `YOUR-TENANT.outsystems.dev` in `opencode.json`, restart opencode, run `/mcp` and complete sign-in. See "Official OutSystems MCP server" above. |

## Creating derivative projects

When asked to create a new/derivative project from this toolkit (e.g.
"generate ArkkiAutomation from this repo"), **do not write files selectively
from memory** — that is how pieces silently go missing (skills, an MCP server,
schemas, scripts, tools). Use one of these instead, in order of preference:

1. **`git clone`** the full repo, then rename/edit. This is the only method
   that guarantees every tracked file is present. After cloning:
   ```powershell
   .\scripts\Build-All.ps1        # builds the 3 gitignored MCP exes
   .\scripts\Verify-Project.ps1   # hard-fail gate; MUST pass before use
   ```
2. **`git archive`** into a new folder if you want a clean copy without the
   `.git` history, then `Build-All.ps1` + `Verify-Project.ps1`.
3. **`scripts\New-OutSystemsProject.ps1 -Target <dir>`** only when a clone is
   not possible. This copies all 3 MCP source projects, all skills (auto-discovered),
   `schemas/`, `scripts/`, `tools/`, and the root files, then runs
   `Verify-Project.ps1 -SourceOnly`.

**Hard rule:** after scaffolding, `scripts\Verify-Project.ps1` MUST exit 0
before the project is considered usable. If it fails, ABORT and fix the
missing pieces — do not proceed and do not try to work around them. The gate
checks: skills (auto-discovered; every `.opencode/skills/*` dir needs SKILL.md), 3 MCP source projects, schemas/scripts/tools, root files,
`opencode.json` lists all 3 servers with `enabled: true` and `publish/` paths,
and the 3 built exes exist.

**Why this section exists:** a derivative project created by selective file
writing can silently drop pieces (a skill, an MCP server, `schemas/`, `scripts/`,
`tools/`). The verify gate makes that failure mode impossible by construction.

**Build artifacts are gitignored:** `bin/`, `obj/`, and `publish/` are not in
git, so every fresh clone/derivative MUST run `Build-All.ps1` to produce the
MCP exes. `opencode.json` points at `./mcp/<server>/publish/<exe>` for all three
servers (normalized). Do not commit the exes.

## Linked consumer projects (thin reference, no copy)

The methods above all copy or clone the **full** toolkit into the new project.
Use a **linked consumer** when the baseline toolkit already lives on this
machine and you want a thin per-app project that references it in place - no
MCP source, skills, schemas, scripts, or tools are copied. The consumer holds
only app-specific content (OMLs, hand-written docs, notes); `opencode.json`
points at the baseline's exes and skills via absolute paths. When the baseline
is updated or its exes are rebuilt, the consumer picks up the changes
immediately (no sync, no copy).

```powershell
.\scripts\New-LinkedProject.ps1 -Target "C:\Users\me\Documents\ArkkiAutomation" -AppName "Arkki"
```

Generates: `opencode.json` (absolute paths into the baseline), a consumer
`.gitignore`, `open/.gitkeep`, an `OMLs/` folder, and an `<AppName>.md` readme.
Does NOT create `docs/` (extraction tools create `docs/<Module>/` on demand)
and does NOT run `git init`. The script ends with a readiness check that
confirms the three baseline exes and skills exist; if exes are missing,
run `.\scripts\Build-All.ps1` in the **baseline** first (the consumer cannot
build them). Then:

```powershell
cd C:\Users\me\Documents\ArkkiAutomation
opencode          # launch from the new project root; /mcp must show 3 connected
```

**When to use which:**

| Method | Copies toolkit? | Use when |
|--------|-----------------|----------|
| `git clone` + rename | Full copy (self-sufficient) | Starting an independent fork with its own toolkit |
| `New-OutSystemsProject.ps1` | Full copy (self-sufficient) | Clone not possible; need a standalone project |
| `New-LinkedProject.ps1` | None (references baseline) | Baseline already on this machine; want a thin per-app project |

**Hard rule still applies:** the baseline must be complete and built
(`Verify-Project.ps1` exits 0) before the consumer is usable. The consumer's
readiness check verifies this at scaffold time.

## Pointers

- **First-time setup**: `SETUP.md` (build + verify + launch on a new PC).
- **Deep reference**: `docs/project_map.md` (source of truth for architecture,
  node detection, flow tracing, expression types, UI extraction model coverage).
- **Per-skill triggers**: `.opencode/skills/*/SKILL.md` (7 skills, auto-discovered).
- **Editing**: lives in the `AI Outsystems Automation Editor` baseline
  (live in-process + headless `.oml`); see that project's `live-editing` skill.
- **MCP wiring**: `opencode.json` (server commands, skills path).
- **Schemas**: `schemas/metadata.schema.json`, `schemas/client-actions.schema.json`.
- **Ground-of-truth references**: `references/README.md` (pattern catalog, classic theme CSS, docs corpus).
- **Standalone tools**: `tools/` (UiExtractor, LiveReader, ModuleInfoExtractor,
  UiProbe, ThemeProbe, probes) — CLIs usable outside the MCP layer.
