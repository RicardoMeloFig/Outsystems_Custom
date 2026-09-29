---
name: outsystems-liveeditor
description: Use when the user wants to edit an OutSystems module that is OPEN in Service Studio via the outsystems-liveeditor MCP server (live, in-process, no reload). Triggers on "live edit", "edit open module", "live service action", "create service action in open module", "change assign", "add output", "add dependency", "consume elements", "manage dependencies", "create button", "add button", "wire button onclick", "create web screen", "create web block", "web block", "add placeholder", "block variable", "add expression to block", "style class". Provides 215 live_* tools. Requires SS running with the OsLiveBridge plugin. **DEFAULT editing path — PREFER THIS over headless .oml editing whenever the module is open in SS; headless only on explicit user request.** Read live-editing first for the mechanism; for headless .oml file editing read outsystems-omleditor instead (on request). For screens/widgets/buttons see building-screens-and-buttons.
---

# outsystems-liveeditor (live in-process editor MCP)

One MCP server, 215 `live_*` tools. Unlike the headless `outsystems-omleditor` (which edits
`.oml` **files** with no SS running), this server talks to the **OsLiveBridge plugin
loaded inside a running Service Studio** over a named pipe (`OsLiveBridge-<SSpid>`).
Mutations land in the open module's live tree **immediately** (no Save/edit/reload)
via the SS command system (`Command.ExecuteFromAsyncCode` → `UndoManager`).

**Read `live-editing` first** — it covers the command-system breakthrough (why
`Command.Execute` is the opener and `ExecuteInContext` is not), `PresenterContext`,
and the bridge.

## Tool inventory (215 `live_*` tools, by area)

Counts are generated from the MCP server's `tools/list` (source of truth:
`mcp/outsystems-liveeditor/Program.cs`). Each tool's full description is in the
MCP schema; representative names below, `…` = more in the same family.

| Area | # | Representative tools |
|---|---|---|
| Status / modules / probes | 13 | `live_status`, `live_list_modules`, `live_module_info`, `live_list_web_flows`, `live_probe_type`, `live_probe_obj`, `live_probe_collection`, `live_debug_eSpace_collections`, `live_debug_eSpace_collection_items`, `live_probe_theme`, `live_probe_sheet`, `live_probe_layout_ref`, `live_debug_create_surface` |
| Actions: create / clone / delete | 15 | `live_create_service_action`, `live_create_server_action`, `live_create_client_action`, `live_create_screen_client_action`, `live_create_block_client_action`, `live_clone_service_action`, `live_clone_server_action`, `live_clone_client_action`, `live_clone_web_block`, `live_delete_service_action`, `live_delete_screen_client_action`, `live_delete_action`, `live_set_action_name`, `live_set_server_action_prop`, `live_probe_entity_actions` |
| Flow editing (nodes, params, vars) | 51 | `live_list_flow`, `live_debug_create_node`, `live_delete_node(_by_index)`, `live_set_node_target`, `live_set_connector_target`, `live_set_assign_value`, `live_add_assign_node`, `live_add_action_call_node`, `live_add_if_node`, `live_add_switch_node`, `live_add_foreach_node`, `live_add_refresh_node`, `live_add_message_node`, `live_add_js_node`, `live_add_input_param`/`live_add_output_param`, `live_add_entity_input`, `live_add_local_variable`, `live_map_action_inputs`, `live_set_error_handler_exception`, `live_layout_flow`, `live_set_node_position`, `live_probe_node_*`, `live_debug_node_props`, `live_debug_action_args` |
| Dependencies | 4 | `live_list_consumable_elements`, `live_consume_elements`, `live_remove_dependency`, `live_probe_references` |
| Data model (entities, structures) | 12 | `live_create_entity`, `live_delete_entity`, `live_add_entity_attribute`, `live_delete_entity_attribute`, `live_set_entity_attribute_name/type`, `live_set_entity_identifier`, `live_set_entity_prop`, `live_create_structure`, `live_add_structure_attribute`, `live_set_structure_attribute_type`, `live_set_structure_prop` |
| Screens, blocks, widgets | 66 | `live_create_web_flow/screen/block`, `live_delete_web_flow`, `live_move_web_block_to_flow`, `live_add_container/text/expression/link/html_element`, `live_add_button(_to_block)`, `live_set_button_onclick`, `live_add_to_placeholder`, `live_list_placeholders`, `live_add_widget_to_block`, `live_add_nr_widget` (23 kinds), `live_add_if_widget_to_block`, `live_set_widget_handler`, `live_add_event_to_block`, `live_set_block_handler`, `live_create_theme`, `live_set_theme_css`, `live_set_screen_theme`, `live_set_theme_layout`, `live_create_folder`, `live_move_widget`, `live_set_link_params`, `live_set_screen_title`, `live_probe_block_members`, `live_dump_widget_concretes` |
| Aggregates & data actions | 24 | `live_probe_data_sources`, `live_add_aggregate_to_screen/block`, `live_add_aggregate_source/sort/filter/join/calculated_attr`, `live_delete_aggregate_sort/filter`, `live_add_screen_aggregate_filter`, `live_set_aggregate_paging`, `live_read_aggregate_calcs/sorts/params`, `live_add_data_action_to_screen/block`, `live_delete_aggregate`, `live_delete_data_action` |
| Widget binding (CPs, types) | 11 | `live_set_block_cp`, `live_set_block_cp_parsed`, `live_set_block_cp_text`, `live_set_block_cp_image`, `live_set_screen_cp_parsed`, `live_set_screen_cp_text`, `live_set_widget_source`, `live_probe_block_cp`, `live_set_block_extended_property`, `live_set_extended_property`, `live_set_block_variable_type` |
| Styling & theme CSS | 8 | `live_set_style_class`, `live_fix_style_literal`, `live_probe_style_prop`, `live_set_user_css`, `live_set_module_css`, `live_read_theme_css`, `live_read_user_css_text`, `live_probe_sheet` |
| Security & config | 8 | `live_create_role`, `live_set_screen_permissions`, `live_read_screen_permissions`, `live_grant_screen_permission`, `live_remove_screen_permission`, `live_create_site_property`, `live_create_timer`, `live_set_timer_action` |
| Verification | 1+ | `live_get_verify_errors` (the inner loop), `live_debug_publish_surface` (publish API surface probe) |
| Publish (in-process) | 2 | `live_publish_module` (F5-equivalent, no UI automation; `ok:false` = async start, poll `live_debug_publish_state`), `live_debug_publish_state` (process monitor) |

## PREFER LIVE FIRST
**If the target module is OPEN in Service Studio, always use the live editor
(`outsystems-liveeditor`) first** — no Save needed, instant tree update, and every
edit is an undo unit (`Ctrl+Z`). Only fall back to the headless `outsystems-omleditor`
( Save `.oml` → edit → reload) when SS is not running or the module is not open.
Decision: `live_status` succeeds and lists the module → go live. Otherwise → headless.

## Prerequisites (different from headless)
- **Service Studio is running** with the OsLiveBridge plugin installed
  (`Plugins\ServiceStudio\ServiceStudio.Plugin.OsLiveBridge.dll`).
- The target module is **OPEN** in SS (e.g. `MyModule`). Server modules have no local
  `.oml` — open them from the server in SS.
- If `live_status` reports no bridge: start SS (plugin loads at startup via
  `[ModuleInitializer]`). If the plugin DLL is missing/stale, rebuild + redeploy it
  (see `live-editing` → Build/deploy/test).

No `.oml` path, no `outOml`, no Save-first — this is the live module.

## Tools

### `live_status()` — check the bridge first
Discovers SS processes with the OsLiveBridge pipe, pings each, reports which modules
are open in each. **Call this first** to confirm SS + plugin + open module before any
mutation. No args.

### `live_list_modules()` — what's open
Lists modules currently OPEN in SS (live, in-memory) with their SS pid. Read-only.

### `live_module_info(module)` — read an open module
Returns Name, Kind, and counts (entities, serviceActions, serverActions,
clientActions, anonymousStructures) + the concrete ESpace type. Read-only. Use to
confirm a module is open and get the before-count before a mutation.

### `live_create_service_action(module, name)` — create in the LIVE tree
Creates a Service Action in the open module — it appears in the SS tree immediately.
Opens a real SS command (`Command.ExecuteFromAsyncCode`), so the new action is a
proper **undo unit** (`Ctrl+Z` in SS removes it). Returns the created element type +
service-action count before/after. **Creates an empty action with only a Comment placeholder
node — NOT Start→End. You must create Start and End nodes yourself.** (Pass
`withSkeleton=true` to auto-create a linked Start→End skeleton.)

> **PROVEN on SS 11.55.89 (no caveat):** creation from scratch works via
> `IESpace.CreateServiceAction` inside a real command — a **Start→End skeleton
> (`CreateStandardNodes`) when `withSkeleton=true`** (default false = empty action
> with a Comment node, create Start/End yourself). Verified live: `serviceActions`
> 2→3 on ZombieGame_CS, type `ServiceStudio.Model.Flows+ServiceAPIMethod`, and the
> earlier `.83` "can't be children of Module objects" blocker did NOT fire (it was
> a stale-espace symptom, not a permanent platform block).

### Clone (server / client, live) — `live_clone_server_action(module, sourceName, newName)` / `live_clone_client_action(module, sourceName, newName)`
Same `IModelServices.Duplicate` mechanism as `live_clone_service_action`,
generalized to module-level **server actions** (searches `UserActions`/`ServerActions`)
and **client actions** (`ClientActions`). Exact copy (params, flow, metadata) with a
fresh key, renamed to `newName`, live tree, undo unit (`Ctrl+Z`).

### `live_remove_dependency(module, producer)` — remove a module dependency (live)
Deletes the `Reference` to `producer` from the module's `References` collection inside
a real SS command — undo unit (`Ctrl+Z` restores it). Companion to
`live_consume_elements`; use after consuming the wrong module or when a producer is no
longer needed.

### `live_create_server_action(module, name)` — create a server action in the LIVE tree
Creates a Server Action (UserAction) in the open module — same pattern as
`live_create_server_action` but calls `IESpace.CreateServerAction`. Server actions
have flows that can be edited with all `live_*` flow tools. Undo unit (`Ctrl+Z`).

### `live_create_client_action(module, name)` — create a client action in the LIVE tree
Creates a Client Action in the open module — same pattern as
`live_create_service_action` but calls `IESpace.CreateClientAction`. Client actions
have flows that can be edited with all `live_*` flow tools. Undo unit (`Ctrl+Z`).

### `live_create_screen_client_action(module, screen, name)` — create a SCREEN-level Client Action
Creates a **screen-level** Client Action (`ClientScreenActionFlow`) on a screen's
`ClientActions` collection — **the kind a Reactive Button's OnClick can be wired to**
(its `Destination` is typed `IClientSideDestination`, which only screen-level flows
implement; the global `ClientActionFlow` is not assignable to a button). Screen actions
have flows editable with all `live_*` flow tools. Undo unit (`Ctrl+Z`).

### `live_add_button(module, screen, placeholder?, parent?, name, text, styleClass?)` — add a REAL Button
The Button is a **plugin CustomWidget** (`NRWebWidgets.CustomWidget-
OutSystems.Plugin.NRWidgets.Button`, `CustomObjectImplKey=...`) — it CANNOT be made via
`CreateWidget<T>`. Created via `Button+Kind.Instance.Descriptor` → `CreateWidget(descriptor)`.
Target: `placeholder` = layout placeholder (e.g. `Actions`), else `parent` ('' = screen).
`text` = button label (set on the `content` CustomPlaceholderWidget Text child, reusing
SS's auto-created default Text). `styleClass` = CSS class (e.g. `"btn btn-primary"`).

### `live_set_button_onclick(module, screen, button, actionName)` — wire a Button's OnClick
Wires the button's OnClick EventHandler **`Destination`** to a Client Action. The action
must be a **screen-level `ClientScreenActionFlow`** (create it with
`live_create_screen_client_action`). Verify with `live_probe_widget_members` →
`onClickDestination` = `Name (NRFlows.ClientScreenActionFlow:...)`.

> Full runbook incl. failure modes: **`building-screens-and-buttons`**.

### `live_clone_service_action(module, sourceName, newName)` — deep-clone in the LIVE tree
Deep-clones an existing Service Action into a new one — an **exact copy** (input/output
parameters, flow graph, metadata) with a fresh key, renamed to `newName`. Uses
`IModelServices.Duplicate(sourceSignature, espace)` inside `Command.ExecuteFromAsyncCode`
— the same serialize→deserialize mechanism SS copy/paste uses. Lands in the live tree
immediately as an undo unit (`Ctrl+Z`). Proven: cloned `SourceAction`→`ClonedAction`
in `MyModule` (1→2 actions; ClrMD `get_action_detail` confirmed identical public flag +
params + output). Use this to replicate an action exactly, no UI.

> ⚠ **Same-module only — cross-module is platform-unreachable (proven 2026-09-29, SS 11.55.89).**
> `live_clone_service_action_from` was exhaustively investigated: the model routes
> cross-espace duplicates through `CopyPaste.ClipboardManager`
> (`prs#miuspfqw.Duplicate` → `Serialize(list)` → `DeserializeInto`), and that path
> **always returns `null` → NRE at `.Objects`**, including when every gate passes:
> - licensing `CheckLocalForeignCodes` **= true** (equal activation codes + real
>   `ServerCommunicationsProvider` fed into a `MockAggregatorPresenter`);
> - `MockPresenter(MockAggregatorPresenter{ActiveESpace=consumer})` constructed
>   (6-param optional ctor, explicit defaults, `registerAsStaticInstance=false`);
> - serialization with the **real** AggregatorPresenter (SourceSelection needs one).
>
> The root: `GetCommandResult` returns null because the deserializer parents nothing
> under the espace — and the model's OWN `ms.Duplicate` fails identically, so **no SS
> code path uses this for cross-module clones**; the UI uses tree-node Copy/Paste
> commands (real presenters + selection) which is not reproducible headlessly.
> Workarounds: same-module `live_clone_element` + `live_create_service_action` (replay
> the flow), or UI copy-paste. `clone_element` (same-module entity/structure/action)
> is **proven** (Wave→ProbeCloneE, 8→9 verified).


## Screens, widgets & buttons (live)

**Full runbook: `building-screens-and-buttons`.** Reactive screens use a layout
WebBlockInstance with placeholders (`Header`, `Breadcrumbs`, `Title`, `Actions`,
`MainContent`, `Footer`). Commands:
- `live_list_web_flows(module)` — flows + screens + themes (module Kind).
- `live_create_web_screen(module, webFlow, name)` / `live_create_web_flow(module, name)`.
- `live_list_widgets(module, screen)` — widget tree incl. placeholder children.
- `live_add_container(module, screen, parent, name, styleClass)` — `parent=''` = screen,
  else a container name to nest.
- `live_add_text` / `live_add_expression` / `live_add_link(module, screen, parent, name, text, targetScreen?, styleClass)`.
- `live_add_html_element(module, screen, parent, name, tag, styleClass)` — real `<h1>`/`<p>`…
- `live_add_button(module, screen, placeholder?, parent?, name, text, styleClass)` — real Button (plugin CustomWidget).
- `live_set_button_onclick(module, screen, button, actionName)` — wire OnClick → **screen-level** Client Action.
- `live_create_screen_client_action(module, screen, name)` — screen-level `ClientScreenActionFlow`.
- `live_add_to_placeholder(module, screen, placeholder, kind, name, …)` — kind ∈
  container|text|expression|link; `live_add_inside_placeholder` (nest into a container's
  `content`), `live_list_placeholders`, `live_delete_from_placeholder`, `live_add_link_to_block`,
  `live_list_block_widgets`, `live_set_screen_title`.
- Theme/layout: `live_create_theme`, `live_set_theme_css`, `live_set_screen_theme`,
  `live_set_flow_theme`, `live_clone_web_block`, `live_move_web_block_to_flow`,
  `live_set_theme_layout`, `live_probe_theme`.

## Web blocks (live) — create from scratch, widgets, variables

Web blocks are **flow nodes** inside a WebFlow's `Nodes` (screens are the other node
kind). A block's content lives in its widget tree (`Widgets`) and its own `Variables`
collection. Tools (all live, undo unit `Ctrl+Z`; verified on `FitnessManager` →
`ZombieHeader` in the `zombieGame` flow):

- `live_create_web_block(module, flow, name)` — **from scratch** web block (NO
  clone/move). Hunts a `Create*`/`New*` factory on the eSpace or the flow and
  invokes it inside `Command.ExecuteFromAsyncCode`. Proven: `IUIFlow.CreateBlock(name,
  key)` → `NRNodes.WebBlock` added to the flow's Nodes. (Contrast:
  `live_clone_web_block` = `Duplicate`-based, then `live_move_web_block_to_flow`.)
- `live_list_block_widgets(module, block)` / `live_dump_block_widget_types(module,
  block)` — read the block's widget tree. The latter also prints each widget's **concrete
  type + all interfaces, including anonymous widgets** (shown as `(anon)`).
- `live_add_widget_to_block(module, block, parent, kind, name, value?, styleClass?)` —
  add any widget kind to the block root or a named container (`parent`): kind ∈
  container|text|expression|link|html|placeholder. `value` applies to text/expression.
- `live_add_placeholder_to_block(module, block, parent, name)` — add a **Placeholder**
  widget (so screens can fill it).
- `live_add_variable_to_block(module, block, name)` — create a **Local Variable** on the
  block (proven: `WebBlock.CreateLocalVariable(name, key)` → `LocalVariable`).
- `live_set_block_widget_property(module, block, widgetName, propName, propValue)` —
  set a string property on a block widget (mirror of `live_set_widget_property`). For an
  Expression's value use `propName='Value'` with the expression reference (e.g. the
  variable name) — falls back `SetValue` → `SetProp`.
- `live_delete_widget_from_block(module, block, name)` — delete a widget (and children).
- **Buttons INSIDE web blocks (proven):** `live_add_button_to_block(module, block,
  parent, name, text, styleClass)` — the SAME Button mechanism as screens (NRWidgets
  plugin CustomWidget via `ButtonDescriptor` → `CreateWidget(descriptor)`), just resolved
  via `FindBlock` instead of `FindScreen`. `parent=''` = block root, or a container name.
  `live_set_block_button_onclick(module, block, button, actionName)` wires OnClick (searches
  the block's own ClientActions first, then the module; falls back to descriptor
  `SetOnClickHandler`). **The screen-only `live_add_button` will NOT accept a block name**
  (`FindScreen` filters by `IScreen`).
- **Web-block Input Parameters (proven):** `live_add_input_param_to_block(module, block,
  name, type)` (factory hunt `WebBlock.CreateInputParameter` — proven for Text/LongInteger),
  `live_set_block_input_param_type` (basic/Structure/entity-record/entity-Identifier),
  `live_add_entity_input_to_block` / `live_add_entity_identifier_input_to_block`,
  `live_remove_input_param_from_block`, `live_list_block_input_params` (read-only).

### ⚠ EXPRESSION vs TEXT (proven pitfall)
A real Reactive Expression is the **NRWidgets plugin CustomWidget**
`ServiceStudio.Plugin.NRWidgets.Expression` (tree shows `[Expression]`), created via
the interface `ServiceStudio.Plugin.NRWidgets.IExpression`. The older fallback
(`OutSystems.Model.UI.Mobile.Widgets.ITextWidget`) produces a plain
`ServiceStudio.Model.NRWebWidgets.Text` (`[Text]`) — visually just a label, NOT an
expression. `live_add_widget_to_block` (and `CreateWidgetByKind`) try
`ServiceStudio.Plugin.NRWidgets.IExpression` **first**. Verify with
`live_dump_block_widget_types` → concrete type must be `...NRWidgets.Expression`.

### Decision nodes (If / Switch — proven)
- `live_add_if_node(module, action, condition, afterNodeIndex?)` — creates an `IIfNode`,
  sets the condition via **`SetCondition(string)`** (the ONLY working surface; the If's
  `Match`/`ForcedMatchOrMatch` props are `ServiceStudio.Merge.Match` objects and CANNOT be
  set with a string). Optional `afterNodeIndex` chains the previous node's Target to the If.
- `live_probe_node_connectors(module, action, nodeIndex)` — read-only: lists the branch
  props. **If → `TrueTarget` / `FalseTarget`** (explicit interface props, NOT `Target`);
  **Switch → `OtherwiseTarget`**.
- `live_set_connector_target(module, action, nodeIndex, propName, targetIndex)` — sets a
  NAMED branch prop (TrueTarget/FalseTarget/OtherwiseTarget). `live_set_node_target` only
  sets `Target`, so it CANNOT wire an If's branches.
- `live_add_switch_node(module, action, afterNodeIndex?)` — creates an `ISwitchNode`.
  Switch case conditions live per-case (not a single expression); wire cases with
  `live_set_connector_target`.

### Custom Events + lifecycle handlers on web blocks (proven)
- `live_probe_block_members(module, block)` — read-only: the block's properties AND
  collections (`CustomEvents`, `InputParameters`, `LocalVariables`, `ClientActions`,
  `DataActions`, `ScreenAggregates`, `Widgets`, `Placeholders`) with counts + first-item
  types. Use BEFORE event/handler work to see the surface.
- `live_add_event_to_block(module, block, name)` — create a custom EVENT
  (proven: `WebBlock.CreateEvent` → `WebBlockCustomEvent`).
- `live_add_event_param_to_block(module, block, event, name, type)` — payload param on the
  event (proven: `Event.CreateInputParameter`).
- `live_set_block_handler(module, block, handler, actionName)` — lifecycle handlers
  (OnInitialize/OnReady/OnRender/OnParametersChanged/OnDestroy) are **NREvents children
  that own their own flow** — NOT properties you point at a separate action. The tool detects
  this and reports it; `FindAction` now resolves them by name, so ALL flow tools
  (`live_list_flow`, assigns, etc.) address a handler's flow directly (e.g.
  `live_list_flow(action="OnParametersChanged")`).

> ⚠ PARTIAL LIMITATION: creating flow NODES **inside a lifecycle child's flow**
> (`NREvents.*` children like `OnParametersChanged`) can **deadlock the bridge pipe**
> (must restart SS to unstick; the pipe is single-instance) — avoid node creation there.
> **A "Raise Event" node in a normal BLOCK client action is PROVEN safe** (2026-09-29):
> `add_event_to_block` + `create_block_client_action` + `add_raise_event_node` — the node
> is created via `ITriggerNode` and bound to the typed `WebBlockCustomEvent` in the SAME
> command, which is what prevents SS's auto-open "Select Event" modal (the old deadlock
> cause). See the proven list at the end of this skill.

### Deploy note (once per bridge change)
Adding a **new bridge command** (e.g. `create_web_block`, `add_widget_to_block`)
requires rebuilding the plugin (`dotnet build bridge/OsLiveBridge/OsLiveBridge.csproj -c
Release`, which copies to `Plugins\ServiceStudio\`) and **restarting Service Studio**
(SS locks the DLL while running). After deploy, drive the new command with
`scripts\Send-BridgeCmd.ps1 -Module <m> -Cmd <cmd> -JsonArgs '{...}'` (the MCP
tool list is frozen for an already-running opencode session; it picks up new tools after
opencode restarts).

Deep reference: **`docs/web-block-editing.md`**.

## Generic flow-editing tools (direct edit — NO SS restart)

These are **generic primitives** — compose any flow edit from MCP calls. The bridge
is deployed once with these; **edits never require recompiling/restarting SS** (only
the one-time plugin install/upgrade needs a restart — see `live-editing` → clean
restart). Each mutation is its own SS command (undo unit, `Ctrl+Z`).

### `live_list_flow(module, action)` — inspect the flow graph (read-only)
Dumps each node: `[i] <Type>  [k] var = value  -> [targetIndex]`. Use BEFORE editing to
see the topology (normal vs exception paths, which node points to which End). Essential
for picking the right `anchor` for `live_add_assign_node`.

### `live_set_assign_value(module, action, matchValue, newValue, matchVar?)`
Finds the assignment whose value text contains `matchValue` (and optionally variable
contains `matchVar`) and sets it to `newValue`. The value is passed as-is to SetValue
(same as `live_add_assignment_to_node`). For text literals, include double quotes in
`newValue` (e.g. pass `"new"` to get `"new"` in SS).

### `live_add_output_param(module, action, name, type)`
Adds an output parameter. `type` is a basic type: `LongInteger`, `Integer`, `Text`,
`Decimal`, `Boolean`, `DateTime`, `Date`, `Time`, `PhoneNumber`, `Email`, `BinaryData`,
`Currency`, `TextIdentifier`, `IntegerIdentifier`, `LongIntegerIdentifier`
(→ `IESpace.<type>Type`).

### `live_add_assign_node(module, action, var, value, where, anchorVar?, anchorValue?)`
**Creates a NEW node with ONE assignment.** For multi-assignment nodes, use
`live_add_assignment_to_node` instead. The `beforeEnd` linking bug is now **fixed**
(uses `ReferenceEquals`). Adds an `Assign` node (`var = value`) and links it:
- `where="afterAnchor"` → inserts AFTER the Assign node whose assignment matches
  `anchorVar`/`anchorValue` (use this to place at the end of the **normal** flow —
  e.g. anchor `Result.IsError`/`False`; the node keeps its old target, the anchor now
  points to the new node). **Prefer this** — it targets a specific path, avoiding the
  exception-handler flow.
- `where="beforeEnd"` → inserts before the FIRST End node (ambiguous if there are
  multiple Ends — normal vs exception; use `afterAnchor` instead).

### `live_add_assignment_to_node(module, action, nodeIndex, var, value)` — add a 2nd assignment
Add a 2nd assignment to an existing `IAssignNode` (multi-assignment nodes). Pass
the target node's `nodeIndex` (from `live_list_flow`), the variable name, and the
value expression. Text literals need **double quotes** in `value` — pass
`"hello"` (the double quotes are part of the value, do NOT wrap in single
quotes). Use this instead of creating separate Assign nodes when one logical
step sets multiple variables.

### `live_delete_node(module, action, matchVar, matchValue)`
Deletes the Assign node whose assignment matches `matchVar`/`matchValue`; relinks the
previous node to the deleted node's target. Use to remove a misplaced node, then
re-add it correctly with `live_add_assign_node`.

### `live_add_input_param(module, action, name, type)`
Adds an **input** parameter (the companion to `live_add_output_param`). `type` is a
basic type (`LongInteger`, `Integer`, `Text`, `Decimal`, `Boolean`, `DateTime`,
`Date`, `Time`, `PhoneNumber`, `Email`, `BinaryData`, `Currency`, `TextIdentifier`,
`IntegerIdentifier`, `LongIntegerIdentifier`) or a **Structure type** (unique name
match in the module's `es.Structures` collection). Uses
`IServiceAPIMethod.CreateInputParameter` + `IParameter.DataType` inside
`Command.ExecuteFromAsyncCode`.

### `live_add_local_variable(module, action, name, type)` — add a local variable
Adds a **local variable** to an action (service action, server action, or client
action). Local variables are action-scoped (visible in the flow but not exposed as
input/output parameters). Same type resolution as `live_add_input_param` (basic
types + Structure types). Uses `IAction.CreateLocalVariable` inside
`Command.ExecuteFromAsyncCode`. Undo unit (`Ctrl+Z`).

### `live_add_end_node(module, action, where)`
Adds an `End` node to the flow. Three insertion modes:
- `where="beforeEnd"` — inserts before the first End node (existing flow end becomes
  the new node's Target, prev→newEnd→oldEnd chain)
- `where="atEnd"` — appends after the last node in the flow
- `where="exceptionPath"` — creates a standalone End node for an exception handler
  branch (no auto-linking; wire manually with `live_set_exception_handler`)

Useful for building Try→Catch→End exception flows where you need a dedicated End node
on the error handler path.

### `live_set_exception_handler(module, action, endVar, endValue, handlerVar, handlerValue)`
Wires an exception handler node to an End node's `ExceptionHandler` slot — completing
a Try→Catch→End exception chain. Both nodes are found by assignment var+value
substrings. Use after `live_add_end_node` with `where="exceptionPath"` to wire up
the error path.

> **Note:** `Start.ExceptionHandler` does NOT exist. To build an exception path use
> `live_debug_create_node(..., "IExceptionHandlerNode")` +
> `live_set_error_handler_exception` + `live_set_node_target` instead.

**Typical Try/Catch flow construction:**
```
live_add_end_node(MyModule, MyAction, "exceptionPath")          # 1. create catch-end node
live_set_exception_handler(MyModule, MyAction, "Id","1","Result","False")  # 2. wire catch→end
```

### `live_set_error_handler_exception(module, action, nodeIndex)` — set Exception=All Exceptions
Sets `Exception = AllExceptions` (SystemException) on an `IExceptionHandlerNode`
found by index. Also sets `AbortTransaction = true` and `LogError = true`. Use to
configure a freshly-created exception handler node (created via
`live_debug_create_node(..., "IExceptionHandlerNode")`) so it actually catches
all exceptions. This is the correct mechanism — `Start.ExceptionHandler` is not
settable (see `live_set_start_exception_handler`).

### `live_set_start_exception_handler(module, action, handlerVar, handlerValue)` — ⚠ BROKEN
Wires the exception handler branch to the Start node's `ExceptionHandler` property.
**`Start.ExceptionHandler` does NOT exist as a settable property on SS 11.55.81+ —
this tool will fail.** Use `live_set_error_handler_exception` instead (set
`Exception=AllExceptions` on the `IExceptionHandlerNode` itself, then wire targets
with `live_set_node_target`).

### `live_probe_node_types(module, action)` — discover node types (read-only)
Enumerates all `I*Node` interface types currently present in the flow graph. Use to
discover the exact interface name for call-server-action nodes, try/catch nodes, etc.
at runtime before using `live_add_action_call_node`. Returns a list of interface names
(e.g. `IStartNode`, `IEndNode`, `IAssignNode`).

### `live_add_action_call_node(module, action, where, serverActionName, anchorVar?, anchorValue?, producerModule?)` — call a server action from the flow
Inserts a **server-action-call node** into the flow. The server action must already be
consumed by the module (via `live_consume_elements`). Insertion modes:
- `where="afterAnchor"` — inserts after the **Assign node** matching `anchorVar`/`anchorValue`.
  **ONLY works with Assign nodes** that have existing assignments. Does NOT work with Comment,
  Start, End, ExecuteAction, or ErrorHandler nodes. Use this to insert into an existing flow
  that already has Assign nodes (e.g. after `Result.IsError=False`).
- `where="beforeEnd"` — inserts before the first End node.
  **PREREQUISITE:** Start and End nodes must exist AND be linked
  (`live_set_node_target(startIdx, endIdx)`) before calling this. If no End node exists or
  Start→End is not linked, the call fails with `"End/prev not found"`.

The server action is resolved by name from the module's consumed `ServerActions`. If
`producerModule` is set, it disambiguates when multiple consumed modules have the same
server action name.

**Common failure modes:**
| Error | Cause | Fix |
|-------|-------|-----|
| `End/prev not found` | No End node exists, or Start→End not linked | Create Start+End first, link them, then retry with `where="beforeEnd"` |
| `anchor not found: =` | Empty `anchorVar`/`anchorValue` on `afterAnchor` | Provide real Assign node var+value, or use `where="beforeEnd"` |
| `anchor not found: Comment=...` | `afterAnchor` used with a Comment node | `afterAnchor` ONLY matches Assign nodes; use `where="beforeEnd"` |

```
live_add_action_call_node(MyModule, MyAction, "afterAnchor", "Result.IsError", "False", "ClientCreate", "Diet_CS")
```

### `live_map_action_inputs(module, action, nodeIndex)` — auto-map ExecuteAction inputs
Auto-maps the input arguments of an `IExecuteServerActionNode` (created by
`live_add_action_call_node`) by **name**. Tries exact name matching first (input
param name → same-named variable in scope), then falls back to trying each input
parameter name against available variables. Use after creating a call node to
avoid wiring each input argument manually with `live_set_node_property`.

### `live_delete_node_by_index(module, action, nodeIndex)` — delete any node type
Deletes a node at the given index from the flow's NodeList. Works for **all node
types** — Assign, ExecuteAction, End, ErrorHandler, Start, Comment. Relinks the
previous node to the deleted node's target. Use this instead of
`live_delete_node` for non-Assign nodes. **Bug fixed:** the Key-as-string comparison
that nullified all Targets is now fixed (uses `ReferenceEquals`).

### `live_set_node_target(module, action, nodeIndex, targetIndex)` — link nodes by index
Set `node[nodeIndex].Target = node[targetIndex]`. This is the **correct** way to
wire flow nodes — deterministic, no reference identity bugs. Use after
`live_list_flow` confirms actual indices.

### `live_set_node_property(module, action, nodeIndex, propName, propValue)` — set any node property
Set any settable string property on a node by name. For the `Action` property on
ExecuteAction, pass the server action name as a string. For typed properties
(like `Exception` on ErrorHandler), use the clone approach instead.

### `live_delete_service_action(module, action)` — delete entire service action
Delete a service action by name. Use to remove a broken action before replacing
via `live_clone_service_action`. This is the fast path for replacing broken
actions — delete + clone.

### `live_debug_create_node(module, action, nodeInterface)` — create any node type
Create a flow node by its interface short name. Valid interfaces:
`IStartNode`, `IEndNode`, `IAssignNode`, `IExecuteServerActionNode`,
`IExceptionHandlerNode`. Returns the created node type. Use `live_probe_node_types`
first to see what's available.

### `live_debug_node_props(module, action, nodeIndex)` — inspect a node's settable properties (read-only)
Inspect a node's concrete type and all settable properties by index. Use to
identify node types at runtime — **indices are NOT creation order** (they reflect
the flow's NodeList order; verify with `live_list_flow` first). Returns property
names, current values, and types so you know what `live_set_node_property` can
change on a given node.

### `live_debug_action_args(module, action, nodeIndex)` — inspect ExecuteAction arguments (read-only)
Inspect an `IExecuteServerActionNode`'s arguments: parameter names, current
values, and settable properties. Use to see exactly which inputs an
ExecuteAction node exposes before mapping them with `live_map_action_inputs` or
setting them individually with `live_set_node_property`.

## Layout tools (live — position nodes on the canvas)

Nodes are **auto-positioned on creation** (each new node gets a Y based on
existing node count; ExceptionHandler nodes go to the right column). This
prevents the "all nodes stacked at (0,0)" problem. For a **proper topology-based
layout**, call `live_layout_flow` after the flow is fully built and linked.

Layout constants (derived from the user's manually-positioned reference):
- Main flow column: **X=3200**
- Exception flow column: **X=12800**
- Y starts at **914**, increments **~2000** per node downward

### `live_layout_flow(module, action)` — auto-position all nodes by topology
Analyzes the flow graph and positions every node:
- **Main flow** (traversed from Start → Target → ... → End): left column (X=3200)
- **Exception flows** (traversed from each ExceptionHandler → Target → ... → End): right column (X=12800)
- Y increments from 914 downward (~2000 per node)

Call this **AFTER** the flow is fully built, linked, and the Comment placeholder
is deleted. Undo unit (Ctrl+Z).

### `live_get_node_positions(module, action)` — read X/Y for all nodes (read-only)
Returns the canvas X/Y position of every node in the flow. Use to verify a layout
or inspect how nodes are positioned. Example output:
```
[0] Start  X=3200  Y=914
[1] End    X=3200  Y=6914
[2] Assign X=12800 Y=2914
```

### `live_set_node_position(module, action, nodeIndex, x, y)` — set X/Y for one node
Set the canvas position of a single node by index. Handles numeric type conversion
(int/double) automatically — unlike `live_set_node_property` which passes strings.
Use for custom layouts or fine-tuning after `live_layout_flow`. Undo unit (Ctrl+Z).

## Dependency management tools (live — consume from producer modules)

These tools manage **module dependencies** (the Manage Dependencies dialog in SS,
headlessly). Both the consumer and producer modules must be OPEN in SS. Covers **all
15 consumable element types**: ServiceActions, ServerActions, ClientActions, Entities,
Structures, Roles, Processes, Scripts, Images, Resources, WebThemes, MobileThemes,
WebFlows, MobileFlows, Folders. Uses `IESpace.AddDependency<T,S>` inside
`Command.ExecuteFromAsyncCode` — single undo unit for all consumptions.

### `live_list_consumable_elements(module)` — list what a producer exposes (read-only)
Enumerates ALL public consumable elements in the producer module, grouped by type.
For types with a `Public` property (12 of 15): only public elements are listed. For
containers without `Public` (WebFlow, MobileFlow, Folder): all are listed. Returns a
JSON object with element names + keys per type + a `totalConsumable` count.

### `live_consume_elements(consumer, producer, what)` — consume elements (mutation)
Consumes (adds references to) elements from a producer into a consumer module. All
consumptions happen inside ONE `Command.ExecuteFromAsyncCode` — a single undo unit
(`Ctrl+Z` removes all). The `AddDependency` generic method is resolved per-element via
reflection (type args extracted from the source object's `IShareable<T,S>` interface).

**`what` parameter formats:**
- `"*"` — consume ALL consumable elements (all 15 types, public only)
- `"ServerAction:*,Entity:*"` — all of specific types
- `"ServerAction:GetUser,Entity:User"` — specific elements by name
- `"ServiceAction:*,ServerAction:*,Entity:*,Structure:*"` — all common types

```
live_list_consumable_elements(Diet_CS)                              # 1. see what's available
live_consume_elements(diet_BL, Diet_CS, "*")                        # 2. consume everything
live_consume_elements(diet_BL, Diet_CS, "ServerAction:*,Entity:*")  # 3. or just specific types
```

### Composing edits (proven on a service action)
```
live_clone_service_action(MyModule, SourceAction, ClonedAction)   # 1. start from a clone
live_list_flow(MyModule, ClonedAction)                            # 2. see the graph
live_set_assign_value(MyModule, ClonedAction, "old value","new value")   # 3. change value
live_add_output_param(MyModule, ClonedAction, "Id","LongInteger")              # 4. add output
live_add_assign_node(MyModule, ClonedAction, "Id","1","afterAnchor","Result.IsError","False")  # 5. Id=1 at normal-flow end
live_list_flow(MyModule, ClonedAction)                            # 6. verify placement
```
Verify with the first baseline's ClrMD `get_action_detail` (authoritative, read-only).

## Delete + Clone Pattern (fastest for replacing broken actions)

When a module has a broken service action and a manually-created reference that
works correctly:

```
1. live_delete_service_action(MyModule, BrokenAction)           # remove broken one
2. live_clone_service_action(MyModule, GoodReference, BrokenAction)  # clone to target name
```

This produces a structurally correct action in seconds. Used to fix `ClientCreate_BL`
when manual flow construction hit the `add_assign beforeEnd` bug. Single undo
unit per step.

## Publishing (live, in-process — no UI automation, no Ollama) — PROVEN 2026-09-24

`live_publish_module(module, commitMessage?)` invokes
`ServiceStudio.Presenter.Commands.Publish` (the F5 command) via the registered singleton
`AutoRegistryType<Publish>.Instance` and calls `Execute(agg, agg)` — the same code path as
the button, zero clicks/vision. **Behavior (verified on SS 11.55.83, ZombieGame_CS, dev env):**
`Execute` returns null almost immediately (async) — `ok:false` is EXPECTED; the real publish
runs in the `ServerProcess` (registered in `ServerProcess.ProcessesStarted`,
`InnerState: Uploading → … → none`). Verify with `live_debug_publish_state`
(ProcessesStarted `none` = finished) + `live_get_verify_errors` + Service Center version.
**Do NOT pass `commitMessage`** — the message path (`prs#lislasrz`) got STUCK at Uploading
and blocked all further publishes (guard `ServerOperationRunning`) until SS restart.
`live_debug_publish_surface(module)` is the read-only surface probe. Full runbook:
`publishing` (Section 3.0).

## Styling & theme CSS (live)

**Read `styling-and-css-live` first** (the runbook) and `docs/ui-styling-reference.md`
(the deep reference). Key facts proven on SS 11.55.81:

- **Container Style Classes** go in the container's **`Style` CustomProperty** as a
  `ParsedExpression` text literal — NOT `CustomStyle` (SS drops CustomStyle on
  containers after save/reload). `live_set_style_class` handles this automatically
  (container → Style CP via `SetValueExpression`, clears stray `CustomStyle`);
  Links/Text keep `CustomStyle` (correct convention).
- **Theme CSS** must be written to **`_userCssSource`** (public
  `WebStyleSheet.SetUserCssSource(String)`) to appear in SS's theme CSS editor.
  The old `_cssSource` path renders the page but leaves the editor empty.
  `live_set_module_css` now routes to `set_user_css`; `live_set_user_css` is the
  explicit tool.
- **Verify:** `live_probe_style_prop` (container `Style` CP shows ParsedExpression
  with the class) and `live_read_theme_css` (`_userCssSource` non-null). `live_probe_sheet`
  lists the sheet's full public API.
- **PowerShell gotcha:** `ConvertTo-Json` wraps long CSS (>~4 KB) into `{"value":"..."}`
  → cryptic `requires an element of type 'String'`. Use `scripts/Send-BridgeCmd.ps1`
  or `JavaScriptSerializer` + concat (MCP tools are already safe).

End-to-end: style → save (`Ctrl+S`) → verify the `.oml` (Style CP `Value="class"`,
no stray `CustomStyle`, theme `UserCssSource` non-empty) → restart SS → confirm the
editor + Properties panel.

## Data fetching � aggregates & data actions (Phase 4, proven live)

Blocks and screens expose `DataActions` (`DataScreenActionFlow`) + `ScreenAggregates` (`WebScreenDataSet`).
- `live_probe_data_sources(module, block?, screen?)` � probe both collections + factory methods
  (proven: `CreateScreenAggregate(Boolean,String,IKey)`, `CreateDataAction(String,IKey)`).
- `live_add_aggregate_to_block` / `live_add_aggregate_to_screen(module, block|screen, name, entityName?, producerModule?)`.
- `live_add_data_action_to_block` / `live_add_data_action_to_screen(module, block|screen, name)`.
- `live_delete_aggregate` / `live_delete_data_action(module, block?, screen?, name)`.
Source entity is best-effort (needs an open producer module exposing entities via a consumed reference).

## Phase 5 widgets � any Reactive custom widget (proven live, 23 kinds)

`live_add_nr_widget(module, screen?, block?, parent?, kind, name, styleClass?, text?)` creates any
Reactive custom widget via the Button-proven descriptor pattern (`<Kind>+Kind.Instance.Descriptor`
? `CreateWidget(descriptor)`). Kinds: button, label, input, textarea, checkbox, dropdown, radio,
radio-group, switch, list, **list-item**, table, image, icon, form, button-group (+ container/
expression/link/html) and **upload, popover, popup, list-item-action** (all in the NRWidgets plugin
- verified by scanning `ServiceStudio.Plugin.NRWidgets.dll` type names; the probe families list is
NOT exhaustive).
- `live_probe_widget_kinds(module)` � enumerate all NRWidgets kinds + their descriptors.
- `live_set_widget_handler(module, screen?, block?, widget, event, actionName)` � wire OnChange/
  OnFocus/OnBlur/OnClick of a widget to a client action (screen-level `ClientScreenActionFlow` required
  for interactive widgets; `Input.OnChange` is a builtin event with settable `Destination`).
- `live_set_element_description(module, kind, name, description)` � set the writable `Description`
  on block/screen/action.
- SS auto-creates children on some widgets: RadioGroup spawns 3 RadioButtons, ButtonGroup 3
  ButtonGroupItems, Upload an Icon+Text, Popover topContent Text+Icon.

## Image widget — the Image CP needs an OBJECT (proven, hard-won)

The NRWidgets.Image `Image` CustomProperty must hold a real **`ServiceStudio.Model.Image`
object**. All string forms FAIL with `'Object' data type required instead of 'Text'` +
`'Value' must be set in Image`:
- `SetValueExpression("imageName")` → Text literal (wrong type);
- writing the `_value` field with `"Image:/Images.<key>"` (the **OML file** serialization form) or
  `"Name (Image:key)"` (its **ToString**) — the probe's `ToString()` masks the type difference.
Correct: **`live_set_block_cp_image(module, block, widget, propName, imageName)`** → resolves the
Image in `es.Images` by Name/Key and calls `CP.SetPropertyValue("Value", <Image object>)`, then
clears any stale ValueExpression. Verified state: `_valueType: ServiceStudio.Model.Image`,
`_valueExpression: null` (matches SS-created widgets). If errors persist, check for a leftover
Text ValueExpression on the CP (`live_probe_block_cp` shows it) — the command clears it.
Also note: after crashes/reloads the saved state comes back; image **names can be renamed** by the
user (`tanjirokamado3840x216023027` → `Tanjiro`) — resolve by Name or Key.

## SetValueExpression semantics + text literals (proven, corrected)

`CustomProperty.SetValueExpression(String)` **parses** the string as an expression:
- bare identifier (`LocalVar_Input`) → **Reference element** — a VALID binding when the name is in
  scope (this is how Variable/Source bindings are set via `live_set_block_cp`);
- quoted string (`"header"`) → Text element with the **quote characters stored verbatim** — NOT
  stripped (SS Style Classes showed `"header"` as the class name);
- bare unknown identifier (`header`) → invalid reference → verify error "Unknown object".

For **text-literal properties (Style classes, static values)** use
**`live_set_block_cp_text(module, block, widget, propName, value)`** — produces
`[Type: Text] [Value: header]` bare, matching SS-created widgets. `live_probe_block_cp` dumps a
CP's expression element + `_valueType` so you can verify Reference vs literal.

## If widget — created via IIfWidget, NOT the Kind-descriptor pattern (proven)

`live_add_if_widget_to_block(module, block, parent, name, condition)` creates the If via the
**parent's generic `CreateWidget<OutSystems.Model.UI.Mobile.Widgets.IIfWidget>(name, key)`**.
The Kind-descriptor pattern does NOT apply: `NRWebWidgets+If+Kind` exists but has **no Instance
singleton** (its factory is `CreateInstanceInParent(IParent1)`), and `ServiceStudio.Plugin.NRWidgets.If`
has no descriptor. Condition is set via the If's **`SetCondition(String)`**.
- **Children in branches**: parent syntax `"IfName:True"` / `"IfName:False"` (add_nr_widget; the
  legacy add_widget_to_block does not support it). SS auto-creates the two IfBranches.
- **Named-placeholder syntax** `"Widget:placeholderName"` (e.g. `"DetailsListItem:rightActions"`)
  targets any named placeholder; `"content"` remains the default for containers/forms/lists.
- Hosts without a CreateWidget surface get a **ChangeParent fallback** (create at root → re-parent).
- Builtin function names: **`CurrDateTime()`** (NOT CurrentDateTime()); parses to a valid
  `[Type: Date Time] [Reference: CurrDateTime(...)]` element.

## Anonymous widgets + probes + variable types (proven)

- **`live_delete_anon_block_widgets(module, block, typeContains, recursive?)`** — SS-created
  children (ListItemAction in a list item's `rightActions`, etc.) are UNNAMED and can't be
  addressed by name; delete them by type substring, optionally recursing into child
  widgets/placeholders. Named widgets are never touched.
- **`live_probe_block_cp(module, block, widget, propName)`** — CP internals: expression element
  (`[Type]/[Value]/[Reference]`), `_value`, `_valueType` (runtime type), verify messages.
- **`live_probe_type(typeName)`** — .NET type surface inside SS (statics, props, methods, nested
  types; full name with `+` or suffix). Use before inventing factory patterns.
- **`live_probe_references(module)`** — what each reference exposes: **`User`, `Group`, `Tenant`…
  live under the `(System)` reference's ReferenceEntities**; Diet_CS exposes `Client`, `Progress`;
  OutSystemsUI exposes static-entity records (SideMenuBehavior, Color, Space…).
- **`live_set_block_variable_type`** now resolves: basic types, Structures, **ListTypes**
  (`"User Record List"`, `"Attachment List"`…), and **entity identifiers**:
  `identifier:true` + `type/entityName` + `producerModule:"(System)"` → sets the entity's
  IdentifierType (e.g. LocalVar_Dropdown → User Identifier for a dropdown bound to users).

## Aggregates from consumed references (proven)

`live_add_aggregate_to_block(module, block, name, entityName, producerModule)` creates the
aggregate **with its source** (CreateAddSourceOperation) when the entity is found in the consumed
references — e.g. `entityName:"User", producerModule:"(System)"`. Then bind widgets:
`live_set_block_cp(widget=DetailsList, propName=Source, value="UsersAggregate.List")` (parses to a
valid `User Record List` reference) and Dropdown `List=UsersAggregate.List`,
`Variable=<User Identifier local>`. If the producer module isn't open, `live_probe_references`
tells you which consumed reference exposes the entity.

## Icon widget — the Icon CP is a plain string Value-attr (proven)

The NRWidgets.Icon `Icon` CustomProperty stores the icon name as a **plain string in the
`_value` field with NO ValueExpression** (SS's default is `_value:"flag"`). Setting it via
`live_set_block_cp` (SetValueExpression) parses `info` as an identifier → invalid reference →
verify error `Can't identify 'info' element in expression`.
Correct: **`set_block_cp_value_attr`** (bridge; via `Send-BridgeCmd.ps1`) with
`propName="Icon", value="info"` — clears the ValueExpression and writes the `_value` string.

**Icon NAMES are FontAwesome:** the OutSystems Icon widget renders `fa fa-<name>` at runtime
(`<i class="fa fa-user">`), e.g. `th`, `user`, `clipboard`, `shield`, `archive`, `flag`,
`info`. Do NOT use Material Symbols names — Material Symbols `@import` does NOT work in OS
theme CSS (icons render as literal text). Set the icon via `set_block_cp_value_attr`
(`propName=Icon, value=<fa-name>`); no screen-side twin exists.

## Stale verification cache after raw field writes (proven)

Writing CP backing fields directly (`SetField(cp, "_value", ...)`) **bypasses SS's change
notifications** — the model is correct but SS's error list keeps showing stale errors (e.g. the
icon's "Can't identify 'info'" persisted after the model was already clean).
`set_block_cp_value_attr` now calls **`widget.ForceValidate()` + `InvalidateSelfVerifyCache()`**
after the write so SS re-validates immediately. If you ever add a new raw-field-write command,
do the same. A `Ctrl+S` (full module verification) also flushes stale errors.

## New MCP tools (added 2026-09, after the image/style/bindings sessions)

`live_set_block_cp_text`, `live_set_block_cp_image`, `live_delete_anon_block_widgets`,
`live_probe_block_cp`, `live_probe_type`, `live_probe_references` — plus
`live_set_block_variable_type` gained `identifier`/`entityName` params and ListTypes support.
The MCP server (`mcp/outsystems-liveeditor/publish/`) must be re-published after bridge/code
changes — run `scripts\Update-LiveEditorMcp.ps1` when no opencode session has the server loaded
(the running server locks the publish DLL), then restart opencode (the tool list is frozen per
session). The bridge commands are also drivable directly via `scripts\Send-BridgeCmd.ps1`.

## New MCP tools (added 2026-09-19, ZombieGame DEAD RUN session)

- **`live_set_screen_cp_text(module, screen, widget, propName, value)`** — set a
  TEXT-LITERAL CustomProperty on a SCREEN widget (exactly like `live_set_block_cp_text`
  does for blocks): bare value, no quotes, no parsing, then `InvalidateSelfVerifyCache`
  + `ForceValidate`. **The ONLY correct writer for Style classes on screen containers**
  (see styling-and-css-live for the quote/arithmetic trap table).
- **`live_delete_screen_client_action(module, screen, action)`** — delete a
  SCREEN-level Client Action (`ClientScreenActionFlow`). `live_delete_service_action`
  is module-level only and will not find screen actions.

## Part A � newly exposed bridge commands

`live_set_extended_property` (set an HTML attr e.g. iframe src), `live_set_screen_layout`,
`live_delete_web_flow`, `live_delete_screen_from_flow`, `live_delete_widget`, `live_delete_layout`,
`live_add_html_text`, `live_probe_widget_deep`, `live_probe_layout_ref`, `live_read_user_css_text`,
`live_probe_block_widget`, `live_dump_widget_concretes`. `live_layout_flow` now lays out If
(TrueTarget/FalseTarget) and Switch (OtherwiseTarget) branches into their own columns.
## Decision flow
1. `live_status` → confirm SS + plugin + which pid holds the module.
2. (optional) `live_list_modules` / `live_module_info` → confirm the module + before-count.
3. Create a service action → `live_create_service_action(module, name)`.
4. Confirm in SS (tree updates live) or via a follow-up `live_module_info`.
5. To undo a test action: `Ctrl+Z` in SS. To persist: `Ctrl+S` (live edits are
   in-memory until saved).

## live vs headless — pick the right server
**Live is the DEFAULT.** Headless only on explicit user request.
| Situation | Server |
|---|---|
| Module OPEN in SS, want instant tree update, keep undo history (DEFAULT) | `outsystems-liveeditor` (this) |
| Module not open / SS not running / batch files / CI — **ONLY on explicit user request** | `outsystems-omleditor` (headless, `editor-workflow`) |
| Need a saved `.oml` artifact on disk — **on explicit request** | `outsystems-omleditor` (live is in-memory until `Ctrl+S`) |

Don't confuse the two: `outsystems-omleditor` = files, no SS; `outsystems-liveeditor`
= open module, SS running. Same conceptual operation (create service action), different
runtime.

## Gotchas
- **Requires SS running + plugin + open module.** If `live_status` finds no bridge,
  SS isn't running or the plugin isn't installed/loaded. (Headless tools need no SS.)
- **Auto-discovers the right SS pid** per module (SS is multi-process; the main IDE
  process holds the open modules). You just pass the module name.
- **Live edits are in-memory** until the user saves in SS. For an on-disk artifact,
  use the headless editor or tell the user to `Ctrl+S`.
- **Undoable:** every mutation is a real SS undo unit. `Ctrl+Z` removes it — useful
  for test cleanup.
- **Name uniqueness:** service-action names are unique across action types in a
  module; SS auto-suffixes collisions. Use a clear suffix (e.g. `_BL`) for test actions.
- **Service-action create/clone + flow editing** (set assign value, add output param,
  add/delete assign node, list flow) are tooled as generic primitives — compose any
  edit, no SS restart. Entities/attributes/structures use the same command-open pattern
  (`IESpace.Create*` inside `Command.ExecuteFromAsyncCode`) — see `live-editing` →
  Extending / Flow editing.
- **Failed calls may still create nodes.** Even if a tool returns "FAILED", it may
  have partially executed and created a node in the flow. After ANY failure, run
  `live_list_flow` to check state. See the Recovery Guide in `building-service-action-flows`
  for surgical cleanup steps.
- **Flow-editing tools work on all action types** (service, server, client, and
  screen client actions). The bridge's `FindAction` function searches `ServiceActions`,
  `ServerActions`, and `ClientActions` collections, then falls back to each web-flow
  screen's `ClientActions` (`ClientScreenActionFlow`). Pass any action name to
  `live_list_flow`, `live_debug_create_node`, etc.
- **Follow the step-by-step runbook.** The `building-service-action-flows` skill has
  a proven 14-step process with verification checkpoints. Follow it in exact order —
  skipping steps causes failures. See the Anti-Patterns section in that skill for
  common mistakes.

## Roadmap (live)
Flow building is fully tooled (create/delete/link nodes, multi-assignment, exception
handlers, action-call nodes, input auto-mapping, debug inspection). Server actions and
client actions can be created and flow-edited (all `live_*` flow tools work on any
`IAction` via the `FindAction` lookup). Local variables are tooled (`live_add_local_variable`).

**Proven as of the 2026-09 close-out session (SS 11.55.89):**
- `live_create_service_action` from scratch — **WORKS** (`es.CreateServiceAction`;
  `withSkeleton=true` gives a linked Start→End skeleton; count verified 2→3).
- `live_upload_image` — **WORKS** via `Image.Create(es, bytes, name, null, null)`
  (the UI's own factory). **Images are rejected by design in Service/Library
  modules** (`ImageKind.RuntimeKind = AnyWebOrMobileOrReactive` vs `Service=4`;
  0x7B & 4 = 0) — test in a Web/Reactive module.
- `live_upload_resource` — WORKS (`IESpace.CreateResource`).
- `live_set_object_prop_deep` — WORKS for entity auto-Id(-identifier), attribute
  `DefaultValue` (`DefaultValuePropertyDescriptor` reflection), timer
  `Schedule`/`Timeout`/`Priority` (public props).
- `live_create_role` — WORKS (`IESpace.CreateRole`, auto `Not <Role>` exception).
- `live_grant_screen_permission` — WORKS **and fixed**: grants via
  `Duplicate(Permission)+Role`, then **dedupes** the extra Permission that
  `AddDependentPermissions` adds (verified `1→3` → dedupe → `[ProbeTmpRole, Registered]`).
  `live_read_screen_permissions` / `live_remove_screen_permission` round-trip cleanly.
- `live_publish_module` — WORKS (plain publish only; see Publish section — `commitMessage`
  variant stalls at Uploading).
- `live_open_producer_module` — invokes the UI tab-open command; ok:true (verify tab
  by eye; producer must be closed to fully prove).
- `live_delete_structure` — WORKS (1→0). `live_remove_unused_dependencies` — WORKS
  (correct no-op).

Remaining / known limitations:
- `live_clone_service_action_from` (cross-module) ? **platform-unreachable**: every
  gate passes (licensing=true, mocks+real provider, real-presenter serialization) but
  `PasteObjectsInto` parents nothing ? `GetCommandResult` null ? model
  `ms.Duplicate` fails identically (see note above). Use same-module
  `live_clone_element`/`live_clone_service_action` or create+replay.
- `live_save_module` — **deadlock**: Save is synchronously UI-bound; the pipe hangs
  even stringing the UI dispatcher. Use `live_publish_module` (publishes save first).
- `live_clone_entity` / `live_clone_structure` — not built (generic `live_clone_element` IS built & PROVEN: same-module entity/structure/action via `IModelServices.Duplicate`+rename; verified `Wave`→`ProbeCloneE`, entities 8→9 on ZombieGame_CS; MCP tool `live_clone_element(module, kind, name, newName)`, kind ∈ entity|structure|serviceaction|serveraction|clientaction).
- Raise Event node — **PROVEN** (2026-09-29): `add_event_to_block` (e.g. `ProbeEvt` on block
  `UserInfo` via `WebBlock.CreateEvent`) + `create_block_client_action` (block ClientActions
  4→5) + `add_raise_event_node` → `TriggerEvent` node created AND **bound to the typed
  `WebBlockCustomEvent` in the same command** (the crash-fix works: no auto-open
  "Select Event" modal, no deadlock); `list_flow` read-back shows `[0] ITriggerNode`.
  Cleanup note: no block-client-action/event delete tools yet — in-memory test artifacts
  vanish on close-without-save.
- Timer schedule wiring UI — SS-manual by design (see `app-configuration`).
- Screen permission grant on a **Service module** is impossible (no screens); test on
  Web/Reactive modules (ZombieGame).
