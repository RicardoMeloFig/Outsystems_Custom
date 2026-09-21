---
name: creating-crud-wrappers
description: Use when the user wants to generate public CRUD service-action wrappers for an entity (Create, Read/Get, Update, Delete). **LIVE is the default** (module open in SS, use building-service-action-flows runbook); HEADLESS (.oml file edit, roadmap) ONLY when the user explicitly requests headless/no-SS editing. Read building-service-action-flows first for the live approach.
---

# Creating CRUD Wrappers

Generate a set of public CRUD service-action wrappers for an entity (Create,
Read/Get, Update, Delete).

## Approaches — LIVE IS THE DEFAULT

| Approach | When to use | Skill to read |
|---|---|---|
| **LIVE (default)** | Module is OPEN in Service Studio | `building-service-action-flows` (proven 15-step runbook) |
| HEADLESS (.oml) | **ONLY if the user explicitly requests headless** (SS not running / CI) | `editor-workflow` + `creating-service-actions` (ROADMAP) |

**Rule:** If the module is open in SS, use the LIVE approach via
`building-service-action-flows`. That skill has the proven 15-step runbook
for building any service action with a flow (ExecuteAction, Assigns,
Exception Handler, input/output parameters). Do NOT use this skill for
live editing — use `building-service-action-flows` instead.

## Live Approach (Proven)

For each CRUD operation (Create, Update, Delete, etc.):

1. **Consume dependencies** — `live_consume_elements` to consume the entity's
   server actions (e.g. `DietCreate`, `DietUpdate`, `DietDelete`) from the
   producer module (e.g. `Diet_CS`).
2. **Create the action** — `live_create_service_action(withSkeleton=true)` to
   create the action with a linked Start→End flow.
3. **Add parameters** — `live_add_entity_input` (for Create/Update — takes the
   full entity record) or `live_add_entity_identifier_input` (for Delete — takes
   the entity Identifier type). Add `Result` output via `live_add_output_param` +
   `live_set_output_param_type`.
4. **Insert ExecuteAction** — `live_add_action_call_node(where="beforeEnd")` to
   insert the server action call (Start→End is already linked from step 2).
5. **Create remaining nodes** — Assign (success), Assign (error), End (exception),
   ExceptionHandler — via `live_debug_create_node`.
6. **Wire, assign, set exception handler, map inputs** — follow the runbook in
   `building-service-action-flows`.

### Clone-and-Modify Approach (also proven, for similar wrappers)

When a working wrapper already exists (e.g. `DietCreate_BL`), clone it and
modify:

1. `live_clone_service_action` — clone the existing wrapper.
2. `live_remove_input_param` — remove the old input parameter (e.g. `Diet`).
3. `live_add_entity_identifier_input` — add the new input (e.g. `DietId`).
4. `live_set_action_call` — change the ExecuteAction's server action (e.g.
   `DietCreate` → `DietDelete`).
5. `live_map_action_inputs(inputParamName="DietId")` — explicitly map inputs.

### Local calls (proven 2026-09-16, SS 11.55.83)

`live_add_action_call_node` / `live_set_action_call` resolve LOCAL server actions
(same module) when `producerModule` is empty — no consumption needed. Proven:
`ScratchWakeCaller` wraps local `ScratchTimerWake` (params, call node, success/error
assigns, All-Exceptions handler, layout — 0 errors). Same-module entity-CRUD wrappers
follow this exact shape; cross-module calls still go through `live_consume_elements`.

## Headless Approach (ROADMAP)

For headless `.oml` editing (no SS running), the approach is:

1. Confirm the entity exists (`probe_oml` / `get_fragment`).
2. For each CRUD operation, `create_service_action(name="<Entity>_<Op>_BL")`.
3. Add input/output parameters matching the entity's attributes.
4. Build the flow: Start → ExecuteAction → Assign result → End, with Exception Handler.
5. Regen, write, verify; reload in SS.

**Status:** Not yet fully tooled for headless flow building. Use the live
approach whenever possible.

## Platform semantics (what SS's own accelerator generates)

Condensed from the official O11 doc
`docs-product\src\building-apps\data\crud-wrappers.md` (OutSystems docs,
CC BY-NC-ND 4.0). Use this as the reference shape when generating wrappers —
our wrappers should mirror what SS's accelerator produces:

- **Shape:** 4 server actions in a folder named after the entity —
  `<Entity>Create`, `<Entity>CreateOrUpdate`, `<Entity>Delete`, `<Entity>Update` —
  each encapsulating the corresponding entity action, with input/output
  parameters and mandatory-attribute validations. **Public** status of the
  wrappers follows the entity's `Public` flag.
- **Mandatory-attribute If-node validates** these types only: `Text`, `Email`,
  `Phone Number`, `Date`, `DateTime`, `Time`, `Binary`, other Entity
  Identifiers. **NOT** validated: `Integer`, `Long Integer`, `Decimal`,
  `Currency`, `Boolean`, and basic audit attributes (those get dedicated
  Assign nodes instead).
- **Audit attributes** auto-filled by name+type when present: `CreatedOn`,
  `CreatedBy`, `UpdatedOn`/`ModifiedOn`, `UpdatedBy`/`ModifiedBy`.
- **Non-AutoNumber Ids:** the platform does not generate the Id — Create/
  CreateOrUpdate wrappers add an Id-set validation; the caller must supply a
  unique Id. (Note for live-created entities: see `creating-entities` — the Id
  must be added explicitly anyway.)
- **The accelerator does NOT adapt to data-model changes** — add/remove an
  attribute and the wrappers' validations/params are stale until regenerated.
  Same applies to our generated wrappers: after `live_add_entity_attribute`,
  re-check the wrappers that take the full entity record.
- **Best practices encoded by the platform:** prefer soft deletes (IsActive
  flag, see Entity `Is Active Attribute` in `docs/element-class-reference.md`),
  add custom checks (role validations, related-table updates), and use a
  separate audit-trail entity for full history.

## Status

- **Live approach: PROVEN** via `building-service-action-flows` (SS 11.55.81).
  All tools work: `live_create_service_action(withSkeleton=true)`,
  `live_add_action_call_node`, `live_set_action_call`, `live_add_entity_input`,
  `live_add_entity_identifier_input`, `live_remove_input_param`,
  `live_map_action_inputs(inputParamName=...)`, etc.
- **Headless approach: ROADMAP** — blocked on headless flow-node building.
- **Dependencies are proven:** `live_consume_elements` handles consuming server
  actions and entities from producer modules. See `managing-dependencies` skill.
