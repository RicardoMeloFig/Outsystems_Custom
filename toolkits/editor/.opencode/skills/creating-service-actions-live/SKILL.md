---
name: creating-service-actions-live
description: Use when the user wants to create a Service Action in an OutSystems module that is OPEN in Service Studio (live, in-process, no reload). Triggers on "create service action live", "add service action in open module", "build service action flow", "create service action with exception handling". Proven on SS 11.55.81 via OsLiveBridge + Command.ExecuteFromAsyncCode. Read live-editing first.
---

# Creating Service Actions Live (in-process, no reload)

> **STATUS 2026-09-16 (SS 11.55.83): live service-action CREATION is BLOCKED by a
> platform validation ("Service Action objects can't be children of Module objects").
> Exhaustively proven over 5 deploy cycles: direct factory, collection Create/Add,
> every ctor × every parent (module/adapter/folder × null/name), ObjectKey path
> (`NewKey()` yields a non-convertible `KeyImplementation` wrapper). The UI creates
> them through its command framework (tree-selection context) with no headless
> equivalent found to date. WORKAROUND: build all logic as SERVER actions
> (`live_create_server_action` ✅ + identical flow tooling) or screen/block client
> actions — service actions add cross-module exposure only. Micro-lead on file:
> unwrap `KeyImplementation` → real `ObjectKey` → key-ctor → collection Add.
> Cloning (`live_clone_service_action`) still works wherever a source action exists.
> The flow-building runbook below applies UNCHANGED to server actions
> (all `live_*` flow tools resolve any action type via FindAction).

Create a Service Action in a module that is **open in Service Studio** — the
mutation lands in the live tree **immediately**, no Save/edit/reload, and is a
proper SS undo unit (`Ctrl+Z` removes it).

**Read `live-editing` first** — it covers the command-system breakthrough
(`Command.ExecuteFromAsyncCode` is the only command-opener), `PresenterContext`,
and the OsLiveBridge plugin architecture.

## Two Approaches

### Approach A: Clone (fastest, for replicating an existing action)

When a working reference action exists in the same module:

```
live_clone_service_action(module, "ReferenceAction", "NewAction")
```

Produces an exact copy (params, flow, metadata) with a fresh key. Single undo unit.

### Approach B: Manual Build (for custom flows — PROVEN)

Build the flow node-by-node. This is the **generic approach** that works for any
flow structure. All tools below are MCP tools (no raw pipe needed).

## Proven Step-by-Step Process

### Step 1: Consume dependencies

```
live_consume_elements(consumer, producer, "ServerAction:ActionName,Entity:EntityName")
```

Consume the server actions and entities the new service action will use.

### Step 2: Create the action shell

```
live_create_service_action(module, name)
```

**IMPORTANT:** This creates an empty action with only a **Comment placeholder**
node — NOT a Start→End flow. You must create Start and End nodes yourself.

### Step 3: Add input/output parameters

```
# Entity-typed input (from a consumed producer module)
live_add_entity_input(module, action, "Client", "Client", "Diet_CS")

# Output with a Structure type (add as placeholder first, then fix the type)
live_add_output_param(module, action, "Result", "Text")       # placeholder
live_set_output_param_type(module, action, "Result", "Result") # set to Structure "Result"

# Basic-type input
live_add_input_param(module, action, "UserId", "LongInteger")
```

### Step 4: Create flow nodes (all standalone, linked manually)

Create each node using `live_debug_create_node`. Nodes are created **standalone**
(no Target, no incoming links) — you wire them in Step 6.

```
live_debug_create_node(module, action, "IStartNode")           # Start
live_debug_create_node(module, action, "IEndNode")             # End (normal)
```

Link Start→End so `live_add_action_call_node` can insert before End:

```
# First, identify node indices with live_debug_node_props
live_debug_node_props(module, action, 0)  # check each index
live_debug_node_props(module, action, 1)
live_debug_node_props(module, action, 2)
# Find which is Start and which is End, then link:
live_set_node_target(module, action, startIdx, endIdx)
```

### Step 5: Add the ExecuteAction (server action call)

> **PREREQUISITE:** Step 4 must be complete — Start and End nodes must exist AND
> be linked (`live_set_node_target(startIdx, endIdx)`). `where="beforeEnd"` finds
> the End node and inserts before it. If no End node exists or Start→End is not
> linked, this will fail with `"End/prev not found"`.
>
> **Do NOT use `where="afterAnchor"` here** — there are no Assign nodes yet.
> `afterAnchor` ONLY matches existing **Assign nodes** by their var+value. It
> does NOT work with Comment, Start, End, ExecuteAction, or any other node type.

```
live_add_action_call_node(module, action, where="beforeEnd", serverActionName="ClientCreate", producerModule="Diet_CS")
```

This creates the IExecuteServerActionNode, sets its Action property to the
consumed server action, and inserts it before the End node. **This is the only
way to create a configured ExecuteAction** — `live_debug_create_node` creates an
empty node without the Action reference.

### Step 6: Create remaining nodes

```
live_debug_create_node(module, action, "IAssignNode")          # success Assign (empty)
live_debug_create_node(module, action, "IAssignNode")          # error Assign (empty)
live_debug_create_node(module, action, "IEndNode")             # End (exception)
live_debug_create_node(module, action, "IExceptionHandlerNode") # ErrorHandler
```

### Step 7: Identify ALL node indices

**Node indices are NOT creation order.** SS assigns indices internally. You MUST
identify each node before linking:

```
# Check every node index
live_debug_node_props(module, action, 0)
live_debug_node_props(module, action, 1)
# ... for all nodes
```

The `nodeType` in the response tells you which is Start, ExecuteAction, Assign,
End, ErrorHandler, Comment.

### Step 8: Link all nodes

```
live_set_node_target(module, action, startIdx, execIdx)         # Start → ExecuteAction
live_set_node_target(module, action, execIdx, successAssignIdx) # ExecuteAction → Assign(success)
live_set_node_target(module, action, successAssignIdx, normalEndIdx) # Assign(success) → End(normal)
live_set_node_target(module, action, errorHandlerIdx, errorAssignIdx) # ErrorHandler → Assign(error)
live_set_node_target(module, action, errorAssignIdx, exceptionEndIdx) # Assign(error) → End(exception)
```

### Step 9: Add assignments (multi-assignment per node)

Each Assign node starts **empty**. Use `live_add_assignment_to_node` to add
assignments one at a time. A single Assign node can hold multiple assignments.

```
# Success Assign: Result.IsError=False, Result.Message="Success"
live_add_assignment_to_node(module, action, successAssignIdx, "Result.IsError", "False")
live_add_assignment_to_node(module, action, successAssignIdx, "Result.Message", "\"Success\"")

# Error Assign: Result.IsError=True, Result.Message=AllExceptions.ExceptionMessage
live_add_assignment_to_node(module, action, errorAssignIdx, "Result.IsError", "True")
live_add_assignment_to_node(module, action, errorAssignIdx, "Result.Message", "AllExceptions.ExceptionMessage")
```

**Value quoting rules:**
- Booleans: bare — `False`, `True`
- Text literals: include double quotes in the value — pass `"Success"` (the double quotes are part of the value, do NOT add single quotes)
- Identifiers/expressions: bare — `AllExceptions.ExceptionMessage`

**IMPORTANT:** The `AllExceptions.ExceptionMessage` assignment on the error
Assign must be added AFTER the exception flow is wired (Step 8) and the
ErrorHandler's Exception property is set (Step 10), so the expression resolves
in the exception context.

### Step 10: Set the exception handler

```
live_set_error_handler_exception(module, action, errorHandlerIdx)
```

This sets the ErrorHandler's `Exception` property to `SystemException [All
Exceptions]` (found via `es.AllExceptions` or copied from a reference action's
ErrorHandler). Also sets `AbortTransaction=true` and `LogError=true`.

**CRITICAL:** `Start.ExceptionHandler` does NOT exist as a settable property.
SS auto-recognizes the `IExceptionHandlerNode` as the flow's exception handler
based on its type and `Exception` property. Do NOT try to set
`Start.ExceptionHandler` — use `live_set_error_handler_exception` instead.

### Step 11: Map input arguments

```
live_map_action_inputs(module, action, execIdx)
```

This auto-maps the ExecuteAction's input arguments to the service action's input
parameters. It tries name matching first; when names don't match (e.g. server
action uses "Source" but service action uses "Client"), it falls back to trying
each input parameter name via `SetValue`.

### Step 12: Delete the Comment placeholder

```
# Find the Comment node index via live_debug_node_props
live_delete_node_by_index(module, action, commentIdx)
```

The `del_node_by_index` bug (Key-as-string comparison nullifying all Targets)
is **fixed** — it now uses `ReferenceEquals` for reliable node matching.

### Step 13: Layout the flow (auto-position nodes)

```
live_layout_flow(module, action)
```

Nodes are auto-positioned on creation (rough heuristic — each new node gets a Y
based on creation order, ExceptionHandler nodes go right). But after the flow is
fully built and linked, call `live_layout_flow` for a proper topology-based
layout: main flow in the left column (X=3200), exception flows in the right
column (X=12800), Y increments downward per node.

### Step 14: Verify and save

```
live_list_flow(module, action)           # verify topology
live_get_node_positions(module, action)  # verify layout (nodes not stacked)
# Then Ctrl+S in SS to persist
```

## Node Types (available via live_debug_create_node)

| Interface | Creates | Notes |
|-----------|---------|-------|
| `IStartNode` | Start | Flow entry point |
| `IExecuteServerActionNode` | ExecuteAction | Calls a consumed server action (use `live_add_action_call_node` instead — it configures the Action property) |
| `IAssignNode` | Assign | Assignment(s) on one node (starts empty, use `live_add_assignment_to_node` to populate) |
| `IEndNode` | End | Flow terminator |
| `IExceptionHandlerNode` | ErrorHandler | Start of exception flow (configure with `live_set_error_handler_exception`) |

## Complete Tool Reference

| Tool | Purpose |
|------|---------|
| `live_create_service_action` | Create empty service action (Comment placeholder only) |
| `live_create_server_action` | Create empty server action (UserAction) |
| `live_create_client_action` | Create empty client action |
| `live_clone_service_action` | Deep-clone a service action (exact copy) |
| `live_consume_elements` | Consume server actions + entities from producer |
| `live_add_entity_input` | Add entity-typed input parameter |
| `live_add_entity_identifier_input` | Add entity Identifier-typed input (for Delete actions) |
| `live_add_input_param` | Add basic-type or structure-type input parameter |
| `live_add_output_param` | Add output parameter (placeholder type) |
| `live_add_entity_output` | Add entity-typed output parameter |
| `live_set_output_param_type` | Fix output parameter type to Structure or entity |
| `live_add_local_variable` | Add a local variable to any action |
| `live_debug_create_node` | Create a flow node by interface name (standalone) |
| `live_add_action_call_node` | Create + configure ExecuteAction (sets Action property) |
| `live_set_node_target` | Link node[nodeIndex].Target = node[targetIndex] |
| `live_add_assignment_to_node` | Add an assignment to an existing IAssignNode (multi-assignment) |
| `live_remove_assignment` | Remove a single assignment from an IAssignNode (clean up leftovers) |
| `live_set_error_handler_exception` | Set Exception=All Exceptions on ErrorHandler |
| `live_map_action_inputs` | Auto-map ExecuteAction input arguments by name |
| `live_debug_node_props` | Inspect node type + settable properties (identify nodes) |
| `live_debug_action_args` | Inspect ExecuteAction arguments (diagnostic) |
| `live_list_flow` | Dump flow graph (verify topology) |
| `live_delete_node_by_index` | Delete any node by index (all types, bug fixed) |
| `live_delete_service_action` | Delete a service action by name |

## Gotchas

- **`live_create_service_action` creates only a Comment placeholder**, NOT a
  Start→End flow. You must create Start and End nodes yourself.
- **Node indices are NOT creation order.** Always use `live_debug_node_props` to
  identify nodes before linking.
- **`live_add_assign_node` creates a NEW node with ONE assignment.** For
  multi-assignment nodes (2+ assignments on one node), use
  `live_add_assignment_to_node` on an existing node instead. NEVER create
  dummy/placeholder assignments (Temp=False) as anchors — use
  `live_remove_assignment` to clean up any leftovers.
- **`live_add_input_param` is NOT idempotent.** Calling it twice with the same
  name creates duplicate parameters. The bridge now rejects duplicates, but
  always verify before adding.
- **Entity Identifier types for Delete actions.** Use
  `live_add_entity_identifier_input(module, action, name, entityName, producerModule)`
  for Delete action Id parameters — NOT `LongInteger`. The tool resolves the
  entity's Identifier type from its `Id` attribute.
- **`Start.ExceptionHandler` does NOT exist.** Exception handling is via the
  ErrorHandler's `Exception` property — use `live_set_error_handler_exception`.
- **Input parameter names may differ** between the server action and the service
  action. `live_map_action_inputs` handles this with a fallback strategy.
- **Save the module** (`Ctrl+S`) when done — live edits are in-memory until saved.
  If SS is restarted without saving, all live edits are lost.
- **Failed calls may still create nodes.** Even if a tool returns "FAILED", it
  may have partially executed and created a node in the flow. After ANY failure,
  run `live_list_flow` to check the current state. If phantom nodes exist,
  delete them with `live_delete_node_by_index` before proceeding. See the
  Recovery Guide in `building-service-action-flows` for surgical cleanup steps.
- **`afterAnchor` ONLY works with Assign nodes.** It matches an existing Assign
  node's variable+value substrings. It does NOT work with Comment, Start, End,
  ExecuteAction, or ErrorHandler nodes. If you need to insert before End, use
  `where="beforeEnd"` (requires Start→End linked first).
- **`live_add_action_call_node(where="beforeEnd")` requires Start→End linked.**
  If no End node exists or Start doesn't point to End, the call fails with
  `"End/prev not found"`. Always complete Step 4 (create Start+End, link them)
  before Step 5.

## Verification Checklist

After building a service action, verify each checkpoint:

| After Step | Verify With | Expected Result |
|---|---|---|
| Step 2 (create shell) | `live_list_flow` | `[0] ICommentNode` only |
| Step 3 (add params) | `live_module_info` | `serviceActions` count incremented |
| Step 4 (create Start+End) | `live_debug_node_props` on each index | Find Start and End node indices |
| Step 4 (link Start→End) | `live_list_flow` | `Start -> End` |
| Step 5 (add ExecuteAction) | `live_debug_node_props` on execIdx | `Action` property set to the server action |
| Step 7 (identify nodes) | `live_debug_node_props` on all indices | All node types identified and mapped |
| Step 8 (link all nodes) | `live_list_flow` | Full topology matches expected pattern |
| Step 9 (assignments) | `live_list_flow` | Assignments appear on correct nodes |
| Step 12 (map inputs) | `live_debug_action_args` | All arguments have values |
| Step 14 (final) | `live_list_flow` | Complete flow matches design, no orphan nodes |

## Scope: Action Types Currently Tooled

| Action Type | Create | Flow Edit | Parameters | Local Variables |
|---|---|---|---|---|
| Service Action | ✅ `live_create_service_action` | ✅ All `live_*` flow tools | ✅ Inputs + Outputs | ✅ `live_add_local_variable` |
| Server Action | ✅ `live_create_server_action` | ✅ All `live_*` flow tools | ✅ Inputs + Outputs | ✅ `live_add_local_variable` |
| Client Action | ✅ `live_create_client_action` | ✅ All `live_*` flow tools | ✅ Inputs + Outputs | ✅ `live_add_local_variable` |

All flow-editing tools work on any action type (service, server, client) because
they operate on the generic `IAction` interface. The bridge's `FindAction`
function searches `ServiceActions`, `ServerActions`, and `ClientActions`
collections.

## See Also

- `building-service-action-flows` — generic runbook for any flow structure
- `exception-handlers-in-service-actions` — exception flow architecture details
- `live-editing` — the underlying command-system mechanism
- `outsystems-liveeditor` — MCP tool reference
- `docs/element-class-reference.md` — platform semantics for every element these
  tools create (parameters, nodes, exposure constraints)
