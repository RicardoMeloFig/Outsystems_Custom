---
name: live-editing
description: Use when the user wants to edit an OutSystems module that is OPEN in Service Studio - mutations land in the LIVE tree immediately (no Save/edit/reload). Triggers on "live edit", "edit open module", "live service action", "in-process edit", "no reload". Proven on SS 11.55.81 via the OsLiveBridge plugin + ServiceStudio.Commands.Command.Execute. Read this before any live edit; for file-based headless editing read editor-workflow instead.
---

# Live in-process editing (the open module, no reload)

Edit a module that is **open in Service Studio** so the change appears in the live
tree **immediately** — no `Ctrl+S`, no `.oml` edit, no close/reopen. This is the
in-process counterpart to the headless `.oml` editor (`editor-workflow`). Proven on
SS 11.55.81: a service action created this way appears in the tree and is a real
undo unit (`Ctrl+Z` removes it).

## Behavioral rules (binding for every live edit)

- **Succeeded ≠ landed.** A tool returning ok is not proof. Re-read the object
  before declaring done — verify **values**, not just absence of errors:
  `live_list_flow` after flow edits, `live_probe_*` for properties,
  `get_verify_errors` for expressions/widgets, `live_debug_eSpace_collection_items`
  for entities/timers/roles. Proven failure this caught: `set_timer_action`
  reported success while `Action` stayed null after a restart; the re-read
  exposed it and the re-set stuck.
- **Never guess opaque keys** (module names, element names, node indices,
  entity Ids). Resolve with the read/probe tools first; ask the user when a
  lookup fails. A node index from a previous step is stale — re-probe.
- **Confirm before destructive calls** (`live_delete_*`, aggregate/data-action
  deletion, any publish, overwriting the source `.oml`): restate the planned
  change and wait for explicit confirmation — a generic "go ahead" does not
  authorize a specific destructive call.
- **Error categories drive retries.** "Property not settable" / "No command is
  open" = WRONG SURFACE (fix the surface, don't retry). TrueChange/verify
  errors = WRONG CONTENT (fix the expression/element). Timeouts/restarts =
  LOST STATE (re-probe before continuing; failed calls may have half-landed —
  run `live_list_flow` first).

**Capability is exposed as the `outsystems-liveeditor` MCP** (tools `live_*`). Read
`outsystems-liveeditor` for the tool guide. This skill is the mechanism/reference.

## The breakthrough — the command system

SS refuses model mutations unless a **command** (undo unit) is open, throwing
`InvalidOperationException: No command is open. The model cannot be changed outside
the scope of a command!`. The command-opener was found by decompiling
`ServiceStudio.Framework.dll` (read-only, via a Mono.Cecil probe + `ilspycmd`):

- **Guard:** `UndoManager.OnNew` / `OnDelete` / `Add` (and `ESpace.EnterWriteLock`)
  check `UndoManager.GetCurrentCommand(espace) == null` → throw.
- **State:** the static `UndoManager.currentCommands` dictionary
  (`Dictionary<IBaseTopLevelPresenter, CommandData>`). A command is "open" when an
  entry exists for the aggregator.
- **Opener:** `ServiceStudio.Commands.Command.Execute(presenterContext, description,
  action)` → `InnerStartCommand` → `UndoManager.OnStartedCommand` →
  `UndoManager.InnerStart`, which `currentCommands.Add(aggregator, new CommandData)`
  and calls `StoreBeginState`. The action runs with a command open; `InnerEndCommand`
  → `OnEndedCommand` → `InnerEnd` closes it (removes the entry, fires view refresh).

**The trap:** `aggregator.ExecuteInContext(action, …)` (the obvious-looking API)
does **NOT** open a command — it only marshals the action to the UI thread. Calling
`IESpace.CreateServiceAction` inside it still throws "No command is open". You MUST
use `Command.Execute` (or `ExecuteFromAsyncCode`).

## The verified approach (in `bridge/OsLiveBridge/BridgeHost.cs`)

Builds a `PresenterContext` and calls `Command.ExecuteFromAsyncCode`:

- **v7 (create, synchronous):** `Command.ExecuteFromAsyncCode(presenterContext, desc,
  action)` called **directly from the bridge pipe thread**. `ExecuteFromAsyncCode` is
  designed for non-UI/async callers — it uses locks, not a synchronous UI marshal, so
  it does **not** deadlock. Returns the result immediately. Exposed as
  `live_create_service_action` (bridge `try_create_v7`).
- **v8 (deep-clone, synchronous):** same `Command.ExecuteFromAsyncCode` wrapping, but
  the action calls `IModelServices.Duplicate` instead of `CreateServiceAction`.
  Exposed as `live_clone_service_action` (bridge `try_clone_v8`).

`ExecuteInContext` is **not** a command-opener (it only marshals to the UI thread);
`ExecuteInContext(action, sync=true)` (synchronous) from the pipe thread
**deadlocks** — never use it.

## PresenterContext

`PresenterContext` = `new PresenterContext(aggregator, aggregator)` (public 2-arg
ctor; `source == target` for a single-module mutation). `aggregator` =
`PluginProvider.GetContext(espace)` — returns the concrete
`ServiceStudio.Presenter.AggregatorPresenter` (implements `IBaseTopLevelPresenter`).
(Equivalent to the extension `CommandExtensions.GetPresenterContext(aggregator)`,
which just does `new PresenterContext(tlp, tlp)`.)

The mutation itself: `modelServices.NewKey()` → `IESpace.CreateServiceAction(name,
key)` → returns a `ServiceStudio.Model.Flows+ServiceAPIMethod`.

## The OsLiveBridge plugin (`bridge/OsLiveBridge/`)

A SS plugin (`ServiceStudio.Plugin.OsLiveBridge.dll`, net8.0 class lib) installed at
`Plugins\ServiceStudio\`. Loaded at SS startup via `[ModuleInitializer]` in
`OsLiveBridgeDescriptor` → `BridgeHost.EnsureStarted()` starts a named-pipe server on
a background thread. **Live READ** uses `PluginProvider.ModelServices.LoadedESpaces`;
**live WRITE** uses `Command.ExecuteFromAsyncCode` (above).

- Pipe name: `OsLiveBridge-<SSpid>` (one per SS process). Newline-delimited JSON; one
  request per line, one response per line; `maxNumberOfServerInstances=1`, recreated
  per connection. UTF-8 **no BOM**.
- Commands: `ping`, `list_modules`, `module_info`, `try_create_v7` (live create
  service action), `try_create_server_action` (live create server action),
  `try_create_client_action` (live create client action), `try_clone_v8` (deep-clone),
  and flow-editing primitives — `list_flow`, `set_assign`,
  `add_output`, `add_assign`, `del_node`/`del_node_by_index`, `set_node_target`,
  `set_node_prop`, `add_input`, `add_local_variable`, `add_end_node`,
  `set_exception_handler`, `debug_create_node`, `add_action_call`,
  `add_assignment_to_node`, `set_error_handler_exception`, `map_action_inputs`,
  `debug_node_props`, `debug_action_args`, `probe_node_types`, `delete_service_action`.
  Dependency mgmt: `list_consumable_elements`/`consume_elements`.
  Styling/theme-CSS: `set_style_class` (container→Style CP, link/text→CustomStyle),
  `set_user_css` (public `WebStyleSheet.SetUserCssSource` — what SS's theme CSS editor
  shows), plus diagnostics `probe_style_prop`, `probe_block_widget`, `set_style_cp`,
  `read_theme_css`, `probe_sheet`. See the `styling-and-css-live` skill + `docs/ui-styling-reference.md`.
  Log: `%TEMP%\OsLiveBridge.log`.
- SS is multi-process; the **main IDE** process is the one whose `list_modules` is
  non-empty (it holds the open modules). The MCP auto-discovers it per module.

## Build / deploy / test (quick)

```powershell
# Build the plugin (skip auto-copy; SS locks the DLL while running)
dotnet build "...\bridge\OsLiveBridge\OsLiveBridge.csproj" -c Release -p:SkipCopy=true

# Stop SS, swap the DLL (retry until handle releases), restart
$src="...bridge\OsLiveBridge\bin\Release\ServiceStudio.Plugin.OsLiveBridge.dll"
$dst="C:\Program Files\OutSystems\Service Studio 11\Service Studio\Plugins\ServiceStudio\ServiceStudio.Plugin.OsLiveBridge.dll"
Get-Process ServiceStudio | Stop-Process -Force; Get-Process ServiceStudio | Wait-Process -Timeout 30
Copy-Item $src $dst -Force   # retry on IOException
Start-Process "C:\Program Files\OutSystems\Service Studio 11\Service Studio\ServiceStudio.exe"

# Then test via the outsystems-liveeditor MCP tools (live_status / live_create_service_action)
# or the raw pipe: OsLiveBridge-<SSpid>
```

Server modules (e.g. `MyModule`) have no local `.oml` — reopen manually in SS after a
restart if SS does not auto-restore the session.

## Starting SS / opening a module (the bridge can't open modules)

The bridge only reads/mutates modules that are **already open** in SS — it cannot
open one. There is **no no-UI way** to open a module: no `osp://` protocol is
registered, no SS CLI flag opens a server module, and SS does not reliably
auto-restore modules on a clean start. **Open the module manually in SS** (home
screen → Recent modules → select the module → Enter; saved server credentials in
`Settings.xml` make the connect non-interactive). Then verify via `live_status` /
`live_module_info` (the bridge sees it once loaded, 1–3 s to fetch from the server).

(The earlier FlaUI `opening-modules` automation has been retired — the first baseline
is now extraction-only. Opening is a manual user step; all editing is UI-free via the
live bridge.)

## Crash-recovery dialogs after killing SS (`-recoverCrash` / "work recovered")

**Root cause:** `CrashHandler.exe` is a **separate persistent process** (not
`ServiceStudio.exe`). `Stop-Process ServiceStudio` kills SS but leaves CrashHandler
alive, so it detects a "crash" and (a) relaunches SS with
`-recoverCrash <session> <telemetryFile>` — the telemetry temp file is gone →
`Invalid command line arguments`; and (b) SS sees the AutoSave `.bak` →
`Service Studio closed unexpectedly but your work has been recovered. Open it?`.

**Clean restart (proven — no dialogs):** kill **both** processes + clear recovery state:
```powershell
Get-Process CrashHandler,ServiceStudio | Stop-Process -Force
Remove-Item "$env:LOCALAPPDATA\OutSystems\ServiceStudio 11 XPlatform Stable\AutoSave\*.bak" -Force
'{}' | Set-Content "$env:LOCALAPPDATA\OutSystems\ServiceStudio 11 XPlatform Stable\EditData\PendingChanges.bin"
Start-Process "...\Service Studio\ServiceStudio.exe"
```
(Killing CrashHandler clears its in-memory pending crash; deleting `AutoSave\*.bak`
removes the "work recovered" offer; clearing `PendingChanges.bin` removes the
`-recoverCrash` trigger.) Start cleanly: one `Service Studio` window, no dialogs.

**Best practice:** for plugin swaps, close SS **cleanly** (Win32 `WM_CLOSE` to the
main window = Alt+F4) rather than killing — a clean exit records no crash, so no
CrashHandler recovery at all. Only kill (both processes) when a clean close isn't
possible, then do the clean-restart above.

## Flow editing (nodes, parameters, expressions) — live

Beyond creating/cloning elements, the live editor can edit a flow inside
`Command.ExecuteFromAsyncCode`. Proven on a service action (changed an Assign value,
added a LongInteger output, added an Assign node at the end). Model API (reflection;
all on `IAction` / `IServiceAction`):

**All flow-editing tools work on ANY action type** (service actions, server actions,
client actions, and screen client actions). The bridge's `FindAction` function searches
`ServiceActions`, `UserActions` (server actions), and `ClientActions` collections,
then falls back to each web-flow screen's `ClientActions` (`ClientScreenActionFlow`).
The mutation primitives are `IAction`-level (generic), so once the action is found,
all downstream operations (CreateNode, CreateOutputParameter, CreateLocalVariable, etc.)
work regardless of action type.

- **Nodes:** `action.Nodes` → `IEnumerable<IActionNode>`. Filter by interface
  (`IAssignNode`, `IEndNode` in `OutSystems.Model.Logic.Nodes`). Or
  `action.GetAllDescendantsOfType<T>()` (on `IObjectSignature`).
- **Assign value:** `IAssignNode.Assignments` → `ISequence<IAssignment>`;
  `IAssignment.Value`/`Variable` are `IExpression` with `.Text` (get/set);
  `IAssignment.SetValue(string)` / `SetVariable(string)` set them. Text literals are
  double-quoted in `.Text` (e.g. `"new value"`); numbers bare (`1`).
- **Output parameter:** `action.CreateOutputParameter(name, key)` → `IOutputParameter`;
  set type via `IParameter.DataType { get; set; }` (settable) = `es.LongIntegerType`
  (`IESpace` exposes `TextType`/`IntegerType`/`LongIntegerType`/`DecimalType`/…).
- **Create a flow node:** `action.CreateNode<T>(name, key)` (generic, on `IAction`).
  Reflection: it's an **explicit interface impl** — search the type's *interfaces*
  for a generic `CreateNode` (2 params), `MakeGenericMethod(nodeType)`, invoke.
- **Link a node:** `IAssignNode.Target { get; set; }` = the next node. To insert
  `newNode` before End: find the node whose `Target == End` (the last node), set
  `newNode.Target = End` and `lastNode.Target = newNode`.

Wrap each mutation in `Command.ExecuteFromAsyncCode` (one undo unit). If a mutation
throws inside the action, decide: catch (partial commit) or let propagate
(`Command.Execute` rolls back the whole command). Verify with the first baseline's
ClrMD `get_action_detail` / `list_actions` (read-only, authoritative).

### Direct edit — no SS restart per change

The bridge exposes flow edits as **generic primitives** (`set_assign`, `add_output`,
`add_assign`, `del_node`/`del_node_by_index`, `list_flow`, plus the node-target/prop
tools below), wrapped as MCP tools (`live_set_assign_value`, `live_add_output_param`,
`live_add_assign_node`, `live_delete_node`/`live_delete_node_by_index`, `live_list_flow`,
…). The bridge is deployed **once** with these primitives; after that, **every flow edit
is composed from MCP calls — no recompilation, no SS restart**. Only a plugin
install/upgrade (a new bridge build) needs the clean-restart procedure above. To place a
node in a specific path (e.g. the normal flow, not the exception handler), use
`add_assign` with `where=afterAnchor` and an anchor on the target path
(e.g. `Result.IsError=False` for the normal-flow end) — `list_flow` shows the graph
(index → target) to pick the anchor. `where=beforeEnd` is reliable now too: the
Key-as-string comparison bug that previously nullified all Targets (in
`add_assign beforeEnd`, `del_node_by_index`, `AddEndNode(beforeEnd)`, and
`AddActionCall(beforeEnd)`) is fixed — it uses `ReferenceEquals`.

## Dependency management (live — consume from producer modules)

Beyond creating/cloning elements and editing flows, the live editor can **manage
dependencies** — consume elements from a producer module into the consumer, live.
Proven on SS 11.55.81: consumed 24 elements (16 server actions + 4 entities + 4
folders) from `Diet_CS` into `Diet_BL` in a single undo unit.

### The API — `IESpace.AddDependency<T,S>(IShareable<T,S>)`

The model API for consuming elements is generic:
```csharp
SignatureT AddDependency<ConcreteT, SignatureT>(IShareable<ConcreteT, SignatureT> sourceObj)
```
**15 consumable element types** all implement `IShareable<T,S>`: ServiceActions,
ServerActions, ClientActions, Entities, Structures, Roles, Processes, Scripts,
Images, Resources, WebThemes, MobileThemes, WebFlows, MobileFlows, Folders. All
can be passed to `AddDependency` — the method creates/updates an `IReference`
entry in the consumer's `References` collection.

**12 of 15** have a `bool Public` property (filter to consume only public elements).
The 3 without `Public` (WebFlow, MobileFlow, Folder) are organizational containers.

### Generic method resolution via reflection

`AddDependency` has **two 1-param overloads** (the generic `IShareable<,>` one and
a non-generic `Extensions.IAction` one). The bridge's `CallMethod` (name + paramCount)
can't disambiguate. The `consume_elements` command resolves it by:
1. Finding the method where `IsGenericMethod=true` (walks type + interfaces via `AllTypes`).
2. Extracting `ConcreteT` + `SignatureT` from the source object's closed
   `IShareable<T,S>` interface (`iface.GetGenericTypeDefinition() == ishareableOpen`).
3. `MakeGenericMethod(typeArgs)` → `Invoke(consumerESpace, {sourceObj})`.

This is **type-agnostic** — the same code works for all 15 element types without
type-specific branching. Adding a new consumable type = just adding it to the
`ConsumableCollections` array.

### Batch consumption (single undo unit)

All `AddDependency` calls for one `consume_elements` command happen inside **one**
`Command.ExecuteFromAsyncCode` — a single undo unit (`Ctrl+Z` removes all). Per-element
errors are caught (already-consumed elements are reported as "already consumed", not
errors); the batch continues.

### Bridge commands

- `list_consumable_elements {module}` — enumerates all 15 consumable types in the
  producer (read-only). Returns names + keys grouped by type.
- `consume_elements {consumer, producer, what}` — consumes elements. `what="*"` for
  all; `"ServerAction:*,Entity:*"` for all of specific types;
  `"ServerAction:GetUser,Entity:User"` for specific elements.

Exposed as MCP tools: `live_list_consumable_elements`, `live_consume_elements`.

## When to use live vs headless

**Live is the default.** Headless is used ONLY when the user explicitly
requests it ("edit the .oml file", "no SS", "headless") — never choose
headless on your own initiative.

| Need | Use |
|---|---|
| Module is OPEN in SS; want instant tree update, keep undo history (DEFAULT) | **live** (`outsystems-liveeditor`) |
| Consume dependencies from a producer (both modules open in SS) | **live** (`live_consume_elements`) |
| Module not open / SS not running / batch-edit many files / CI — **ONLY on explicit user request** | **headless** (`outsystems-omleditor`, `editor-workflow`) |
| Need a saved `.oml` artifact on disk | headless on request (live edits are in-memory; `Ctrl+S` to persist) |

Live edits are in-memory until the user saves in SS. For a verifiable on-disk
artifact, the user may explicitly request the headless editor.

## Extending to new element types (entities, attributes, …)

The headless editor creates elements by editing `.oml` fragments. The live editor
creates them via the **model API** (`IESpace.CreateServerEntity`,
`CreateStructure`, …) inside `Command.ExecuteFromAsyncCode`. To add a new live
operation:
1. Confirm the `IESpace.Create*` method (reflection: `OutSystems.Model.V1.IESpace`,
   or decompile with `ilspycmd -t "OutSystems.Model.IESpace" -r "$ss" "$ss\OutSystems.Model.V1.dll"`).
2. Wrap it in `Command.ExecuteFromAsyncContext(presenterContext, desc, () => es.Create*(…))`.
3. Add a bridge command + an MCP tool (mirror `try_create_v7` / `live_create_service_action`).

For **dependency management** (consuming elements), the pattern is different —
`IESpace.AddDependency<T,S>(IShareable<T,S>)` is a **generic** method that accepts
any shareable element from a producer. See the "Dependency management" section above.
The reflection resolves type args from the source object's `IShareable<,>` interface,
making it type-agnostic. `ilspycmd -l i "$ss\OutSystems.Model.V1.dll"` lists all
interfaces (to find the right namespace/arity for new types).

The mutation is undoable automatically (it went through the command system).

## Deep-cloning any element (`IModelServices.Duplicate`)

To replicate an existing element **exactly** (params, flow, metadata) live, use
`IModelServices.Duplicate(IObjectSignature source, IObject parent)` inside
`Command.ExecuteFromAsyncCode` — the same serialize→deserialize mechanism SS
copy/paste uses. The clone gets a fresh key; rename it via the element's `Name`
setter. The returned `IObjectSignature` IS the new object (it implements
`IServiceAction`/`IObject`, which expose `Name`). Proven for service actions
(`live_clone_service_action` / bridge `try_clone_v8`): cloned `SourceAction`→
`ClonedAction` in `MyModule`, exact copy confirmed via ClrMD `get_action_detail`.
`Duplicate` works on any `IObjectSignature` (entities, structures, …) — a generic
`live_clone_element` is the natural generalization.

## Flow editing tools (node-level)

Beyond the assign/output/assign-node primitives, the bridge exposes node-level tools
for arbitrary node types. These are standard MCP tools:

- **`live_delete_node_by_index`** (bridge `del_node_by_index`) — delete a node by its
  `NodeList` index. Works for **all node types** (Assign, ExecuteAction, End,
  ErrorHandler, Start, Comment), unlike `live_delete_node` which matches Assign nodes
  by var/value. Relinks the previous node to the deleted node's target.
- **`live_set_node_target`** (bridge `set_node_target`) — set
  `node[nodeIndex].Target = node[targetIndex]`. The deterministic way to wire flow
  nodes; use after `live_list_flow` confirms actual indices.
- **`live_set_node_property`** (bridge `set_node_prop`) — set any settable string
  property on a node by name. For node references (e.g. `Action` on ExecuteAction),
  pass the server action name string — the bridge resolves it internally. For typed
  properties (e.g. `Exception` on ErrorHandler) it fails with a type mismatch — use
  `live_set_error_handler_exception` instead.
- **`live_probe_node_types`** (bridge `probe_node_types`) — enumerate all `I*Node`
  interface types in the action's `NodeList`. Use to discover exact interface names at
  runtime before `live_debug_create_node`.
- **`live_debug_create_node`** (bridge `debug_create_node`) — create a flow node by
  interface short name (e.g. `"IStartNode"`, `"IExceptionHandlerNode"`,
  `"IExecuteServerActionNode"`). The bridge searches
  `OutSystems.Model.Logic.Nodes.<shortName>` and creates via the explicit-interface
  `CreateNode<T>` method.
- **`live_delete_service_action`** (bridge `delete_service_action`) — delete a
  ServiceAPIMethod by name (`action.Delete()` inside `Command.ExecuteFromAsyncCode`).
- **`live_add_assignment_to_node`** (bridge `add_assignment_to_node`) — add a 2nd
  assignment to an existing `IAssignNode` (multi-assignment; an Assign node holds a
  sequence of `IAssignment` entries).
- **`live_set_error_handler_exception`** (bridge `set_error_handler_exception`) — set
  `Exception = All Exceptions` on an ErrorHandler node (resolves the
  `es.AllExceptions` SystemException). See "Exception Handler Architecture" below.
- **`live_map_action_inputs`** (bridge `map_action_inputs`) — auto-map an
  ExecuteAction node's input arguments by name against the consumed server action's
  parameters.
- **`live_debug_node_props`** (bridge `debug_node_props`) — inspect a node's type and
  settable properties (reflection aid).
- **`live_debug_action_args`** (bridge `debug_action_args`) — inspect an ExecuteAction
  node's arguments.

## Node Index Ordering (important caveat)

**Created nodes do not appear at expected indices.** The SS model creates nodes
in its internal order, not creation order. After `live_debug_create_node` calls,
**always verify with `live_list_flow`** and re-check types with `live_debug_node_props`
before linking.

Example: created nodes in order `Start, ExecuteAction, Assign, End, ErrorHandler,
Assign, End` but actual indices were:
```
[0] ExecuteAction
[1] Assign
[2] Assign
[3] Start
[4] ErrorHandler
[5] End
[6] End
```

## Exception Handler Architecture (critical)

- **`Start.ExceptionHandler` does NOT exist** — not even as a read-only property. Do
  not try to wire the exception path through the Start node.
- **`ExecuteAction` has NO `ExceptionHandler` property either** — setting it throws
  `property not settable: ExceptionHandler on ServiceStudio.Model.Nodes+ExecuteAction`.
- The ErrorHandler node (`IExceptionHandlerNode`) is the **START of the exception
  flow**. SS auto-recognizes the `IExceptionHandlerNode` in the flow as the exception
  handler and routes exceptions to it automatically — no explicit wiring to Start or
  ExecuteAction is needed.
- The `Exception` property on ErrorHandler **CAN** be set — use
  `live_set_error_handler_exception` (sets `Exception = All Exceptions` by resolving
  the `es.AllExceptions` SystemException). `live_set_node_property` cannot set it
  (typed property).

Correct flow:
```
Normal:  Start → ExecuteAction → Assign(success) → End(normal)
Error:   ErrorHandler → Assign(error) → End(exception)
```

See `exception-handlers-in-service-actions` skill for full details.

## Gotchas

- **`ExecuteInContext` is not the command-opener.** Only `Command.Execute` /
  `ExecuteFromAsyncCode` open a command. This was the entire blocker.
- **`ExecuteInContext(sync=true)` deadlocks** from a non-UI thread (synchronous UI
  marshal). Use `sync=false` (async) or — preferred — call
  `Command.ExecuteFromAsyncCode` directly (no marshal, no deadlock).
- **SS locks the plugin DLL while running.** Stop SS (`Stop-Process -Force` +
  `Wait-Process`) before swapping; retry the copy until the handle releases.
- **UTF-8 BOM** breaks the pipe — use `new UTF8Encoding(false)`.
- **`dynamic` binding fails** for SS explicit-interface-impl methods; use reflection
  with interface search (the bridge's `GetProp`/`FindMethod`/`CallMethod`).
- **Multi-process SS:** the bridge loads in every SS process; target the one whose
  `list_modules` is non-empty. The MCP handles this automatically.
- Live edits are **undo units** — `Ctrl+Z` in SS removes them (good for test
  cleanup). They are in-memory until saved.
- **`add_end_node(..., "atEnd")` fails on empty flows** — the "last node" is a
  Comment placeholder, and End.Target is read-only. Use
  `live_debug_create_node("IEndNode")` + `live_set_node_target` explicitly.
- **`set_assign` requires an existing assignment to match** — if the node was
  created empty, it returns "NOT FOUND". Use `add_assign` for empty nodes.

- `tools/CommandProbe/` — Mono.Cecil static analyzer. Modes: (default) scan all SS
  DLLs for a string in IL + dump IL + find callers; `string <sub>`, `il <type> <m>`,
  `callers <type> <m>`, `calls <type> <m>`, `writes/reads <type> <field>`,
  `type <type>`, `find <methodName>`, `findtype <name>`. Read-only, no SS needed.
- `ilspycmd` (global dotnet tool) — readable C# per type:
  `ilspycmd -t <FullTypeName> -r "<SS dir>" "<SS dir>\ServiceStudio.Framework.dll"`.
- `tools/LiveModelProbe/` — reflection probe over the SS DLLs (the `command` mode
  surveys the command/undo/aggregator API surface).
