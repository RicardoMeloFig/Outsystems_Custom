# AGENTS.md

Canonical entry point AND runbook for any AI agent working in this repo. Read
this **first**, before touching tools or skill files. It is written to be
followed step-by-step.

## Project overview

The **editor** baseline for OutSystems 11 — the sole place modules get mutated.
It edits **live-first**: **live in-process** (`outsystems-liveeditor` via the
OsLiveBridge plugin, open module) is the default; **headless `.oml` files**
(`outsystems-omleditor`, no SS running) are used **only when the user
explicitly requests headless/no-SS/file editing**. No Service Studio UI, no
`productKey`. This is the UI-free counterpart to the first baseline
(`AI Outsystem Automation`), which is now **extraction-only** (ClrMD readers + docs).

- **Two MCP servers** (`outsystems-omleditor` + `outsystems-liveeditor`, declared in
  `opencode.json`) + **26 skills** (`.opencode/skills/`, auto-discovered).
- The headless server is `net8.0`, hand-rolled JSON-RPC/stdio. It loads the SS model
  DLLs from the SS install dir via `Assembly.LoadFrom` at runtime — **SS 11 must be
  installed** (for the DLLs) but **need not be running**. The live server is a thin
  stdio→named-pipe forwarder to the OsLiveBridge plugin running **inside** SS (so SS
  must be running with the plugin + module open).

| Server | Backend | Purpose |
|--------|---------|---------|
| `outsystems-omleditor` | SS model DLLs (`Oml.LoadWithoutUpgrades`) | Edit `.oml` **files** headlessly: probe fragments, create service actions, add dependencies, regen signatures. No UI, no running SS. |
| `outsystems-liveeditor` | OsLiveBridge plugin (named pipe) in a **running** SS | Edit the **open** module live: create/clone service actions, edit flows (assign/output/node), manage dependencies (consume all 15 element types from a producer). All land in the SS tree immediately (no reload), as real undo units. Requires SS running + plugin + module open. |

The 26 skills (see `.opencode/skills/` for the full list):
- **Proven (headless):** `editor-workflow` (the breakthrough meta-skill),
  `outsystems-omleditor` (tool guide), `creating-service-actions`,
  `managing-dependencies` (live + headless), `oml-editing-reference` (deep reference)
- **Proven (live):** `live-editing` (the command-system breakthrough + bridge
  mechanism/reference + dependency management), `outsystems-liveeditor` (live tool guide),
  `creating-service-actions-live`, `exception-handlers-in-service-actions`,
  `building-service-action-flows`, `styling-and-css-live` (widget Style Classes + theme
  CSS so it shows in SS's editor), `building-screens-and-buttons` (web screens +
  placeholders + real Buttons + screen Client Actions + OnClick wiring)
- **Roadmap stubs:** `creating-entities`, `adding-attributes`, `deleting-elements`,
  `creating-crud-wrappers`

## The breakthrough (in one paragraph)

`Oml.LoadWithoutUpgrades(bytes, "")` loads any `.oml` (empty product key is fine).
`GetFragmentXmlReader/Writer` read/write fragments — and the writer **creates new
fragments** on write. `SetNeedsSignatureRegeneration()` + `GetBytes()` produces a
valid `.oml` (`IsValidOml=True`); signatures are **content hashes**, not key-crypto,
so they regenerate without any key. Proven: add element, create fragment,
clone-with-key-remap. A headlessly-created Service Action opens correctly in SS.

## The file-vs-live constraint (critical)

**Editing is live-first.** The default is the live in-process editor
(`outsystems-liveeditor`, module open in SS, instant tree updates, undo
history). Headless `.oml` editing below is used **only when the user
explicitly requests it** ("edit the .oml file", "no SS", "headless") — never
switch to headless on your own initiative.

Headless canonical workflow (on explicit request):
1. **Save** the module in SS (`Ctrl+S`) → `.oml` on disk. (Use
   `scripts\Get-OmlPath.ps1 <Module>` to find it.)
2. **Edit headlessly** — call an `outsystems-omleditor` tool with `omlPath` +
   `outOml` (write to a **new** file; SS locks the source while open).
3. **Reload in SS** — close the module, open `outOml`.
4. **Verify** — the tool self-verifies offline (`IsValidOml` + read-back).

Live in-memory editing of the open module is **proven** (not future): the
`outsystems-liveeditor` MCP + the OsLiveBridge plugin mutate the open module via the
SS command system (`Command.ExecuteFromAsyncCode`), so changes appear in the live
tree immediately with no reload. This covers service actions, flow editing, AND
dependency management (consuming all 15 element types from a producer module via
`IESpace.AddDependency`). See the `live-editing` skill. Use live by default when
the module is open; use headless only when the user explicitly asked for it.

## Agent conventions (behavioral rules for every edit session)

Modeled on the official OutSystems MCP conventions doc
(`Outsystems REPO\outsystems-mcp\SKILL.md`). These bind all editing work:

- **Never guess opaque keys.** Module names, element names, node indices, entity
  Ids — resolve them with the read/probe tools first; ask the user when a lookup
  fails. An index from memory of a previous step is stale; re-probe.
- **Succeeded ≠ landed.** A tool returning ok is not proof. Re-read the object
  (`live_list_flow`, `live_probe_*`, `get_verify_errors`,
  `live_debug_eSpace_collection_items`) before declaring done. Verify
  **values**, not just absence of errors.
- **Confirm before destructive tenant-state changes.** Destructive = overwriting
  the source `.oml` in place, deleting elements/widgets/screens/flows
  (`live_delete_*`), deleting aggregates/data actions, and any publish. Restate
  the planned change and wait for explicit confirmation — a generic "go ahead"
  for a task does not authorize a specific destructive call. Read-only probes
  and additions are exempt.
- **Error categories drive retry decisions.** Bridge/model errors ("property not
  settable", "No command is open") mean WRONG SURFACE — fix the surface, don't
  retry. Verify (TrueChange) errors mean WRONG CONTENT — fix the expression/
  element. Timeouts/restarts mean LOST STATE — re-probe before continuing.
  Never blindly retry a failed mutation: failed calls may have half-landed
  (run `live_list_flow` first).
- **Long operations are poll-and-report.** After a save/publish/refresh kick-off,
  give the user one short status line per poll; only the operation's own
  terminal status counts as done.

## Prerequisites (confirm before any tool call)

### On the machine
- **Git** — https://git-scm.com
- **.NET 8+ SDK** — `dotnet --list-sdks` must show an `8.0.x` (or newer) entry.
  (The editor is `net8.0`; .NET 10 also works.)
- **opencode CLI** — https://opencode.ai
- **OutSystems Service Studio 11** — **installed** (the editor loads its model
  DLLs from `C:\Program Files\OutSystems\Service Studio 11\Service Studio\`).
  SS does **not** need to be running to edit `.oml` files. (Override the install
  dir with the `OSSS_DIR` env var.)
- **Windows x64**.

### For every edit
- The `.oml` to edit is on disk and reflects the last **Save** in SS. Have the
  user `Ctrl+S` first if they just changed the module.
- An `outOml` output path that is **not** the file SS has open (write to a new
  file; the user reloads it in SS).

## Which tool do I need?

| Goal | Tool |
|------|------|
| Inspect a module before editing (fragments, validity) | `probe_oml` |
| Read one fragment's XML | `get_fragment` |
| Find which fragment holds an element/name/key | `scan_oml` |
| Create a Service Action (clone or from-scratch) — **headless, file** | `create_service_action` |
| Add a module dependency (Reference) — headless, file | `add_dependency` |
| Round-trip / regen signatures on a .oml | `regen_and_write` |
| Check the live bridge (SS running? module open?) | `live_status` |
| List modules OPEN in SS (live) | `live_list_modules` |
| Read an open module's live counts | `live_module_info` |
| Create a Service Action in the **open** module (live, no reload) | `live_create_service_action` |
| Create a Server Action in the **open** module (live, no reload) | `live_create_server_action` |
| Create a Client Action in the **open** module (live, no reload) | `live_create_client_action` |
| Deep-clone a Service Action in the **open** module (exact copy, live) | `live_clone_service_action` |
| Deep-clone a Server Action / Client Action in the **open** module (exact copy, live) | `live_clone_server_action`, `live_clone_client_action` |
| Inspect a service action's flow graph (nodes/links, live, read-only) | `live_list_flow` |
| Change an Assign value / add output / add Assign node / delete node (live, direct — no restart) | `live_set_assign_value`, `live_add_output_param`, `live_add_assign_node`, `live_delete_node` |
| Add an input parameter (basic, entity, or structure type) | `live_add_input_param`, `live_add_entity_input` |
| Add an output parameter (basic, entity, or structure type) | `live_add_output_param`, `live_add_entity_output`, `live_set_output_param_type` |
| Add a local variable to an action | `live_add_local_variable` |
| List consumable elements in a producer module (live, read-only) | `live_list_consumable_elements` |
| Consume elements from a producer into a consumer (live, all 15 types, single undo unit) | `live_consume_elements` |
| Remove a module dependency (reference to a producer) live, undo unit | `live_remove_dependency` |
| 1-Click Publish the OPEN module in-process (F5-equivalent, no UI automation) | `live_publish_module` (ok:false = async start; poll `live_debug_publish_state`), `live_debug_publish_surface` (surface probe) |
| Add a 2nd assignment to an existing Assign node (multi-assignment) | `live_add_assignment_to_node` |
| Set Exception=All Exceptions on an ErrorHandler node | `live_set_error_handler_exception` |
| Auto-map ExecuteAction input arguments by name | `live_map_action_inputs` |
| Inspect a node's type and settable properties | `live_debug_node_props` |
| Inspect ExecuteAction arguments | `live_debug_action_args` |
| Add a container/text/link/expression/html element (live) | `live_add_container`, `live_add_text`, `live_add_link`, `live_add_expression`, `live_add_html_element` |
| Add a REAL Button (plugin CustomWidget, live) | `live_add_button` |
| Create a SCREEN-level Client Action (what a Button OnClick accepts) | `live_create_screen_client_action` |
| Wire a Button's OnClick to a Client Action (live) | `live_set_button_onclick` |
| Web screens/flows + layout placeholders (live) | `live_create_web_screen`, `live_list_web_flows`, `live_add_to_placeholder`, `live_list_placeholders`, `live_delete_from_placeholder`, `live_list_widgets` |
| Web block from scratch in a web flow (NO clone/move, live) | `live_create_web_block` |
| Add any widget to a web block (container/text/expression/link/html/placeholder) | `live_add_widget_to_block`, `live_add_placeholder_to_block`, `live_add_link_to_block` |
| Add a Local Variable to a web block (live) | `live_add_variable_to_block` |
| Set a string property / Expression value on a web block widget (live) | `live_set_block_widget_property` |
| Delete a widget from a web block / inspect its concrete types (live) | `live_delete_widget_from_block`, `live_list_block_widgets`, `live_dump_block_widget_types` |
| Set a widget's CSS class (container→Style CP, link/text→CustomStyle) | `live_set_style_class` |
| Write the theme CSS so SS's CSS editor shows it (user CSS) | `live_set_user_css`, `live_set_module_css` |
| Verify a container class landed (Style CP = ParsedExpression) | `live_probe_style_prop` |
| Verify the theme CSS source fields | `live_read_theme_css`, `live_probe_sheet` |

## Canonical runbook: edit a module headlessly (ONLY on explicit user request)

1. **Find + save the .oml.** In SS, open the module and `Ctrl+S`. Run
   `scripts\Get-OmlPath.ps1 <Module>` to locate the `.oml`. Note its path.
2. **Inspect.** `probe_oml(omlPath)` → learn the fragments (does it have
   `ServiceAPIMethods`? which flow fragments?).
3. **Edit.** Call the right tool with `omlPath` + `outOml` (new file):
   - Service action → `create_service_action(omlPath, outOml, name, templateName?)`
   - Dependency → `add_dependency(omlPath, outOml, referenceXml)` (get the
     `<Reference>` XML first via `get_fragment` on a module that already consumes
     the producer).
4. **Trust the read-back.** Every write tool reloads the output and reports
   `IsValidOml` + a read-back of the new element. If it says `FAIL:`, the edit did
   not land — re-`probe_oml` and retry; do not proceed.
5. **Reload in SS.** Either tell the user to close the module and open `outOml`,
   or run `scripts\Open-OmlInSS.ps1 -OmlPath <outOml>` (Service Studio CLI:
   `servicestudio.exe <outOml>`). The new element appears in the tree.

## Canonical runbook: build a service action flow (live)

> **⚠ CRITICAL: Follow steps 4-11 in strict order.** Each step depends on the
> previous. SKIPPING A STEP WILL CAUSE FAILURES. If a step fails, run
> `live_list_flow` to check state — failed calls may still create nodes. See the
> `building-service-action-flows` skill for anti-patterns, recovery guide, and a
> complete worked example.

1. **Consume dependencies** — `live_consume_elements` (all 15 element types from a
   producer, single undo unit).
2. **Create action** — `live_create_service_action` (creates an empty action with a
   Comment placeholder, NOT a Start→End flow).
3. **Add parameters** — `live_add_entity_input` for entity-typed inputs;
   `live_add_output_param` + `live_set_output_param_type` for typed outputs;
   `live_add_local_variable` for local variables.
4. **Create flow nodes** — `live_debug_create_node` for Start, End, Assign, and
   ErrorHandler nodes (all created standalone, then linked).
   **VERIFY:** `live_list_flow` after creating Start+End, then link them with
   `live_set_node_target(startIdx, endIdx)` before proceeding to Step 5.
5. **Add ExecuteAction** — `live_add_action_call_node` (creates and configures the
   server-action call node in one step). **REQUIRES** Start→End linked (Step 4).
6. **Identify nodes** — `live_debug_node_props` (node indices are NOT creation order;
   always probe before linking).
7. **Link nodes** — `live_set_node_target` (deterministic wiring, no known bugs).
8. **Add assignments** — `live_add_assignment_to_node` (supports multi-assignment —
   multiple var=value pairs on a single Assign node).
9. **Set exception handler** — `live_set_error_handler_exception` (sets
   Exception=All Exceptions on the ErrorHandler node). **MUST be done BEFORE**
   adding `AllExceptions.ExceptionMessage` assignment.
10. **Map inputs** — `live_map_action_inputs` (auto-maps ExecuteAction input arguments
    by name, with fallback).
11. **Verify** — `live_list_flow` (read-back the full node graph: indices, types,
    assignments, targets).
12. **Save in SS** (`Ctrl+S`) to persist the live changes to disk.

## Setup (fresh clone)

1. `git clone` (or copy) this repo; `cd` into the root.
2. `.\scripts\Build-All.ps1` — builds the editor exes into
   `mcp\<server>\publish\`. (Self-contained win-x64 by default — the exes
   run with no .NET runtime installed.)
3. `.\scripts\Verify-Project.ps1` — hard-fail integrity gate. **Must print `OK:`
   and exit 0.** If it lists `MISSING:`, stop and fix.
4. Launch opencode **from the repo root**: `opencode` (or
   `.\scripts\start-opencode.ps1`, which runs the staleness gate first).
5. Inside opencode, `/mcp` — `outsystems-omleditor` must show active/connected.

### Staleness gate
After any MCP source edit, the built exe goes stale. `scripts\start-opencode.ps1`
runs `Ensure-Built.ps1` automatically (rebuilds before launching, while DLLs are
not locked). If launched directly, run `.\scripts\Ensure-Built.ps1` first. Exit 1
(`STALE-LOCKED`) means opencode is running — close it, re-run, restart.

## Relationship to the first baseline

| Baseline | Focus |
|---|---|
| `AI Outsystem Automation` (first) | **Extraction-only** (ClrMD readers) + documentation |
| `AI Outsystems Automation Editor` (this) | **Editing** — headless `.oml` + live in-process |

The two are **independent**. This project is **editor-only** and verifies edits
**offline** (no ClrMD, no running SS). Consumers that need both reading and
editing link **both** baselines via absolute paths.

## Error handling / troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| MCP server inactive in `/mcp` after clone | exe not built (gitignored) | `.\scripts\Build-All.ps1`; restart opencode from the repo root. |
| `Verify-Project.ps1` prints `MISSING:` | toolkit incomplete | re-clone/copy the full repo; do not fix by selective file writing. |
| Tool error "type not found" / assembly load | SS 11 not installed (DLLs missing) | install SS 11, or set `OSSS_DIR` to the install dir. |
| Tool error `MissingFragmentException` | read a fragment that doesn't exist | `HasFragment` first; `probe_oml` to list fragments. |
| `IsValidOml=False` after a write | edit produced an invalid .oml | do not open in SS; re-`probe_oml`, fix the fragment edit, retry. |
| SS can't open the edited .oml | rare — a stricter check than `IsValidOml` | report the exact SS error; see `oml-editing-reference` gotchas (e.g. `Count` attr, missing companion fragment). |
| `Ensure-Built.ps1` exits 1 (`STALE-LOCKED`) | opencode holding DLLs open | close opencode, re-run, restart. |
| Edit "succeeds" but element absent after reload | fragment write didn't apply | re-`probe_oml`; ensure you wrote the right fragment; re-run. |
| Container CSS class "disappears" after save/reload | class on `CustomStyle` of a container (Style CP null) | re-apply via `live_set_style_class` (container→Style CP); `live_probe_style_prop` to confirm ParsedExpression. |
| Page styled but theme CSS editor empty | only `_cssSource` written; `_userCssSource` null | `live_set_user_css` / `live_set_module_css` (SetUserCssSource); verify with `live_read_theme_css`. |
| `...requires an element of type 'String', but... 'Object'` | PowerShell `ConvertTo-Json` wrapped long CSS | use `scripts/Send-BridgeCmd.ps1` or `JavaScriptSerializer` + concat (MCP tools are safe). |
| Corrupted .oml / SS crashes on one module | rare file corruption | `servicestudio.exe -recover <module.oml>` (wraps `scripts\Open-OmlInSS.ps1 -Recover`); refresh references via `-refresh` (see `editor-workflow`). |
| Stale references after a headless `add_dependency` | producer changed on the server | `scripts\Open-OmlInSS.ps1 -Refresh -HostName <env> -VerifyXml <path>` (SS CLI `-refresh` + verify log). |

## Creating derivative / consumer projects

This baseline is meant to be **referenced** (linked) by per-app consumer
projects alongside the first (extraction) baseline — not copied. A consumer's
`opencode.json` points at **both** baselines' exes/skills via absolute paths:
the first for `outsystems-tools/logic/ui` (extraction), this for
`outsystems-omleditor` (editing). See the first baseline's `New-LinkedProject.ps1`
for the linked-consumer pattern.

## Pointers
- **First-time setup**: `SETUP.md`.
- **Deep reference**: `docs/oml-editing-reference.md` + the `oml-editing-reference` skill;
  `docs/element-class-reference.md` (O11 element semantics mapped to our tools);
  `docs/ui-styling-reference.md` (live CSS + the OutSystemsUI `--osui-*` override
  hierarchy); `docs/web-block-editing.md` (live web blocks).
- **Per-skill triggers**: `.opencode/skills/*/SKILL.md` (26 skills, auto-discovered;
  `scripts\Test-SkillLockstep.ps1` checks their structure/conventions).
- **MCP wiring**: `opencode.json`.
- **Standalone scripts**: `scripts\` (Build-All, Verify-Project, Ensure-Built,
  start-opencode, Get-OmlPath, Parse-OmlHeader, Send-BridgeCmd, Open-OmlInSS,
  Get-OsuiVars, Test-SkillLockstep).
- **Ground-of-truth repos** (`Outsystems REPO\`): `docs-product` (official O11
  docs — CC BY-NC-ND 4.0: summarize + attribute, never bulk-copy),
  `outsystems-ui` (UI framework source + CSS architecture),
  `outsystems-mcp` (official MCP conventions doc), `outsystems-pipeline`
  (LifeTime REST v2 — future deploy tooling).
