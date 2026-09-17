# Project Map — AI Outsystems Automation Editor

The **editor** baseline — the sole place OutSystems 11 modules get mutated. Two
ways: headless `.oml` files (`outsystems-omleditor`, no SS running) and live
in-process (`outsystems-liveeditor` via the OsLiveBridge plugin, open module).
No Service Studio UI, no `productKey`. The counterpart to the first baseline
(`AI Outsystem Automation`), which is now **extraction-only** (ClrMD readers).

## Relationship to the first baseline

| Baseline | Focus | Editor |
|---|---|---|
| `AI Outsystem Automation` (first) | **Extraction-only** (ClrMD) + documentation | — (editing removed) |
| `AI Outsystems Automation Editor` (this) | **Editing** | `outsystems-omleditor` (file) + `outsystems-liveeditor` (live) |

The two are **independent**. This project is **editor-only**: two MCP servers
(`outsystems-omleditor` — headless files; `outsystems-liveeditor` — live open
module) + editor skills. The headless path verifies edits **offline** (reload
`.oml` + `IsValidOml`); the live path mutates the open module in a running SS via
the OsLiveBridge plugin. Consumers that need both reading and editing link **both**
baselines via absolute paths.

## Components

### MCP server — `outsystems-omleditor` (`mcp/outsystems-omleditor/`)
- `net8.0`, hand-rolled JSON-RPC/stdio. No FlaUI/WinForms.
- Loads SS model DLLs from the SS install dir via `Assembly.LoadFrom` (SS 11
  installed; not running).
- Backend `OmlEditor.cs` ports the proven `OmlEdit` logic: `LoadOml`,
  `WriteFragment`, `NewKey`, `Probe`, `GetFragment`, `Scan`,
  `CreateServiceAction` (clone + from-scratch + flow key-remap), `AddDependency`,
  `RegenAndWrite`, `IsValidOml`, `HeaderValid`.
- Tools: `probe_oml`, `get_fragment`, `scan_oml`, `create_service_action`,
  `add_dependency`, `regen_and_write`. Every write tool self-verifies.

### MCP server — `outsystems-liveeditor` (`mcp/outsystems-liveeditor/`)
- `net8.0`, hand-rolled JSON-RPC/stdio. A thin forwarder to the OsLiveBridge named
  pipe (`OsLiveBridge-<SSpid>`) running inside a **running** SS. Requires SS + the
  plugin + the module open.
- Tools (126, all `live_*`): flow building (create/clone server/server/client/screen-client
  actions, folders, params, local vars, assign/call/end/error-handler/if/switch nodes,
  node targets/positions/layout, action-input mapping), dependency management
  (`live_list_consumable_elements`, `live_consume_elements`), screens & widgets
  (container/text/expression/link/html/button/link-to-block/placeholder, style classes,
  screen layout/title/theme), web blocks (create/clone/move, block widgets/buttons/
  input-params/events/handlers, aggregates & data actions), and read-only probes.
- New flow-building bridge commands (proven): `live_add_assignment_to_node`
  (add a 2nd assignment to an existing Assign node — multi-assignment per node),
  `live_set_error_handler_exception` (set Exception=All Exceptions on an ErrorHandler
  node), `live_map_action_inputs` (auto-map ExecuteAction input arguments by name with
  fallback), `live_debug_node_props` (inspect a node's type and settable properties),
  `live_debug_action_args` (inspect ExecuteAction arguments).
- Mutations use `Command.ExecuteFromAsyncCode` (real SS command → undo unit) so they
  land in the live tree immediately, no reload.
- Dependency management (`live_list_consumable_elements`, `live_consume_elements`)
  uses `IESpace.AddDependency<T,S>` — covers all 15 consumable element types
  (server actions, entities, structures, roles, etc.). Proven: 24 elements consumed
  from `Diet_CS` into `Diet_BL` in a single undo unit.
- Styling & theme CSS (proven on FitnessManager): `live_set_style_class` (container
  class → `Style` CustomProperty `ParsedExpression` via `SetValueExpression`, clears
  stray `CustomStyle`; links/text → `CustomStyle`), `live_set_user_css` /
  `live_set_module_css` (repointed) → public `WebStyleSheet.SetUserCssSource` (the
  source SS's theme CSS editor reads — `_cssSource` only renders, editor stays empty),
  diagnostics `live_probe_style_prop`, `live_read_theme_css`, `live_probe_sheet`.
  Deep reference: `docs/ui-styling-reference.md`.
- Web blocks (proven on FitnessManager → `ZombieHeader` in `zombieGame`):
  `live_create_web_block` (from scratch via `IUIFlow.CreateBlock`, NO clone/move),
  `live_add_widget_to_block` / `live_add_placeholder_to_block`,
  `live_add_variable_to_block` (`WebBlock.CreateLocalVariable`),
  `live_set_block_widget_property`, `live_delete_widget_from_block`,
  `live_dump_block_widget_types` (concrete type + interfaces incl. anonymous).
  **Buttons in blocks:** `live_add_button_to_block` / `live_set_block_button_onclick`
  (same NRWidgets ButtonDescriptor mechanism as screens, resolved via `FindBlock`).
  **Block input parameters:** `live_add_input_param_to_block` /
  `live_set_block_input_param_type` / `live_remove_input_param_from_block` /
  `live_add_entity_input_to_block` / `live_add_entity_identifier_input_to_block`.
  **Decision nodes:** `live_add_if_node` (`IIfNode.SetCondition`),
  `live_add_switch_node`, `live_probe_node_connectors` (If → TrueTarget/FalseTarget,
  Switch → OtherwiseTarget), `live_set_connector_target`.
  Gotcha: a real Reactive **Expression** is `ServiceStudio.Plugin.NRWidgets.Expression`
  (via interface `...NRWidgets.IExpression`); the old fallback creates a plain `[Text]`.
  **Data fetching (Phase 4, proven):** `live_probe_data_sources`, `live_add_aggregate_to_block/screen`
  (`CreateScreenAggregate` → `WebScreenDataSet`), `live_add_data_action_to_block/screen`
  (`CreateDataAction` → `DataScreenActionFlow`), `live_delete_aggregate` / `live_delete_data_action`.
  **Any Reactive widget (Phase 5, proven, 19 kinds):** `live_add_nr_widget`
  (`<Kind>+Kind.Instance.Descriptor` → `CreateWidget(descriptor)`; label/input/textarea/checkbox/
  dropdown/radio/radio-group/switch/list/table/image/icon/form/button-group + container/expression/
  link/html), `live_probe_widget_kinds`, `live_set_widget_handler` (OnChange/OnFocus/OnClick → client action),
  `live_set_element_description` (writable Description on block/screen/action).
  **Exposed bridge commands (Part A):** `live_set_extended_property` (html attrs), `live_set_screen_layout`,
  `live_delete_web_flow`, `live_delete_screen_from_flow`, `live_delete_widget`, `live_delete_layout`,
  `live_add_html_text`, probes (`live_probe_widget_deep`, `live_probe_layout_ref`, `live_read_user_css_text`,
  `live_probe_block_widget`, `live_dump_widget_concretes`).
  Decision-node layout: `live_layout_flow` now lays out If (TrueTarget/FalseTarget) and Switch
  (OtherwiseTarget) branches into their own columns.
  Deep reference: `docs/web-block-editing.md`.
- Auto-discovers the main IDE SS pid per module (SS is multi-process).

### Live bridge — `bridge/OsLiveBridge/`
- The SS plugin (`ServiceStudio.Plugin.OsLiveBridge.dll`, net8.0 class lib) dropped
  into `Plugins\ServiceStudio\`. `[ModuleInitializer]` starts a named-pipe server at
  SS startup. `BridgeHost.cs` has all commands + reflection helpers.
- `bridge/HeapProbe/` — ClrMD read-only probe (verify live state without the pipe).
- `tools/CommandProbe/` — Mono.Cecil static analyzer (found the command-opener);
  `tools/LiveModelProbe/` — reflection probe over the SS DLLs.

### Skills (`.opencode/skills/`, 15)
- **Proven (headless):** `editor-workflow` (meta), `outsystems-omleditor` (tool guide),
  `creating-service-actions`, `managing-dependencies`, `oml-editing-reference`.
- **Proven (live):** `live-editing` (command-system breakthrough + bridge),
  `outsystems-liveeditor` (live tool guide), `creating-service-actions-live`,
  `exception-handlers-in-service-actions`, `building-service-action-flows`,
  `styling-and-css-live` (widget Style Classes + theme CSS so it shows in SS's editor).
- **Roadmap stubs:** `creating-entities`, `adding-attributes`, `deleting-elements`,
  `creating-crud-wrappers`.

### Scripts (`scripts/`)
`Build-All.ps1` (builds the editor exes), `Verify-Project.ps1` (integrity gate),
`Ensure-Built.ps1` (staleness gate), `start-opencode.ps1`, `Get-OmlPath.ps1`
(find a module's `.oml`; SS-install check + AutoSave discovery),
`Parse-OmlHeader.ps1` (offline header read), `Send-BridgeCmd.ps1` (raw bridge
pipe with safe JSON), `Open-OmlInSS.ps1` (SS CLI wrapper: open/`-diff`/`-merge`/
`-recover`/`-refresh`), `Get-OsuiVars.ps1` (list `--osui-*` component variables
from a compiled OutSystemsUI bundle), `Test-SkillLockstep.ps1` (skill structure +
convention + phrase-lockstep checks).

### Docs (`docs/`)
`oml-editing-reference.md` (this breakthrough, deep reference),
`ui-styling-reference.md` (live CSS surfaces + style-class placement),
`web-block-editing.md` (live web-block tools + Expression-vs-Text pitfall + deploy),
`element-class-reference.md` (O11 element semantics/defaults mapped to our live
tools; condensed from the official docs-product class-*.md pages, CC BY-NC-ND),
`project_map.md` (this file).

## The breakthroughs (summary)

### Headless .oml editing (proven)
- `Oml.LoadWithoutUpgrades(bytes, "")` loads any `.oml` (empty product key OK).
- `GetFragmentXmlWriter(string).Write(xe)` reads/writes **and creates new
  fragments**.
- `SetNeedsSignatureRegeneration()` + `GetBytes()` → valid `.oml`
  (`IsValidOml=True`). Signatures are **content hashes**, not key-crypto.
- Proven: add element, create fragment, clone-with-key-remap, from-scratch
  element. Confirmed in SS.

### Live in-process editing (proven)
- The SS model refuses mutation unless a **command** (undo unit) is open. The
  opener is `ServiceStudio.Commands.Command.Execute(presenterContext, desc, action)`
  → `UndoManager.InnerStart` adds a `CommandData` to the static
  `UndoManager.currentCommands[aggregator]`. (`ExecuteInContext` does NOT open a
  command — that was the blocker.)
- `PresenterContext` = `new PresenterContext(aggregator, aggregator)`,
  `aggregator` = `PluginProvider.GetContext(espace)`.
- The OsLiveBridge plugin (named pipe) + the `outsystems-liveeditor` MCP mutate the
  **open** module: `Command.ExecuteFromAsyncCode` on the pipe thread. No reload;
  changes are undo units. Proven: service action created in open Diet_BL (count
  1→2→3→4), visible in the tree.
- **Dependency management (proven):** `IESpace.AddDependency<T,S>(IShareable<T,S>)`
  inside `Command.ExecuteFromAsyncCode`. The generic method is resolved per-element
  via reflection (type args from the source's `IShareable<T,S>` interface). Covers
  all 15 consumable element types. Proven: 24 elements consumed from `Diet_CS` into
  `Diet_BL` (16 server actions + 4 entities + 4 folders, 0 errors, single undo unit).

## Workflow
1. **Save** the module in SS (`Ctrl+S`) → `.oml` on disk.
2. **Edit headlessly** via an `outsystems-omleditor` tool (`omlPath` → `outOml`).
3. **Reload in SS** (close + open `outOml`) → see the change.
4. **Verify**: tool self-verifies offline (`IsValidOml` + read-back); optionally
   confirm in SS.

## Roadmap
- Prove + tool entity/attribute/structure/delete/CRUD editing (same pattern) — both
  headless (`.oml` fragment edits) and live (`IESpace.Create*` inside
  `Command.ExecuteFromAsyncCode`).
- **DONE:** `add_service_action_param` + flow-node building (Run Server Action / Assign
  / Exception Handler).
- **Live in-memory editing: PROVEN** (OsLiveBridge + `outsystems-liveeditor` MCP +
  `live-editing` skill). Mutates the open module via the SS command system; no reload.
  Service actions, flow editing, AND dependency management all proven. Next: live
  entity/structure/attribute/delete tools (same command-open pattern).
- **Live dependency management: PROVEN** — `live_consume_elements` consumes all 15
  element types from a producer into a consumer (single undo unit). Next:
  `live_remove_dependency` (targeted removal via `IESpace.RemoveUnusedDependencies`).
- **Live data fetching (aggregates / data actions): PROVEN** — probe + create + delete on
  blocks and screens (`live_probe_data_sources`, `live_add_aggregate_to_block/screen`,
  `live_add_data_action_to_block/screen`, `live_delete_aggregate`, `live_delete_data_action`).
  Next: wire the aggregate's source entity fully (needs an open producer w/ entities) and a
  refresh/run-node if the node interface exists.
- **Phase 5 widgets: PROVEN** — `live_add_nr_widget` creates any of 19 Reactive widget kinds
  via the NRWidgets descriptor pattern; `live_set_widget_handler` wires OnChange/OnClick to
  client actions; `live_set_element_description` documents elements. Next: block input-param
  binding to aggregates, dropdown List property, widget handler generalization to remaining events.
- **Deployment note:** SS 11 on this machine runs Windows Smart App Control (WDAC,
  `{0283AC0F-...}`) enforced — rebuilding the unsigned OsLiveBridge DLL makes SS refuse to load it
  (CodeIntegrity 3077/3033). **Sign the rebuilt DLL** with the project's trusted cert before
  deploying:
  `Set-AuthenticodeSignature -FilePath $dst -Certificate (Get-ChildItem Cert:\CurrentUser\My | ? Subject -like '*OsLiveBridge Dev Signing*') -HashAlgorithm SHA256`.
