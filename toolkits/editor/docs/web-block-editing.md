# Web block editing (live) — reference

How to create and edit **web blocks** in an OPEN module via the `outsystems-liveeditor`
MCP server (live, in-process, no reload). All mutations are single SS commands (undo
unit, `Ctrl+Z`). Proven on SS 11.55.81 against `FitnessManager` → `ZombieHeader`
in the `zombieGame` flow.

Read `live-editing` first (bridge mechanism), then the `outsystems-liveeditor` skill.

## Where web blocks live

A web block is a **node inside a WebFlow's `Nodes` collection** (the other node kind is
a screen). Its content is:

- the **widget tree** — `IObjectWithWidgets.Widgets` on the block (containers, text,
  expressions, links, html, placeholders);
- its **Variables** — `LocalVariable` objects on the block's `Variables` collection;
- placeholders — `Placeholder` widgets in the tree that screens can fill.

`live_list_web_flows` lists flows + screens (not blocks); use `live_list_block_widgets`
/ `live_dump_block_widget_types` to read a block's tree.

## Tools

| Tool | What it does |
|------|-------------|
| `live_create_web_block(module, flow, name)` | Create a web block **from scratch** (NO clone/move) inside a web flow. Hunts a `Create*`/`New*` factory on the eSpace or the flow and invokes it inside `Command.ExecuteFromAsyncCode`, then adds the node to the flow's `Nodes` if needed. Proven factory: `IUIFlow.CreateBlock(name, key)` → `ServiceStudio.Model.NRNodes.WebBlock`. |
| `live_list_block_widgets(module, block)` | Read-only: dump the block's widget tree (names + concrete types). |
| `live_dump_block_widget_types(module, block)` | Read-only: every widget with concrete type **and all interfaces**, including anonymous widgets (shown as `(anon)`). Use to discover the exact concrete type SS uses (e.g. `[Expression]` vs `[Text]`). |
| `live_add_widget_to_block(module, block, parent, kind, name, value?, styleClass?)` | Add any widget to the block root or a named container (`parent`). `kind` ∈ container\|text\|expression\|link\|html\|placeholder. `value` applies to text/expression (for expressions, pass the expression reference, e.g. the variable name). |
| `live_add_placeholder_to_block(module, block, parent, name)` | Add a **Placeholder** widget (concrete `NRWebWidgets.Placeholder`) so screens can fill it. |
| `live_add_variable_to_block(module, block, name)` | Create a **Local Variable** on the block. Proven factory: `WebBlock.CreateLocalVariable(name, key)` → `LocalVariable`. |
| `live_set_block_widget_property(module, block, widgetName, propName, propValue)` | Set a string property on a block widget (mirror of `live_set_widget_property` on screens). For an Expression's value: `propName='Value'` → falls back `SetValue` → `SetProp`. |
| `live_delete_widget_from_block(module, block, name)` | Delete a widget (and its children) from the block's tree. |
| `live_clone_web_block(module, sourceName, newName)` | Deep-clone an existing block via `IModelServices.Duplicate` (exact copy, fresh key, renamed). Clone lands in the **source flow** — follow with `live_move_web_block_to_flow` if needed. |
| `live_move_web_block_to_flow(module, block, targetFlow)` | Reparent an existing block into another web flow. |
| `live_add_button_to_block(module, block, parent, name, text, styleClass)` | Add a REAL Button inside a web block (or a container in it). Same mechanism as screens (NRWidgets `ButtonDescriptor` → `CreateWidget(descriptor)`), resolved via `FindBlock` — **the screen-only `live_add_button` rejects block names** (`FindScreen` requires `IScreen`). |
| `live_set_block_button_onclick(module, block, button, actionName)` | Wire a block's Button OnClick → an action (block `ClientActions` first, then module; falls back to `SetOnClickHandler`). |
| `live_list_block_input_params(module, block)` | Read-only: block's InputParameters (name + type). |
| `live_add_input_param_to_block(module, block, name, type)` | Create an input parameter on the block (proven: `WebBlock.CreateInputParameter`; basic types + Structures). |
| `live_set_block_input_param_type(module, block, paramName, typeName, producerModule, entityName)` | Set a block input param's DataType (basic/Structure/entity-record/entity-Identifier). |
| `live_remove_input_param_from_block(module, block, paramName)` | Remove a block input parameter. |
| `live_add_entity_input_to_block` / `live_add_entity_identifier_input_to_block` | Entity-record / entity-Identifier typed block input params (from a consumed producer). |
| `live_add_if_node(module, action, condition, afterNodeIndex?)` | Create an `IIfNode` decision node; sets the condition via **`SetCondition(string)`** (the `Match`/`ForcedMatchOrMatch` props are `ServiceStudio.Merge.Match` objects — not string-settable). |
| `live_add_switch_node(module, action, afterNodeIndex?)` | Create an `ISwitchNode` (case conditions set per-case). |
| `live_probe_node_connectors(module, action, nodeIndex)` | Read-only: list branch props — If → `TrueTarget`/`FalseTarget`; Switch → `OtherwiseTarget` (explicit interface props, NOT `Target`). |
| `live_set_connector_target(module, action, nodeIndex, propName, targetIndex)` | Set a named branch prop to another node (what `live_set_node_target` cannot reach). |
| `live_probe_block_members(module, block)` | Read-only: block properties + collections (`CustomEvents`, `InputParameters`, `LocalVariables`, `ClientActions`, `DataActions`, `ScreenAggregates`) with counts + first-item types. |
| `live_add_event_to_block(module, block, name)` | Custom EVENT on the block (proven: `WebBlock.CreateEvent` → `WebBlockCustomEvent`) so the block signals the parent. |
| `live_add_event_param_to_block(module, block, event, name, type)` | Event payload param (proven: `CreateInputParameter`). |
| `live_set_block_handler(module, block, handler, actionName)` | Lifecycle handlers (OnInitialize/OnReady/OnRender/OnParametersChanged/OnDestroy) are `NREvents.*` children owning their own flow (not separate-action props). Tool reports this; `FindAction` resolves them by name so flow tools edit the handler's flow directly. |
| `live_add_raise_event_node(module, action, eventName?, afterNodeIndex?)` | ⚠ Attempts Raise-Event node creation via fail-fast interfaces only. `CreateNode<ITriggerNode>`/node-creation on `NREvents.*` children **deadlocks the bridge pipe** (needs an SS restart to unstick) — do NOT call those. |

## Expression vs Text — the proven pitfall

A real Reactive **Expression** is the NRWidgets plugin custom widget
`ServiceStudio.Plugin.NRWidgets.Expression` (tree shows `[Expression]`), created
through the interface `ServiceStudio.Plugin.NRWidgets.IExpression`. Using the older
fallbacks (`OutSystems.Model.UI.Web.Widgets.IExpressionWidget` /
`OutSystems.Model.UI.Mobile.Widgets.ITextWidget`) yields a plain
`ServiceStudio.Model.NRWebWidgets.Text` (`[Text]`) — a static label, NOT an
expression that can reference variables.

`CreateWidgetByKind` (`WidgetKindCandidates`) tries `ServiceStudio.Plugin.NRWidgets.IExpression`
**first** for kind `expression`. Always verify with `live_dump_block_widget_types` — concrete
type must be `...NRWidgets.Expression`.


## Data fetching on web blocks (aggregates / data actions) - Phase 4 (proven live)

Web blocks (and screens) expose two DATA-SOURCING collections, both confirmed live via
`probe_block_members`: **`DataActions`** (`DataScreenActionFlow`) and **`ScreenAggregates`**
(`WebScreenDataSet`). These are how a reactive block fetches entities client-side.

| Tool | What it does |
|------|-------------|
| `live_probe_data_sources(module, block?, screen?)` | Read-only: probe both collections - concrete types, count, existing items, **and the factory methods** on the collection + the block/screen. Proven factories: `CreateScreenAggregate(Boolean,String,IKey)` on block/screen, `CreateDataAction(String,IKey)`. |
| `live_add_aggregate_to_block` / `live_add_aggregate_to_screen` | Create a **Screen Aggregate - SERVER FETCH** (proven factory: `CreateScreenAggregate(false, name, key)` -> `WebScreenDataSet`). IMPORTANT: the Boolean is `isClientSide` and MUST be `false` — `true` creates a client-side/local aggregate which corrupts the module. Optional source entity from a consumed reference (`FindEntityInReferences`). |
| `live_add_data_action_to_block` / `live_add_data_action_to_screen` | Create a **Data Action** (proven factory: `CreateDataAction(name, key)` -> `DataScreenActionFlow`). It's a client-side flow - edit its flow with all `live_*` flow tools. |
| `live_delete_aggregate` / `live_delete_data_action` | Delete by name from a block or screen. |

> Source entity: `SetAggregateSource` tries `CreateSource(source)` /
> `CreateAddSourceOperation + Source` / `Sources.Add(CreateSource(name))`. FitnessManager
> has 0 local entities and its producer modules are not open, so the entity arm is best-effort -
> create without `entityName` first, then wire the source via an entity from an open producer.

## Input / display / container widgets - Phase 5 (proven live, 19 kinds)

`live_add_nr_widget(module, screen?, block?, parent?, kind, name, styleClass?, text?)`
creates **any Reactive custom widget** using the Button-proven descriptor pattern:
`<Kind>+Kind.Instance.Descriptor` -> `CreateWidget(descriptor)` -> name + style. Kind ->
NRWidgets concrete class map (`NRWidgetKindClasses`):

| kind | class | kind | class |
|------|-------|------|-------|
| button label input textarea checkbox dropdown radio radio-group switch form | Button / Label / Input / TextArea / Checkbox / Dropdown / RadioButton / RadioGroup / Switch / Form | list table image icon button-group container expression link html | List / TableRecords / Image / Icon / ButtonGroup / Container / Expression / Link / AdvancedHtml |

`text` sets initial value/label for label/input/textarea (widget `Text` property or link
label). Widgets create as the real plugin custom widget (`ServiceStudio.Plugin.NRWidgets.<Kind>`).

`live_probe_widget_kinds(module)` - read-only: enumerate every NRWidgets plugin kind at
runtime (class + nested `+Kind` + its Descriptor), useful when extending the map.

**Wire interactive handlers on any widget:** `live_set_widget_handler(module, screen?,
block?, widget, event, actionName)` routes a widget event (OnChange/OnFocus/OnBlur/OnClick)
to a client action. Input/Dropdown/etc. expose builtin-event properties (e.g. `Input.OnChange`
-> `IBuiltinEvent` with settable `Destination`); OnClick uses the button `SetOnClickHandler`
fallback. **Destination must be a screen-level `ClientScreenActionFlow`** (create with
`live_create_screen_client_action`) - a global `ClientActionFlow` is NOT assignable.

## Element description

`live_set_element_description(module, kind, name, description)` sets the writable
`Description` property on a **block**, **screen**, or **action** (any type) - live, undo
unit (confirmed writable via `probe_block_members`).


## Deploying a new bridge command (once per change)

Adding a **new bridge command** (e.g. `create_web_block`, `add_widget_to_block`,
`add_variable_to_block`, `set_block_widget_property`) requires:

1. Edit `bridge/OsLiveBridge/BridgeHost.cs` (add the handler + a `case` in `HandleJson`).
2. Optionally register the MCP tool + forwarder in `mcp/outsystems-liveeditor/Program.cs`.
3. Rebuild: `dotnet build bridge/OsLiveBridge/OsLiveBridge.csproj -c Release`
   (its `CopyToPlugins` target copies to `Plugins\ServiceStudio\`). Compile-check with
   `-p:SkipCopy=true` while SS is running.
4. **Restart Service Studio** (SS locks the plugin DLL while running) and reopen the module.
5. The MCP tool list is frozen per opencode session — to use a just-added command without
   restarting opencode, drive it via
   `scripts\Send-BridgeCmd.ps1 -Module <m> -Cmd <cmd> -JsonArgs '{...}'`.
6. `Ctrl+S` in SS to persist the live edits to the `.oml`.

## Cmd↔tool map (bridge commands behind the MCP tools)

| Bridge command | MCP tool |
|---------------|----------|
| `create_web_block` | `live_create_web_block` |
| `add_placeholder_to_block` | `live_add_placeholder_to_block` |
| `add_widget_to_block` | `live_add_widget_to_block` |
| `delete_widget_from_block` | `live_delete_widget_from_block` |
| `dump_block_widget_types` | `live_dump_block_widget_types` |
| `add_variable_to_block` | `live_add_variable_to_block` |
| `set_block_widget_property` | `live_set_block_widget_property` |
| `clone_web_block` / `move_web_block_to_flow` | `live_clone_web_block` / `live_move_web_block_to_flow` |
