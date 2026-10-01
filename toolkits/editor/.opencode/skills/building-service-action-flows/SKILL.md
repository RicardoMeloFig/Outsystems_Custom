---
name: building-service-action-flows
description: Generic runbook for building any OutSystems service action with a flow (live, in-process). Use when the user wants to create a service action that calls server actions, has input/output parameters, includes exception handling, or has a multi-step flow. Triggers on "build service action", "create flow", "service action with exception handling", "wrap server action". Proven on SS 11.55.81. Read creating-service-actions-live first for the full tool reference.
---

# Building Service Action Flows (Generic Runbook)

This is the **generic, proven runbook** for building any service action with a
flow. It works for any structure: simple wrapper, CRUD operation, multi-step
process, exception-handled action, etc. All steps use MCP tools — no raw pipe
commands needed.

**Prerequisites:** Module is OPEN in Service Studio with the OsLiveBridge plugin
loaded. Verify with `live_status`.

---

## CRITICAL: Follow These Steps IN EXACT ORDER

> **Each step depends on the previous one. SKIPPING A STEP WILL CAUSE FAILURES.**
>
> If a step fails, **STOP**. Run `live_list_flow` to check the current state —
> failed calls may still create nodes. Read the [Recovery Guide](#recovery-guide-surgical-cleanup)
> below before proceeding.
>
> Do NOT jump ahead. Do NOT skip verification checkpoints. Do NOT assume a
> "FAILED" response means "nothing happened."

---

## Scope: What This Runbook Covers

| Capability | Status | Notes |
|---|---|---|
| Create **service** actions | ✅ Tooled | `live_create_service_action` / `live_clone_service_action` |
| Create **server** actions | ✅ Tooled | `live_create_server_action` (if bridge updated) |
| Create **client** actions | ✅ Tooled | `live_create_client_action` (if bridge updated) |
| Add **inputs** (basic, entity, structure) | ✅ Tooled | `live_add_input_param` / `live_add_entity_input` |
| Add **outputs** (basic, entity, structure) | ✅ Tooled | `live_add_output_param` / `live_add_entity_output` / `live_set_output_param_type` |
| Add **local variables** | ✅ Tooled | `live_add_local_variable` (if bridge updated) |
| **Flow editing** on service/server/client actions | ✅ Tooled | All `live_*` flow tools work on any action type |
| **Exception handling** | ✅ Tooled | `IExceptionHandlerNode` + `live_set_error_handler_exception` |
| Fill a **record-literal** argument field-by-field | ✅ Tooled | `live_set_action_arg_field` — NOT `live_set_action_arg` (see [Anti-Patterns](#anti-patterns--what-not-to-do)) |

---

## The Runbook

### Step 1: Consume Dependencies

Consume the server actions and entities the service action will use:

```
live_consume_elements(consumer="MyModule", producer="MyModule_CS", what="ServerAction:Create,Entity:User")
```

- `what` accepts `"*"` (all), `"ServerAction:*"` (all of a type), or
  `"ServerAction:Name,Entity:Name"` (specific elements)
- Already-consumed elements are skipped (no error)
- Single undo unit

**VERIFY:** Check the response for `"status":"added"` on each element.

### Step 2: Create the Action Shell

```
live_create_service_action(module="MyModule", name="UserCreate_BL")
```

**IMPORTANT:** Creates an empty action with only a **Comment placeholder** node.
You must create Start and End nodes yourself (Step 4).

For **server actions** (if tooled):
```
live_create_server_action(module="MyModule", name="UserCreate_Server")
```

For **client actions** (if tooled):
```
live_create_client_action(module="MyModule", name="UserCreate_Client")
```

**VERIFY:** `live_list_flow(module, action)` — expect:
```
[0] ICommentNode
```
If you see anything other than a single Comment node, the action was not created
cleanly. Delete it with `live_delete_service_action` and retry.

### Step 3: Add Parameters

**Entity-typed input** (from a consumed producer module):
```
live_add_entity_input(module, action, name="User", entityName="User", producerModule="MyModule_CS")
```

**Structure-typed output** (from the same module):
```
live_add_output_param(module, action, name="Result", type="Text")        # placeholder type
live_set_output_param_type(module, action, paramName="Result", typeName="Result")  # set to Structure
```

**Entity-typed output** (from a consumed producer module):
```
live_add_entity_output(module, action, name="User", entityName="User", producerModule="MyModule_CS")
```

**Basic-type input:**
```
live_add_input_param(module, action, name="UserId", type="LongInteger")
```

**Local variable** (if tooled):
```
live_add_local_variable(module, action, name="TempId", type="LongInteger")
```

**VERIFY:** `live_module_info(module)` — check that `serviceActions` (or
`serverActions`/`clientActions`) count incremented. The parameters are visible
in the SS tree immediately.

### Step 4: Create Start and End Nodes, Then Link Them

> **This is the most critical step.** `live_add_action_call_node(where="beforeEnd")`
> in Step 5 REQUIRES an End node to exist and be linked from Start. If you skip
> this, Step 5 will fail with `"End/prev not found"`.

```
# Create Start and End nodes
live_debug_create_node(module, action, "IStartNode")
live_debug_create_node(module, action, "IEndNode")
```

**VERIFY node indices** — node indices are NOT creation order:
```
live_debug_node_props(module, action, 0)   # check nodeType
live_debug_node_props(module, action, 1)
live_debug_node_props(module, action, 2)   # Comment is also here
```

Map the results: index → type (Start, End, Comment).

**Link Start → End:**
```
live_set_node_target(module, action, startIdx, endIdx)
```

**VERIFY:** `live_list_flow(module, action)` — expect:
```
[0] IStartNode  -> [endIdx]
[1] IEndNode
[2] ICommentNode
```
(Indices may vary — check that Start's Target points to End.)

### Step 5: Add the ExecuteAction (Server Action Call)

> **PREREQUISITE:** Step 4 must be complete — Start and End must exist and be
> linked. This step uses `where="beforeEnd"` which finds the End node and
> inserts the ExecuteAction before it.

```
live_add_action_call_node(module, action, where="beforeEnd", serverActionName="Create", producerModule="MyModule_CS")
```

This creates the `IExecuteServerActionNode`, sets its `Action` property to the
consumed server action, and inserts it between Start and End.

**Do NOT use `where="afterAnchor"` here** — there are no Assign nodes yet to
anchor on. `afterAnchor` ONLY works with existing Assign nodes (see
[Anti-Patterns](#anti-patterns--what-not-to-do)).

**Do NOT use `live_debug_create_node("IExecuteServerActionNode")`** — it creates
an empty node without the Action reference. Always use
`live_add_action_call_node` which configures the Action property.

**VERIFY:** `live_list_flow(module, action)` — expect Start → ExecuteAction → End.
Then verify the Action property is set correctly:
```
live_debug_node_props(module, action, execIdx)   # check that Action is set to the server action
```

### Step 6: Create Remaining Nodes

Create any additional nodes needed for the flow:

```
live_debug_create_node(module, action, "IAssignNode")           # for success assignments
live_debug_create_node(module, action, "IAssignNode")           # for error assignments (if exception handling)
live_debug_create_node(module, action, "IEndNode")             # exception End (if exception handling)
live_debug_create_node(module, action, "IExceptionHandlerNode") # ErrorHandler (if exception handling)
```

### Step 7: Identify ALL Node Indices

> **Node indices are NOT creation order.** SS assigns indices internally. You
> MUST identify each node before linking. Do NOT skip this step.

```
live_debug_node_props(module, action, 0)   # returns nodeType + settableProperties
live_debug_node_props(module, action, 1)
live_debug_node_props(module, action, 2)
# ... repeat for ALL nodes
```

Map the results: index → type (Start, ExecuteAction, Assign, End, ErrorHandler,
Comment). Write this map down — you will use it in Step 8.

**VERIFY:** Count the nodes. You should have:
- 1 Start
- 1 ExecuteAction
- 1 or 2 Assign (success, and error if exception handling)
- 1 or 2 End (normal, and exception if exception handling)
- 1 ErrorHandler (if exception handling)
- 1 Comment (the original placeholder — will be deleted in Step 12)

### Step 8: Link All Nodes

Wire the flow using `live_set_node_target`. Use the index map from Step 7:

```
# Normal flow
live_set_node_target(module, action, startIdx, execIdx)              # Start → ExecuteAction
live_set_node_target(module, action, execIdx, successAssignIdx)      # ExecuteAction → Assign(success)
live_set_node_target(module, action, successAssignIdx, normalEndIdx) # Assign(success) → End(normal)

# Exception flow (if applicable)
live_set_node_target(module, action, errorHandlerIdx, errorAssignIdx)  # ErrorHandler → Assign(error)
live_set_node_target(module, action, errorAssignIdx, exceptionEndIdx)  # Assign(error) → End(exception)
```

**VERIFY:** `live_list_flow(module, action)` — verify the topology:
```
Normal:  Start → ExecuteAction → Assign(success) → End(normal)
Error:   ErrorHandler → Assign(error) → End(exception)
```
Every node (except End nodes) should have a Target pointing to the next node.

### Step 9: Add Assignments

Assign nodes start **empty**. Use `live_add_assignment_to_node` to add assignments.
Multiple assignments can go on one node:

```
# Success Assign: Result.IsError=False, Result.Message="Success"
live_add_assignment_to_node(module, action, successAssignIdx, "Result.IsError", "False")
live_add_assignment_to_node(module, action, successAssignIdx, "Result.Message", "\"Success\"")

# Error Assign: Result.IsError=True (add Message AFTER exception handler is set — Step 10)
live_add_assignment_to_node(module, action, errorAssignIdx, "Result.IsError", "True")
```

**Value quoting rules** (the value parameter is passed as-is to SetValue):

| Type | Appears in SS as | Exact characters to pass in `value` |
|------|------------------|--------------------------------------|
| Boolean | `False` | `False` |
| Text literal | `"Success"` | `"Success"` (the double quotes ARE part of the value) |
| Identifier | `AllExceptions.ExceptionMessage` | `AllExceptions.ExceptionMessage` |
| Number | `1` | `1` |
| **Message text** | `"Title is required."` | ⚠️ Message widgets take EXPRESSIONS: raw `Title is required.` parses `is` as an operator (red). ALWAYS quote message text, including the trailing period (`"Saved."` — bare `Saved.` hits EOF parse). |

⚠️ **Do NOT wrap the value in single quotes.** Pass `"Success"` (9 chars),
NOT `'"Success"'` (11 chars). The value is passed as-is to SetValue — single
quotes will end up in the SS expression and cause errors.

**VERIFY:** `live_list_flow(module, action)` — assignments should appear on the
correct Assign nodes.

### Step 10: Set Exception Handler (if applicable)

> **MUST be done BEFORE adding `AllExceptions.ExceptionMessage` assignment** — the
> expression needs the exception context to resolve.

```
live_set_error_handler_exception(module, action, errorHandlerIdx)
```

Sets `Exception = SystemException [All Exceptions]` (found via `es.AllExceptions`).
Also sets `AbortTransaction=true` and `LogError=true`.

**CRITICAL:** `Start.ExceptionHandler` does NOT exist as a settable property.
SS auto-recognizes the `IExceptionHandlerNode` as the flow's exception handler
based on its type and `Exception` property. Do NOT try to set
`Start.ExceptionHandler` — use `live_set_error_handler_exception` instead.

### Step 11: Complete Error Assignments

After the exception handler is set, add the AllExceptions assignment:

```
live_add_assignment_to_node(module, action, errorAssignIdx, "Result.Message", "AllExceptions.ExceptionMessage")
```

### Step 12: Map Input Arguments

```
live_map_action_inputs(module, action, execIdx)
```

Auto-maps the ExecuteAction's input arguments to the service action's input
parameters. Tries name matching first; falls back to trying each input parameter
name when names don't match (e.g. server action uses "Source" but service action
uses "User").

**VERIFY:** `live_debug_action_args(module, action, execIdx)` — check that all
arguments have values mapped.

> **Record-typed argument?** `live_map_action_inputs` and `live_set_action_arg`
> store raw TEXT — the O11 parser rejects every record-literal text form
> (`{ Id: ... }` → `ParserUnexpectedElement`; `New Entity(...)` → syntax error).
> Fill entity/structure-typed arguments field-by-field with
> `live_set_action_arg_field(module, action, execIdx, "Source", fields={"TurnNumber":"TurnNumber","ActorName":"ActorName"})`
> — it edits the argument's `RecordLiteralExpression` directly (identifier refs
> and `NullIdentifier()` parse fine at field level).

### Step 13: Delete Comment Placeholder

```
# Find the Comment index via live_debug_node_props (it's the remaining ICommentNode)
live_delete_node_by_index(module, action, commentIdx)
```

### Step 14: Layout Flow (Auto-Position Nodes)

> Nodes are auto-positioned on creation (rough heuristic — each new node gets a Y
> based on creation order). But after all nodes are created, linked, and the Comment
> is deleted, call `live_layout_flow` for a **proper topology-based layout**:
> main flow in the left column (X=3200), exception flows in the right column
> (X=12800), Y increments downward per node.

```
live_layout_flow(module, action)
```

**VERIFY:** `live_get_node_positions(module, action)` — check that:
- Main flow nodes have X=3200, Y incrementing from 914
- Exception flow nodes have X=12800, Y incrementing from 914

### Step 15: Final Verify and Save

```
live_list_flow(module, action)   # verify complete topology
# Then Ctrl+S in SS to persist!
```

**VERIFY:** The flow should match your intended structure exactly. Every node
should be linked. No orphan nodes. No duplicate nodes. Nodes should be visually
laid out (not stacked) in the SS canvas.

---

## Common Flow Patterns

### Pattern 1: Simple Wrapper (no exception handling)

```
Start → ExecuteAction → End
```
- No Assign nodes, no ErrorHandler
- Just map inputs (Step 12)

### Pattern 2: Wrapper with Result Structure

```
Start → ExecuteAction → Assign(success) → End(normal)
```
- One Assign with `Result.IsError=False` + `Result.Message="Success"`
- No exception handling

### Pattern 3: Full Exception-Handled Wrapper

```
Start → ExecuteAction → Assign(success) → End(normal)
                          ↓ (on exception)
               ErrorHandler → Assign(error) → End(exception)
```
- Two Assigns (success + error), each with 2 assignments
- ErrorHandler with `Exception=All Exceptions`
- Error Assign includes `AllExceptions.ExceptionMessage`

### Pattern 4: Multi-Step Flow

```
Start → ExecuteAction1 → ExecuteAction2 → Assign → End
```
- Multiple ExecuteAction nodes (use `live_add_action_call_node` for each)
- Link sequentially with `live_set_node_target`

---

## Anti-Patterns / What NOT to Do

These are the mistakes that cause flows to break. Read this before starting.

### ❌ DON'T call `live_add_action_call_node` before Start and End exist and are linked

**What happens:** `where="beforeEnd"` fails with `"End/prev not found"` because
there is no End node to insert before.

**Fix:** Go back to Step 4. Create Start and End, link them, then retry.

### ❌ DON'T use `afterAnchor` with non-Assign nodes

**What happens:** `afterAnchor` searches for an **Assign node** whose assignment
variable contains `anchorVar` and value contains `anchorValue`. Comment, Start,
End, ExecuteAction, and ErrorHandler nodes do NOT have assignments. The call
fails with `"anchor not found"`.

**Fix:** `afterAnchor` is for inserting into an existing flow that already has
Assign nodes. For the initial ExecuteAction, always use `where="beforeEnd"`
(after Step 4 links Start→End).

### ❌ DON'T assume a "FAILED" response means "no side effects"

**What happens:** Some failed calls still create nodes in the flow. The tool
returns an error, but the node exists in the model. Subsequent calls may behave
unexpectedly because of these phantom nodes.

**Fix:** After ANY failure, run `live_list_flow` to check the current state.
If phantom nodes exist, delete them with `live_delete_node_by_index` before
proceeding.

### ❌ DON'T skip `live_debug_node_props` to identify node indices

**What happens:** Node indices are NOT creation order. SS assigns indices
internally. If you assume index 0 = Start, index 1 = End, etc., you will wire
the wrong nodes together.

**Fix:** Always run `live_debug_node_props` on every index after creating nodes.
Map index → type before linking.

### ❌ DON'T create `IExecuteServerActionNode` via `live_debug_create_node`

**What happens:** `live_debug_create_node("IExecuteServerActionNode")` creates an
empty node without the Action reference. The node exists but doesn't call any
server action. There is no way to set the Action property afterward via
`live_set_node_property` (it expects a string, but Action needs an object
reference).

**Fix:** Always use `live_add_action_call_node` which creates AND configures
the ExecuteAction in one step.

### ❌ DON'T use `live_set_action_arg` to fill a record-literal argument

**What happens:** `live_set_action_arg` stores the value STRING raw via the
argument's `SetValue`. For an entity/structure-typed argument the model expects
a `RecordLiteralExpression`, and the O11 expression parser rejects EVERY text
form of a record literal:
- `{ Id: NullIdentifier(), OrderIndex: OrderIndex }` → `ParserUnexpectedElement`
  at the `:` (braces = static-entity syntax to the parser)
- `New BattleEvent(Id: NullIdentifier(), ...)` → "Syntax error caused by
  unexpected 'BattleEvent' element" (O11 has no text record-constructor)

Worse, a failed parse can HALF-LAND invalid text on the argument — the edit
session must then repair the value before continuing.

**Fix:** Use `live_set_action_arg_field(module, action, nodeIndex, argName,
field, value)` (single field) or `fields={"Attr":"expr", ...}` (multiple
fields). It locates the argument's `RecordLiteralExpression` (the default SS
builds when `live_add_action_call_node` sets the Action), maps attribute names
to field slots (by `AttributeName`, falling back to the record type's attribute
order), and calls the model's field-level parse API per field — identifier
refs, literals and `NullIdentifier()` all parse fine at FIELD level. It
validates all requested attributes BEFORE mutating, and a mid-mutation failure
rolls back the whole undo unit.

### ❌ DON'T try to set `Start.ExceptionHandler`

**What happens:** `Start.ExceptionHandler` does NOT exist as a property — not
even read-only. The call fails with `"property not settable"`.

**Fix:** Use `live_debug_create_node("IExceptionHandlerNode")` +
`live_set_error_handler_exception` + `live_set_node_target` instead. SS
auto-recognizes the `IExceptionHandlerNode` as the flow's exception handler.

### ❌ DON'T add `AllExceptions.ExceptionMessage` before setting the exception handler

**What happens:** The expression `AllExceptions.ExceptionMessage` needs the
exception context to resolve. If the ErrorHandler's `Exception` property is not
set yet, the assignment may fail or produce an invalid expression.

**Fix:** Follow the step order: Step 10 (set exception handler) BEFORE Step 11
(add AllExceptions assignment).

### ❌ DON'T use `live_add_assign_node` when you need multiple assignments on one node

**What happens:** `live_add_assign_node` creates a NEW node with ONE assignment.
If you call it twice, you get two separate Assign nodes, each with one
assignment — instead of one Assign node with two assignments.

**Fix:** Use `live_debug_create_node("IAssignNode")` to create an empty node,
then `live_add_assignment_to_node` to add each assignment to that same node.

### ❌ DON'T create dummy/placeholder assignments (Temp, Placeholder, Dummy)

**What happens:** `live_add_assign_node(where="afterAnchor")` requires an
existing Assign node with a matching var+value to anchor on. If no anchor
exists, you may be tempted to create a dummy assignment (e.g. `Temp=False`) as
a fake anchor, then add the real assignments after it. This leaves a leftover
`Temp=False` assignment on the node that serves no purpose and clutters the flow.

**Fix:** NEVER use `afterAnchor` on newly created (empty) Assign nodes. Instead:
1. Use `live_debug_create_node("IAssignNode")` to create an empty node
2. Use `live_add_assignment_to_node` to add each assignment
3. Use `live_set_node_target` to wire the node into the flow

If you already have leftover dummy assignments, remove them with
`live_remove_assignment(module, action, nodeIndex, "Temp")`.

### ❌ DON'T create new service actions when building flows for existing actions

**What happens:** If you're building a flow inside an EXISTING service action
(one that was already created), calling `live_create_service_action` again will
either create a duplicate (if the bridge doesn't check) or fail with "service
action already exists". Subagents are especially prone to this — they may
create `_BL2` suffixed duplicates instead of working on the existing action.

**Fix:** When building flows for existing actions, ONLY use flow-editing tools
(`live_debug_create_node`, `live_set_node_target`, `live_add_assignment_to_node`,
etc.). NEVER call `live_create_service_action` unless you genuinely need a new
action. The bridge now rejects duplicate names with an error.

### ❌ DON'T call `live_add_input_param` without checking for existing parameters

**What happens:** `live_add_input_param` is NOT idempotent. Calling it twice
with the same name creates duplicate parameters (e.g. `ClientId` AND
`ClientId2`). The bridge now rejects duplicates with an error, but you should
still verify before adding.

**Fix:** Before adding a parameter, verify the action's existing parameters via
`live_debug_action_args` (which lists input parameters). Only add a parameter
if it doesn't already exist.

### ❌ DON'T use `LongInteger` for entity Id parameters — use entity Identifier types

**What happens:** Delete actions typically take an `Id` parameter whose type is
the entity's Identifier type (e.g. `Diet Identifier`), NOT a plain
`LongInteger`. Using `LongInteger` causes type mismatch — the server action
expects a typed identifier, not a generic integer.

**Fix:** Use `live_add_entity_identifier_input(module, action, name="DietId",
entityName="Diet", producerModule="Diet_CS")` which resolves the entity's
Identifier type from its `Id` attribute's `DataType`. This is the correct type
for Delete actions.

---

## Recovery Guide (Surgical Cleanup)

When something goes wrong, follow this guide to diagnose and fix the flow
without starting over.

### Step 1: Diagnose

Run `live_list_flow` to see all nodes, their types, and links:
```
live_list_flow(module, action)
```

Look for:
- **Duplicate nodes** (e.g., two ExecuteAction nodes when you only need one)
- **Missing links** (a node with no Target, or an End node that nothing points to)
- **Wrong links** (Start pointing to End instead of ExecuteAction)
- **Phantom nodes** (nodes created by failed calls)

### Step 2: Identify Bad Nodes

Run `live_debug_node_props` on each suspect index to confirm the node type and
check its properties:
```
live_debug_node_props(module, action, suspectIdx)
```

For ExecuteAction nodes, also check what action they're calling:
```
live_debug_action_args(module, action, suspectIdx)
```

### Step 3: Remove Bad Nodes

Delete each bad node by index:
```
live_delete_node_by_index(module, action, badIdx)
```

**IMPORTANT:** Deleting a node relinks the previous node to the deleted node's
target. If the flow is complex, verify the links after each deletion:
```
live_list_flow(module, action)   # verify after each deletion
```

Delete one node at a time, verify, then delete the next. Do NOT batch deletions
without verifying between each — indices may shift after deletion.

### Step 4: Verify Clean State

After cleanup, run `live_list_flow` one more time to confirm the flow is in a
clean state. Then resume from the correct step in the runbook.

### Step 5: Nuclear Option (if the flow is too broken)

If the flow has too many problems to fix surgically, delete the entire service
action and start over:
```
live_delete_service_action(module, action)
```

Then restart from Step 2 (create the action shell). This is a single undo unit
(Ctrl+Z in SS restores it).

---

## Complete Worked Example: DietCreate_BL

**Goal:** Create `DietCreate_BL` in `Diet_BL` that wraps `DietCreate` from `Diet_CS`.

**Input:** `Diet` (entity from `Diet_CS`)
**Output:** `Result` (Structure from `Diet_BL`, with `IsError:Boolean`, `Message:Text`)
**Flow:**
```
Normal:  Start → DietCreate → Assign(IsError=False, Msg="Success") → End
Error:   ErrorHandler → Assign(IsError=True, Msg=AllExceptions.ExceptionMessage) → End
```

### Step-by-step with expected outputs:

**Step 1: Consume dependencies**
```
live_consume_elements("Diet_BL", "Diet_CS", "ServerAction:DietCreate,Entity:Diet")
```
Expected: `{"ok":true,"consumed":[{"type":"ServerAction","name":"DietCreate","status":"added"},{"type":"Entity","name":"Diet","status":"added"}],"totalConsumed":2}`

**Step 2: Create the action shell**
```
live_create_service_action("Diet_BL", "DietCreate_BL")
```
Expected: `{"ok":true,"serviceActionsBefore":0,"serviceActionsAfter":1}`

VERIFY: `live_list_flow("Diet_BL", "DietCreate_BL")` → expect `[0] ICommentNode`

**Step 3: Add parameters**
```
live_add_entity_input("Diet_BL", "DietCreate_BL", "Diet", "Diet", "Diet_CS")
```
Expected: `{"ok":true,"report":"added entity input Diet : Diet (from Diet_CS)"}`

```
live_add_output_param("Diet_BL", "DietCreate_BL", "Result", "Text")
live_set_output_param_type("Diet_BL", "DietCreate_BL", "Result", "Result")
```
Expected: `{"ok":true}` for both calls

**Step 4: Create Start and End, link them**
```
live_debug_create_node("Diet_BL", "DietCreate_BL", "IStartNode")
live_debug_create_node("Diet_BL", "DietCreate_BL", "IEndNode")
```
Expected: `{"ok":true,"report":"created: Start"}` / `{"ok":true,"report":"created: End"}`

VERIFY indices:
```
live_debug_node_props("Diet_BL", "DietCreate_BL", 0)  # → nodeType: Start
live_debug_node_props("Diet_BL", "DietCreate_BL", 1)  # → nodeType: End (or Comment)
live_debug_node_props("Diet_BL", "DietCreate_BL", 2)  # → nodeType: End (or Comment)
```
(Map actual indices — they may NOT be 0=Start, 1=End, 2=Comment.)

Link Start → End:
```
live_set_node_target("Diet_BL", "DietCreate_BL", startIdx, endIdx)
```
Expected: `{"ok":true}`

VERIFY: `live_list_flow("Diet_BL", "DietCreate_BL")` → expect `Start -> End`

**Step 5: Add ExecuteAction (DietCreate call)**
```
live_add_action_call_node("Diet_BL", "DietCreate_BL", "beforeEnd", "DietCreate", producerModule="Diet_CS")
```
Expected: `{"ok":true}` (creates IExecuteServerActionNode with Action=DietCreate, inserts before End)

VERIFY: `live_list_flow` → expect `Start → ExecuteAction → End`

**Step 6: Create remaining nodes**
```
live_debug_create_node("Diet_BL", "DietCreate_BL", "IAssignNode")            # success Assign
live_debug_create_node("Diet_BL", "DietCreate_BL", "IAssignNode")            # error Assign
live_debug_create_node("Diet_BL", "DietCreate_BL", "IEndNode")              # exception End
live_debug_create_node("Diet_BL", "DietCreate_BL", "IExceptionHandlerNode")  # ErrorHandler
```

**Step 7: Identify ALL node indices**
```
live_debug_node_props("Diet_BL", "DietCreate_BL", 0)
live_debug_node_props("Diet_BL", "DietCreate_BL", 1)
# ... for all indices
```
Map: index → type. Example result:
```
[0] Start
[1] ExecuteAction
[2] Assign        ← success
[3] Assign        ← error
[4] End           ← normal
[5] End           ← exception
[6] ErrorHandler
[7] Comment       ← to be deleted
```
(Your actual indices WILL differ — always verify!)

**Step 8: Link all nodes**
```
live_set_node_target("Diet_BL", "DietCreate_BL", 0, 1)   # Start → ExecuteAction
live_set_node_target("Diet_BL", "DietCreate_BL", 1, 2)   # ExecuteAction → Assign(success)
live_set_node_target("Diet_BL", "DietCreate_BL", 2, 4)   # Assign(success) → End(normal)
live_set_node_target("Diet_BL", "DietCreate_BL", 6, 3)   # ErrorHandler → Assign(error)
live_set_node_target("Diet_BL", "DietCreate_BL", 3, 5)   # Assign(error) → End(exception)
```
(Use YOUR actual indices from Step 7.)

VERIFY: `live_list_flow` → verify topology matches Pattern 3.

**Step 9: Add success assignments + error IsError**
```
live_add_assignment_to_node("Diet_BL", "DietCreate_BL", 2, "Result.IsError", "False")
live_add_assignment_to_node("Diet_BL", "DietCreate_BL", 2, "Result.Message", "\"Success\"")
live_add_assignment_to_node("Diet_BL", "DietCreate_BL", 3, "Result.IsError", "True")
```

**Step 10: Set exception handler**
```
live_set_error_handler_exception("Diet_BL", "DietCreate_BL", 6)   # errorHandlerIdx
```

**Step 11: Add error Message assignment**
```
live_add_assignment_to_node("Diet_BL", "DietCreate_BL", 3, "Result.Message", "AllExceptions.ExceptionMessage")
```

**Step 12: Map input arguments**
```
live_map_action_inputs("Diet_BL", "DietCreate_BL", 1)   # execIdx
```

**Step 13: Delete Comment placeholder**
```
live_delete_node_by_index("Diet_BL", "DietCreate_BL", 7)   # commentIdx
```

**Step 14: Layout flow (auto-position nodes)**
```
live_layout_flow("Diet_BL", "DietCreate_BL")
```
This positions the main flow (Start→ExecuteAction→Assign→End) in the left column
(X=3200) and the exception flow (ErrorHandler→Assign→End) in the right column
(X=12800), with Y incrementing downward per node.

**Step 15: Final verify**
```
live_list_flow("Diet_BL", "DietCreate_BL")
live_get_node_positions("Diet_BL", "DietCreate_BL")
```
Expected topology:
```
[0] Start      -> [1]
[1] ExecuteAction -> [2]
[2] Assign (Result.IsError=False, Result.Message="Success") -> [4]
[3] Assign (Result.IsError=True, Result.Message=AllExceptions.ExceptionMessage) -> [5]
[4] End (normal)
[5] End (exception)
[6] ErrorHandler -> [3]
```
Expected positions (after live_layout_flow):
```
[0] Start           X=3200  Y=914
[1] ExecuteAction   X=3200  Y=2914
[2] Assign(success) X=3200  Y=4914
[3] Assign(error)   X=12800 Y=914
[4] End(normal)     X=3200  Y=6914
[5] End(exception)  X=12800 Y=2914
[6] ErrorHandler    X=12800 Y=914   (traversed first in exception flow)
```
Then Ctrl+S in SS to persist.

---

## Troubleshooting

| Problem | Cause | Fix |
|---------|-------|-----|
| `live_create_service_action` creates only 1 node | Creates a Comment placeholder, NOT Start→End | Create Start and End manually (Step 4) |
| `End/prev not found` from `live_add_action_call_node` | No End node exists, or Start→End not linked | Go back to Step 4: create Start+End, link them, then retry Step 5 |
| `anchor not found: =` from `afterAnchor` | Empty anchorVar/anchorValue passed | `afterAnchor` requires a real Assign node's var+value; use `beforeEnd` instead |
| `anchor not found: Comment=...` from `afterAnchor` | `afterAnchor` used with a Comment node | `afterAnchor` ONLY works with Assign nodes; use `beforeEnd` or link manually |
| Duplicate nodes after "FAILED" response | Failed call still created a node | Run `live_list_flow`, find duplicates via `live_debug_node_props`, delete with `live_delete_node_by_index` |
| Node indices don't match creation order | SS assigns indices internally | Use `live_debug_node_props` to identify (Step 7) |
| `live_add_assignment_to_node` fails with empty value | Text literal not quoted | Pass `"Success"` (double quotes are part of the value; do NOT add single quotes) |
| `AllExceptions.ExceptionMessage` fails | Exception handler not set yet | Run Step 10 before Step 11 |
| `live_set_node_property` fails on Action | Action needs object reference, not string | Use `live_add_action_call_node` instead |
| All Targets nullified after `live_delete_node_by_index` | Old Key-as-string bug | **Fixed** — now uses ReferenceEquals |
| `live_add_assign_node(beforeEnd)` links wrong nodes | Old Key-as-string bug | **Fixed** — now uses ReferenceEquals |
| `property not settable: ExceptionHandler on Start` | `Start.ExceptionHandler` doesn't exist | Use `IExceptionHandlerNode` + `live_set_error_handler_exception` |
| `action not found` when editing a server/client action | `FindServiceAction` only searches ServiceActions | Bridge updated to `FindAction` which searches all action types |

---

## See Also

- `creating-service-actions-live` — full tool reference + detailed examples
- `exception-handlers-in-service-actions` — exception flow architecture
- `outsystems-liveeditor` — MCP tool reference
- `docs/element-class-reference.md` — platform ground truth for each flow node
  (Assign order semantics, If/Switch/ForEach, Exception Handler defaults,
  input-parameter scope rule, output-parameter access pattern)
