---
name: building-tables
description: Use when the user wants a data Table (TableRecords) bound to an aggregate — source binding, sorts, columns. LIVE-first (proven core). Read live-editing + expression-reference first.
---

# Building Tables — LIVE (core proven SS 11.55.83)

## Create + bind (PROVEN)

```
live_add_nr_widget(module, screen, name="OrdersTable", kind="table")   # ServiceStudio.Plugin.NRWidgets.TableRecords
live_set_widget_source(module, screen, "OrdersTable", "GetOrders.List") # SetSource(String) — verifies GREEN
live_get_verify_errors(module, "widget", "OrdersTable", screen)         # expect 0
```

- Source is any `<Agg>.List` in scope (screen aggregate, data-action list, block event payload).
- Table exposes `ShowHeader` (bool), `HeaderRow`/`Row` templates, `OnSort` event,
  `Style/StyleHeader/StyleRow`, `Visible` — probed live, see below for status.

## SCREEN TableRecords — full rebuild pattern (PROVEN 2026-09-19, ZombieGame WaveTbl)

Use TableRecords when a bare `List`+`ListItem` is NOT viable on a screen. Getting a
fully wired, clickable row on a SCREEN (all 0 verify errors):

```
# 1. table + source
live_add_nr_widget(module, screen, parent='MainContent', name="WaveTbl", kind="table")
live_set_widget_source(module, screen, "WaveTbl", "WavesAgg.List")
# 2. row cells (dotted parent):
#    row cells are EXPRESSIONS, not text:
live_add_nr_widget(module, screen, parent="WaveTbl.Row", name="WaveCellName", kind="expression")
live_set_screen_cp_parsed(module, screen, "WaveCellName", "Value", "WavesAgg.List.Current.Wave.Name")
# 3. clickable cell -> link to another screen with a param:
live_add_nr_widget(module, screen, parent="WaveTbl.Row", name="WaveCellAction", kind="link")
live_add_inside_placeholder(module, screen, "MainContent", "WaveCellAction", "text", "FightLink", "FIGHT", "Battle")
live_set_link_params(module, screen, "FightLink", "WaveId", "WavesAgg.List.Current.Wave.Id")
```

Why NOT `List`/`ListItem` here: `live_add_nr_widget(kind="list")` auto-creates an
anonymous `ListItemAction` that reports "On Click must be set" and there is **no
screen-side way to delete the anon action or set its handler** (the anon-delete tool
is block-scoped). TableRecords rows don't create ListItemActions, and expression
cells bind cleanly with `set_screen_cp_parsed` (Value CP) — the link-in-cell pattern
above passes the current record's Id into `Battle` via `set_link_params`.
Verify: `live_get_verify_errors` on the table (Source + cells) must stay 0.



## Sorts live on the AGGREGATE, not the table (PROVEN pattern, scope rules apply)

```
live_add_aggregate_sort(module, screen, "GetOrders", "Title")   # CreateSort + SetOriginalAttribute, delta-verified
```

- Sort-attribute scope is under discovery (bare vs qualified — the tool tries forms and
  keeps the winner; read its report). Filters use plain `Entity.Attr` scope and verify green.
- Direction defaults ASC; DESC support is best-effort (see tool report).

## Columns (PROVEN 2026-09-16)

Row/HeaderRow are `IContent` (not `FindWidget`-addressable). Address them dotted:

```
live_add_nr_widget(kind="expression", parent="OrdersTable.Row", name="CellTitle", ...)
live_add_nr_widget(kind="label", parent="OrdersTable.HeaderRow", name="HdrTitle", text="Title")
```

- The bridge resolves `<Table>.Row` / `<Table>.HeaderRow` to the content object
  (create-or-`ChangeParent` fallback inside).
- Cell scope: `<Agg>.List.Current.<Entity>.<Attr>` (e.g.
  `ProbeAgg.List.Current.ScratchOrder.Title`) — verifies GREEN **only** inside a row
  scope, which itself proves placement (the same expression at screen root would be red).
- Header cells are plain labels (static text). Sortable headers, two patterns:
  header-Link → OnClick → sort action (fallback, see pagination-sorting-search), or
  NATIVE `OnSort` event (canon since 2026-09-17 — see "Native OnSort" below).

## Native OnSort (PROVEN 2026-09-17, SS 11.55.83 — decompiled platform contract)

The table raises `OnSort(ClickedColumn)` — decompiled from
`ServiceStudio.Plugin.NRWidgets.TableRecordsDescriptor.OnSortEvent`:
`EventParameter("ClickedColumn", Text, "Column clicked in runtime to be sorted")`.
The handler action carries an OPTIONAL Text input `SortBy` fed from `ClickedColumn`;
the aggregate sorts by a DYNAMIC OrderBy on a screen Text var (`TableSort`),
toggled `"col"` / `"col DESC"` — decompiled from
`ClientScreenActionFlow.CreateStandardNodesForOnSort` (+ `RemoveDefaultSort`).

Recipe (all live, each an undo unit; `ScratchHome.ProbeTable` → `TableSort` proof):

```
live_create_screen_client_action(module, screen, "TableSort")
live_debug_create_node(TableSort, IStartNode) + live_debug_create_node(TableSort, IEndNode)
live_set_node_target(Start -> End)
live_set_widget_handler(module, screen, widget=ProbeTable, event=OnSort, actionName=TableSort)
  # via EventHandlers.OnSort + handler.Destination (builtin branch, like Input.OnChange)
live_add_input_param(TableSort, SortBy, Text)
  # NOTE: the factory default is MANDATORY; the platform generates it OPTIONAL
  # (raw GenericInputParameter ctor; IsMandatory's setter is a no-op, ActivityInput
  # is the real surface). In practice the red clears as soon as the handler argument
  # below is mapped — `live_set_input_param_mandatory` exists but is NOT needed
  # for green (its SetProp path needs a SetPropForce upgrade — deferred).
live_set_widget_handler_arg(module, screen, ProbeTable, OnSort, SortBy, ClickedColumn)
  # THE fix for ArgumentRequiredExpression AND for runtime function: feeds the
  # event payload into the action input. ClickedColumn resolves in handler scope
  # (verify 0 proves it). Handler shows exactly 1 arg (SortBy).
live_add_screen_variable(module, screen, "TableSort", "Text")
# Flow body (mirrors CreateStandardNodesForOnSort; PageNumber=1 is the var-driven
# equivalent of the generator's StartIndex=0 when paging is var-bound):
If(TableSort = SortBy and SortBy <> "")
  True:  Assign TableSort = SortBy + " DESC"     # re-click toggles DESC
  False: Assign TableSort = SortBy               # new column sorts ASC
  both -> Assign PageNumber = 1                  # reset to first page + comment
       -> RefreshQuery(ProbeAgg) -> End
live_remove_aggregate_sort(module, screen, ProbeAgg, ScratchOrder.Title)  # RemoveDefaultSort equiv
live_add_aggregate_dynamic_sort(module, screen, ProbeAgg, TableSort)      # AddOrderBy(var, dynamic:true)
live_get_verify_errors(action+screen+widget)                              # expect 0/0/0
```

Caveats (proven 2026-09-17):
- `add_aggregate_dynamic_sort`'s `Parent.MoveChildTo(sort, 0, false)` step FAILS
  (`MoveChildTo/3` not found on CombineSources — likely an extension method; the sort
  itself creates fine and green). Harmless in the canon path: `remove_aggregate_sort`
  clears statics first so the dynamic sort lands alone at index 0. Multi-sort ordering
  fix deferred to the next bridge batch (try `MoveToNewAbsoluteIndex/1`, then
  best-effort skip-with-report).
- The dynamic sort verifies green with direction Ascending; the `" DESC"` toggle is a
  runtime string convention (`TableSort` var holds `"col"` / `"col DESC"`).

Mechanics (hard-won — follow exactly):
- `Target` is NOT settable on If/End nodes (only TrueTarget/FalseTarget on If).
  `beforeEnd` creation needs a `.Target → End` backbone that an If can never provide.
- `action.Nodes` RE-SEQUENCES after every link change: NEVER reuse an index across
  commands — `live_list_flow` immediately before each indexed op.
- Build order that works: If standalone → Start→If → assigns via `afterAnchor`
  (content-addressed, immune to resequencing) → Refresh via `beforeEnd` once an
  assign points at End → explicit True/False/Target wiring with fresh indices.
- A FAILED command may roll back the PREVIOUS undo unit (observed: Start→If link
  lost) — after any failure, re-list + re-verify + re-apply prior wiring.
- `add_refresh_node` bridge dispatch now forwards `afterNodeIndex` (was silently dropped).

## Empty state

## Empty state

No EmptyMessage widget exists. Canon: `If GetOrders.List.Empty` (or `.List.Length = 0`
— verify which) showing a Text placeholder, table `Visible = NOT Empty`. Exact empty
predicate: verify-pending — test both forms with `get_verify_errors` + runtime check.

## Verify
- `live_get_verify_errors` on widget (source/cells) and screen (aggregate ops).
- Table with Source + no columns verifies GREEN (proven: ProbeTable, 0 errors).

## Status
- ✅ create, Source binding (green), aggregate sorts/filters/paging companions.
- ✅ NATIVE OnSort wiring + handler body + dynamic-sort contract (this section);
  deploy-8 tools (`live_remove_aggregate_sort`, `live_add_aggregate_dynamic_sort`,
  `live_set_input_param_mandatory`, `live_set_widget_handler_arg`) coded + built,
  live proof after swap+restart gate.
- ⚠️ Row/HeaderRow nesting, ShowHeader setter, empty predicate — probe next.
