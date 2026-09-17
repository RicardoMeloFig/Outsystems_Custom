---
name: exception-handlers-in-service-actions
description: Use when the user wants to build a service action with exception handling in OutSystems, or troubleshoot why an exception flow isn't working. Triggers on "exception handler", "error handling flow", "try catch", "exception path", "AllExceptions". Proven on SS 11.55.81.
---

# Exception Handlers in Service Actions

This skill documents the **actual architecture** of exception handling in OutSystems
service action flows — which differs from what the API suggests.

## Key Discoveries

1. **`Start.ExceptionHandler` does NOT exist** as a property on the Start node.
   Every attempt to set it fails: "property not settable: ExceptionHandler on
   ServiceStudio.Model.Nodes+Start". The property doesn't exist at all (not even
   read-only).

2. **`ExecuteAction` has NO ExceptionHandler property** either. You cannot wire
   exception handling to a specific node.

3. **SS auto-recognizes `IExceptionHandlerNode`** as the flow's exception handler
   based on its node type. No explicit wiring to Start is needed.

4. **The ErrorHandler's `Exception` property CAN be set** — using
   `live_set_error_handler_exception`, which finds the `SystemException` object
   (named "All Exceptions") from `es.AllExceptions` or copies it from a reference
   action's ErrorHandler. This was previously thought to be impossible.

## How Exception Handling Actually Works

```
Normal Flow:
  Start → ExecuteAction → Assign(success) → End(normal)

Exception Flow:
  ErrorHandler → Assign(error) → End(exception)
```

When any node in the normal flow throws an exception, OutSystems automatically
routes to the ErrorHandler node. The ErrorHandler is recognized by its type
(`IExceptionHandlerNode`) and its `Exception` property (which must be set to
`All Exceptions`).

**No explicit wiring to Start is needed.** Just create the ErrorHandler, set its
`Exception` property, and link it to the error flow.

## The ErrorHandler Node

The ErrorHandler node (`IExceptionHandlerNode`) is:
- Created via `live_debug_create_node(module, action, "IExceptionHandlerNode")`
- The **START of the exception flow** (not a handler attached to ExecuteAction)
- Has a `Target` property pointing to the first node in the exception flow
- Has an `Exception` property of type `AbstractException` — settable via
  `live_set_error_handler_exception` (NOT via `live_set_node_property`, which
  only handles string values)
- Also has `AbortTransaction` (set to `true`) and `LogError` (set to `true`)

## Configuring the Exception Handler

```
# After creating the ErrorHandler node and linking it to the error flow:
live_set_error_handler_exception(module, action, errorHandlerIdx)
```

This command:
1. Finds the `AllExceptions` type — `es.AllExceptions` returns a `SystemException`
   object with `Name="All Exceptions"`. If not found, it reads the Exception from
   a reference action's ErrorHandler (e.g. `ClientCreate_BL_Manually`).
2. Sets it on the ErrorHandler's `Exception` property via `SetProp`.
3. Also sets `AbortTransaction=true` and `LogError=true`.

## Correct Flow Structure

```
[Start]──────────→[ExecuteAction]──────────→[Assign(success)]──────────→[End(normal)]
                      │
                      │ (SS auto-routes here on exception)
                      ↓
               [ErrorHandler]──→[Assign(error)]──→[End(exception)]
```

Both Assign nodes can hold **multiple assignments** via `live_add_assignment_to_node`:
- Success: `Result.IsError=False` AND `Result.Message="Success"` (one Assign node)
- Error: `Result.IsError=True` AND `Result.Message=AllExceptions.ExceptionMessage` (one Assign node)

## Node Linking (via live_set_node_target)

| From | To | Meaning |
|------|-----|---------|
| Start | ExecuteAction | Normal flow entry |
| ExecuteAction | Assign(success) | Normal flow continues |
| Assign(success) | End(normal) | Normal flow ends |
| ErrorHandler | Assign(error) | Exception flow entry |
| Assign(error) | End(exception) | Exception flow ends |

**IMPORTANT:** `live_set_node_target` uses `ReferenceEquals` for reliable node
matching. The old `Key`-as-string comparison (which caused bugs in `beforeEnd`
and `del_node_by_index`) has been fixed.

## Adding Assignments

Assign nodes start **empty** when created via `live_debug_create_node`. Use
`live_add_assignment_to_node` to add assignments one at a time:

```
# Success Assign
live_add_assignment_to_node(module, action, successIdx, "Result.IsError", "False")
live_add_assignment_to_node(module, action, successIdx, "Result.Message", "\"Success\"")

# Error Assign (add AllExceptions.ExceptionMessage AFTER exception flow is wired)
live_add_assignment_to_node(module, action, errorIdx, "Result.IsError", "True")
live_add_assignment_to_node(module, action, errorIdx, "Result.Message", "AllExceptions.ExceptionMessage")
```

**Value quoting:**
- Booleans: bare (`False`, `True`)
- Text literals: include double quotes in the value (pass `"Success"`, NOT `'"Success"'`)
- Identifiers: bare (`AllExceptions.ExceptionMessage`)

## Properties Summary

### Start (IStartNode)
- Settable: `Target`, `X`, `Y`, `IsDisabled`, `Links`
- **`ExceptionHandler` does NOT exist** — not settable, not even read-only

### ExecuteAction (IExecuteServerActionNode)
- Settable: `Action` (server action reference), `Target`
- **NOT settable:** `ExceptionHandler` (does not exist)
- **Configuring Action:** Use `live_add_action_call_node` (creates + configures in one step).
  Do NOT use `live_set_node_property` for the Action property (it expects a string, but
  Action needs a server action object reference).

### ErrorHandler (IExceptionHandlerNode)
- Settable: `Target`, `Exception`, `AbortTransaction`, `LogError`, `Name`
- `Exception` is of type `AbstractException` — set via `live_set_error_handler_exception`
  (finds `es.AllExceptions` → `SystemException` and sets it)

### Assign (IAssignNode)
- Settable: `Target`, `Label`, `Assignments` (collection)
- Use `live_add_assignment_to_node` to add assignments (supports multi-assignment)

### End (IEndNode)
- **NOT settable:** `Target` (End is terminal)
- Settable: `X`, `Y`, `IsDisabled`, `Name`

## Step-by-Step: Building the Exception Flow

```
# 1. Create nodes (all standalone)
live_debug_create_node(module, action, "IStartNode")
live_debug_create_node(module, action, "IEndNode")             # normal End
live_set_node_target(module, action, startIdx, endIdx)         # link Start→End first

# 2. Add ExecuteAction (creates + configures)
live_add_action_call_node(module, action, "beforeEnd", serverActionName="...")

# 3. Create remaining nodes
live_debug_create_node(module, action, "IAssignNode")          # success Assign
live_debug_create_node(module, action, "IAssignNode")          # error Assign
live_debug_create_node(module, action, "IEndNode")             # exception End
live_debug_create_node(module, action, "IExceptionHandlerNode") # ErrorHandler

# 4. Identify all node indices (NOT creation order!)
live_debug_node_props(module, action, 0)  # repeat for each index

# 5. Link all nodes
live_set_node_target(module, action, execIdx, successAssignIdx)
live_set_node_target(module, action, successAssignIdx, normalEndIdx)
live_set_node_target(module, action, errorHandlerIdx, errorAssignIdx)
live_set_node_target(module, action, errorAssignIdx, exceptionEndIdx)

# 6. Add assignments
live_add_assignment_to_node(module, action, successAssignIdx, "Result.IsError", "False")
live_add_assignment_to_node(module, action, successAssignIdx, "Result.Message", "\"Success\"")
live_add_assignment_to_node(module, action, errorAssignIdx, "Result.IsError", "True")

# 7. Set exception handler (MUST be before AllExceptions assignment)
live_set_error_handler_exception(module, action, errorHandlerIdx)

# 8. Add AllExceptions.ExceptionMessage (needs exception context)
live_add_assignment_to_node(module, action, errorAssignIdx, "Result.Message", "AllExceptions.ExceptionMessage")

# 9. Map inputs
live_map_action_inputs(module, action, execIdx)

# 10. Verify
live_list_flow(module, action)
```

## See Also

- `creating-service-actions-live` — full action creation with proven step-by-step process
- `building-service-action-flows` — generic runbook for any flow structure
- `live-editing` — underlying command system mechanics
- `outsystems-liveeditor` — MCP tool reference
- `docs/element-class-reference.md` — Exception Handler platform defaults
  (`Exception` hierarchy, `Abort Transaction=Yes` server-side only,
  `Log Error=Yes`, `ExceptionMessage` runtime property)
