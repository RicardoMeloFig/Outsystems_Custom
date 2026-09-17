# Element Class Reference (OutSystems 11) — mapped to our live tools

Condensed ground truth for what each flow/data/UI element **is**, its key
properties (with platform defaults), and which `live_*` tool creates/edits it.
Use this when a flow edit needs to know what SS will validate at verify/publish.

> **Source + license.** Summarized and condensed from the official OutSystems 11
> product documentation, `docs-product\src\ref\lang\auto\class-*.md`
> (OutSystems docs, CC BY-NC-ND 4.0). Wording is ours; consult the source
> `class-*.md` files for the full property tables.

## Quick map: element → our tools

| Element | What it is | Our tools (live) |
|---|---|---|
| Service Action | Module-level action, server-executed, consumable | `live_create_service_action`, flow tools |
| Server Action | Server-side logic unit; `Public`/`Function` flags | `live_create_server_action`, `live_add_action_call_node` (to call it) |
| Client Action | Client-side logic unit | `live_create_client_action`, block/screen client actions |
| Screen Action | Logic bound to a screen (what Button OnClick targets) | `live_create_screen_client_action` |
| Start / End | Flow begin / termination | `live_debug_create_node('IStartNode'/'IEndNode')` |
| Assign | Variable = Value pairs, applied top→bottom | `live_add_assign_node`, `live_add_assignment_to_node` |
| If | Boolean condition → True/False branches | `live_add_if_node`, `live_set_connector_target` |
| Switch | First true condition wins; Otherwise fallback | `live_add_switch_node`, `live_set_switch_case` |
| For Each | Iterate a Record List (Cycle body) | flow node tools |
| Exception Handler | Starts an exception-handling flow | `live_debug_create_node('IExceptionHandlerNode')`, `live_set_error_handler_exception` |
| Raise Exception | Throw + terminate the current flow | flow node tools |
| Input Parameter | Data passed in; `Is Mandatory` default **Yes** | `live_add_input_param`, `live_add_entity_input` |
| Output Parameter | Data returned; set via Assign in-flow | `live_add_output_param`, `live_set_output_param_type` |
| Local Variable | Scoped to parent element/action | `live_add_local_variable`, `live_add_variable_to_block` |
| Run Server Action | Call a server action from a flow | `live_add_action_call_node`, `live_map_action_inputs` |
| Aggregate | Optimized data fetch (client- or server-side) | `live_add_aggregate_to_screen/block`, `live_add_aggregate_filter` |
| Data Action | Async server-side fetch after screen load | `live_add_data_action_to_screen/block` |
| Refresh Data | Synchronous re-fetch of a data source | `live_add_refresh_node` |
| Ajax Refresh | Partial screen refresh (one widget per node) | client-action flow tools |
| Trigger Event | Raise a block Event up to the parent | `live_add_raise_event_node`, `live_set_raise_event_arg` |
| Entity | Database table | `live_create_entity` |
| Entity Attribute | Table column | `live_add_entity_attribute` |
| Structure | Compound data type | `live_create_structure`, `live_add_structure_attribute` |

---

## Actions

### Service Action
- Identified by `Name`; `Icon` mandatory. Pure container for server-executed logic.
- Property limits: `Description` max **2000 chars** (applies to every element below too).

### Server Action
- Runs on the server. Key properties:
  - `Public` (default **No**) — required Yes for other modules to consume it.
  - `Function` (default **No**) — turns it into a function usable in expressions;
    **must return a value**; only for module-level actions; only callable in
    server-side expressions.
  - `Cache in Minutes` (advanced; default none).
- **Exposure constraint:** an action cannot be exposed (`Public=Yes`) when any
  parameter's type is an Entity/Structure that is not exposed, or that is itself
  reused from another module. Same constraint applies to Client Actions,
  Structures, and (Traditional) Web Blocks — remember it whenever we make a
  cloned action public with entity-typed parameters.

### Client Action
- Runs on the client. Same `Public` / `Function` pattern as Server Action
  (functions usable only in client expressions).
- Exists module-level, **block-level** (our block ClientActions), and
  **screen-level** (what a Button OnClick requires — see
  `building-screens-and-buttons`).

### Screen Action
- Logic bound to a screen, triggered by user interaction. Properties: `Name`,
  `Description`. This is the Reactive "screen-level Client Action"
  (`ClientScreenActionFlow`) our Button OnClick wiring targets.

---

## Flow nodes

### Start / End
- `Start`: where a flow begins executing. No settable logic properties.
- `End`: terminates a flow path. In **process flows** (BPT) it additionally has
  `Terminate Process` (force-stops the whole process) — not relevant to
  action flows.

### Assign
- Contains one or more **Variable = Value** pairs; multiple assignments in one
  node are applied **top to bottom** (order matters — a later assignment can
  read an earlier one's result).
- Values can be literals, variables, or computed expressions.
- **Assignment semantics:** OutSystems assigns **by value** except
  **Record List, Binary Data, Object** — those assign **by reference**. Use
  `ListDuplicate()` for a by-value copy of a list.

### If
- `Condition` (mandatory): Boolean literal or expression. True branch /
  False branch. (UI has "Swap Connectors"; our tooling just wires
  `TrueTarget`/`FalseTarget`.)

### Switch
- Splits flow into 2+ paths; **the first condition evaluating True wins**;
  none true → **Otherwise** path runs. Each link needs a condition except
  Otherwise. Links have a priority order.

### For Each
- Iterates a **Record List**. Properties:
  - `Record List` (mandatory),
  - `Start Index` (default **0**, may be an expression),
  - `Maximum Iterations` (default the list's full length).
- Inside the cycle body, `Current` is the element of the current iteration.
- Cycle body connector is labeled **Cycle**; the exit connector is unlabeled.

### Exception Handler
- Starts an exception-handling flow. Properties:
  - `Exception` (mandatory) — the type to catch; **`All Exceptions`** is the
    catch-all. There is a **hierarchy**: a handler for a parent type also
    catches child types (e.g. a specific handler catches its User Exception
    children).
  - `Abort Transaction` (default **Yes**) — rollback uncommitted DB writes.
    Server-side only: **not available** when handling exceptions client-side
    in Reactive/Mobile.
  - `Log Error` (default **Yes**) — writes to Service Center **Monitoring >
    Errors**.
- Runtime property available inside the handler flow:
  `ExceptionMessage` (Text, read-only) — the reason for the error. This is the
  canonical `AllExceptions.ExceptionMessage` we assign in error paths.
- Our tooling: `live_set_error_handler_exception` sets `Exception` **and**
  defaults `AbortTransaction=true`, `LogError=true` — matching the platform
  defaults.

### Raise Exception
- Throws an exception and **ends the current flow** (nothing may follow it).
  Properties: `Exception` (type; may be an existing type or a **User
  Exception**) and `Exception Message` (mandatory for User Exceptions; used
  for logging/UI). Execution resumes from the handler that catches it.
  Don't surface internal details in end-user-visible messages.

### User Exception
- A custom exception type, created as a child of the general User Exception.
  Property: `Name`. Define one per application-specific failure
  (e.g. `UnavailableExternalSystem`), then Raise it where detected and handle
  it where meaningful.

---

## Parameters and variables

### Input Parameter
- Brings data **into** an element's scope (actions, screens, blocks, REST/SOAP
  methods, SQL, JavaScript, emails, processes...).
- Properties: `Name`, `Data Type` (mandatory), `Is Mandatory` (default
  **Yes**), `Default Value` (defaults to the type's default), `Description`.
- **Scope rule:** once you call another action, the input parameter is no
  longer in scope — pass it explicitly as an argument of the nested call.
  (This is why our generated wrapper flows re-map inputs onto every nested
  server-action call.)

### Output Parameter
- Returns computed values from actions/processes. Properties: `Name`,
  `Data Type` (mandatory), `Default Value`, `Description`.
- Set it with an **Assign** inside the flow.
- Read a callee's outputs downstream via
  `<flow_element_name>.<output_parameter_name>` (e.g. `API_GetWeatherData.Data`).

### Local Variable
- Exists **only in the scope of its parent** (screen, action, block,
  automatic activity); destroyed when execution leaves it. Properties:
  `Name`, `Data Type` (mandatory), `Default Value`, `Description`.
- Typical use: holding a search keyword that feeds an Aggregate filter
  (`Employee.FirstName like "%" + SearchKeyword + "%"`).

---

## Calling and data nodes

### Run Server Action / Run Client Action
- Executes an action from a flow. Property: `Action` (the target). Mandatory
  input parameters **must** be provided as arguments on the node.
- Outputs become available **only after the node in flow order**
  (there must be a path from the call to where the output is used).
- Our tooling: `live_add_action_call_node` creates + targets the call;
  `live_map_action_inputs` maps arguments by name.

### Aggregate
- Optimized, model-absorbing data fetch. In Reactive/Mobile: **client-side**
  (screens/blocks, run at load) or **server-side** (action flows); Traditional
  Web: server-side only.
- Key behavior: brings only the attributes actually used; supports multiple
  sources with joins, filters, sorts, calculated attributes, grouping/distinct.
- `Max. Records`: in Screen Aggregates set it above the expected row count when
  fetching "all"; in Data Actions leaving it empty = fetch all.
- Naming: dragging entity `MyEntity` creates aggregate `GetMyEntity`.
- Our tooling: `live_add_aggregate_to_screen/block` (+ `entityName` source),
  `live_add_aggregate_filter`, `live_set_block_aggregate_source`.

### Data Action
- Server-side fetch that runs **after the screen loads**, concurrently with
  client-side aggregates. Properties: `Fetch` (default **At start**),
  `Server Request Timeout` (seconds; triggers Communication Exception),
  `On After Fetch` event.
- Runtime props (read-only): `IsDataFetched`, `HasFetchError`.
- Gotcha: **avoid login operations inside Data Actions** (races cookies with
  concurrent requests).

### Refresh Data
- Synchronous re-fetch of an **existing** data source; blocks the flow while
  fetching. Properties: `Data Source` (mandatory), `Max. Records`,
  `Start Index`. This is what our `live_add_refresh_node` wires into
  OnParametersChanged flows.

### Ajax Refresh
- Refreshes part of the screen without a full page reload. Rules:
  **one widget per node** (wrap multiple widgets in a Container and refresh
  the container); the refreshed element must have a `Name`; multiple Ajax
  Refresh nodes run in flow order; refreshing a block re-runs its
  preparation/lifecycle fetches.

### Trigger Event
- Raises an **Event defined on a block** from inside the block's logic.
  Property: `Event` (mandatory). Each of the event's input parameters gets an
  argument on the node — our `live_set_raise_event_arg` fills them.

---

## Data model elements

### Entity
- A database table. Key properties:
  - `Public` (default **No**) — expose for consumption by other modules.
  - `Expose Read Only` (default **No**) — consumers can read but not write
    (note: raw SQL can still write; not a hard guarantee).
  - `Identifier Attribute` — the unique key; `Label Attribute` — shown in
    combo boxes / scaffolded tables; `Order By Attribute`; `Is Active
    Attribute` (Boolean soft-delete filter honored by queries).
  - `Update Behavior` (default **Changed Attributes**) — `CreateOrUpdate`/
    `Update` write only changed attributes when the record came from an
    Aggregate/SQL and was flagged changed via Assign.
  - `Is Multi-tenant`, `Show Tenant Identifier` (default **No**).
- Our tooling note (`creating-entities`): live-created entities do **not**
  auto-generate the `Id` attribute — add it explicitly.

### Entity Attribute
- A column. Properties:
  - `Data Type` (mandatory) — must be a **basic type or an Entity Identifier**.
  - `Length` (Text), `Decimals` (Decimal, mandatory there),
  - `Is AutoNumber` (default **No**), `Is Mandatory` (default **No** — used by
    Form validations),
  - `Delete Rule` (default **Protect**; `Protect`/`Delete`/`Ignore`; only for
    Entity Identifier FK attributes; external-DB FKs force `Ignore`),
  - `Default Value`, `Label`, `Description`.

### Structure
- Compound data type (group of attributes) for parameters, REST payloads,
  cross-action data. Property: `Public` (default **No**). Same exposure
  constraint as actions: an attribute typed with a non-exposed or reused
  Entity/Structure blocks exposing it.
- Our tooling: `live_create_structure` + `live_add_structure_attribute`
  (+ `live_set_structure_attribute_type` to fix a type after the fact).

### Web Block (Block)
- Reusable screen part with its own logic. In the O11 docs this page is
  Traditional-Web-worded; the Reactive equivalent is the **Block** our live
  tools manipulate (`live_create_web_block`, `live_add_widget_to_block`,
  block Input Parameters, block Events, lifecycle handlers).
- Same exposure constraints as actions (non-exposed/reused parameter types).
- Can carry its own `Style Sheet` and `JavaScript`.

---

## Cross-cutting rules our edits must respect

1. **`Description` ≤ 2000 chars** on any element we document via
   `live_set_element_description`.
2. **Exposure constraint** (actions/structures/blocks): a parameter typed with
   a non-exposed Entity/Structure — or one reused from another module — blocks
   `Public=Yes`.
3. **Assign order matters** within a node; lists/binary/object assign by
   reference.
4. **Mandatory defaults**: input parameters default to mandatory; exception
   handlers default to `AbortTransaction=Yes` + `LogError=Yes`; entity delete
   rules default to `Protect`.
5. **Outputs live downstream**: a call's output parameters are only readable
   at/after the call node in flow order.
6. **Input params don't cascade**: after calling another action, the parent's
   input parameters are out of scope — pass them explicitly.
