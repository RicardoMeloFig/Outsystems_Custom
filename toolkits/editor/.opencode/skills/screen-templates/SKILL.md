---
name: screen-templates
description: Copy-paste runbooks for the five standard screens (List, Detail, Form, MasterDetail, AuthShell) — every call proven live. Read building-tables + building-forms + expression-reference first.
---

# Screen Templates — LIVE (all calls proven SS 11.55.83)

Provenance: every step below ran green on `ScratchHome`/`Dashboard`/`ProbeAgg`
(entity `ScratchOrder`). Verify with `get_verify_errors` after EACH step.

## T0. Scaffold (any template)

```
live_create_web_screen(module, flow, "<Name>")
live_set_screen_title(module, "<Name>", "<Title>")
```

## T1. ListScreen (search + table + sort + page + New button)

```
# data
live_add_aggregate_to_screen(module, screen, "GetOrders", "ScratchOrder", "")  # local-entity fallback
live_add_screen_aggregate_filter(module, screen, "GetOrders", "ScratchOrder.IsDone = False")
live_add_aggregate_sort(module, screen, "GetOrders", "ScratchOrder.Title")      # UI-bytecode path, delta-verified
live_set_aggregate_paging(module, screen, "GetOrders", "PageSize", "(PageNumber-1)*PageSize")
# page vars (initialize PageNumber=1 via OnInitialize assign; 0-based guard in expressions)
live_add_screen_variable(module, screen, "PageNumber", "Integer")
live_add_screen_variable(module, screen, "PageSize", "Integer")
live_add_screen_variable(module, screen, "SearchText", "Text")
# widgets
live_add_nr_widget(kind="table", name="OrdersTable")
live_set_widget_source(module, screen, "OrdersTable", "GetOrders.List")
live_add_nr_widget(kind="input", name="SearchBox")
live_set_block_cp-equivalent Variable=SearchText (screen CP path — same SetValueExpression mechanics)
live_add_button(module, screen, "Actions", "", "NewBtn", "New")  # → screen client action → navigate Form (no Id)
# refresh action (search change / page buttons call this)
live_create_screen_client_action → Start→End skeleton (manual nodes+link)
live_add_refresh_node(action, screen, "GetOrders", beforeEnd)    # dual-Target fix inside; verifies 0 errors
```

- Search wiring: `SearchBox.OnChange` → client action (set filter expression referencing
  `SearchText`, `PageNumber=1`, refresh node) — filter-REPLACE needs no remove (one filter per aggregate; rebuild aggregate to change it).
- Sort headers: one static sort per aggregate (no remove/replace yet — `delete_aggregate_sort`
  exists for cleanup by index).
- Empty state: `If GetOrders.List.Empty` + Text (predicate: verify both `.Empty`/`Length` forms).

## T2. DetailScreen (required Id input → read-only card)

```
live_add_screen_input_param(module, "OrderDetail", "OrderId", "Text")   # screen.CreateInputParameter, proven
live_add_aggregate_to_screen(... "GetOrder" ...) + filter "ScratchOrder.Id = TextToIdentifier(OrderId)"
# card widgets bound to GetOrder.List.Current.ScratchOrder.<Attr>
live_add_link(..., targetScreen="OrderDetail") from List + live_set_link_params("OrderId=" + quoted/current id)
```

- Param passing: `live_set_link_params(module, screen, link, "OrderId=\"...\"")` — text
  literals MUST be double-quoted (bare text reads as variable → red). Proven green.
- Receiving side needs the input BEFORE the link args materialize (add input first).

## T3. FormScreen (optional Id → blank-or-loaded → validate → save)

```
live_add_screen_input_param(module, "OrderForm", "OrderId", "Text")   # empty = create mode
live_add_nr_widget(kind="form", name="OrderForm") + fields (building-forms matrix)
# save = SCREEN client action: validate (If empty → Message+abort) → server-action call
#   (CreateOrUpdate via consumed/server action) → Message(Success) → link back to List
```

- Validation canon: `building-forms` (client If-checks + error Messages).
- Post-save navigation: Link with params back to List (same `set_link_params` path).

## T4. MasterDetail (block event → parent refresh)

- Detail block raises custom event with Id payload (`live_add_event_to_block` +
  `live_add_event_param_to_block` + RaiseEvent node — UserProfileCard canon).
- Parent handles event → refresh detail aggregate (`live_add_refresh_node` in handler flow).
- List row action/button raises selection (row-click wiring: row widget OnClick →
  block action → RaiseEvent).

## T5. AuthShell (login gating)

- `live_create_role` + `live_set_screen_permissions(isPublic=false)` + grant wrapper —
  see `app-security`. Anonymous → InvalidPermissions (platform behavior).
- Login screen: platform Login action; stamp rows with `UserId` server-side.

## Navigation checklist (G8, proven)

1. Receiving screen input exists FIRST (`add_screen_input_param`).
2. Link/button destination set (`targetScreen` or OnClick action that navigates).
3. `set_link_params` with QUOTED literals; verify 0 errors on the source screen.
4. Round-trip: List → Detail(Id) → Form(Id?) → save → List refresh.

## BLAST-RADIUS RULE (proven the hard way 2026-09-16)

> NEVER add a required input to a LIVE screen with existing callers. Adding `FromHome`
> to `Dashboard` broke 4 real menu links at once (every navigation to that screen must
> then pass the argument). Reverted via `live_remove_screen_input_param`; proof redone
> on scratch `ScratchDetail` (zero callers) — all green.
>
> Options in order: (1) prove navigation on scratch screens (canon), (2) give the input
> a default (unverified surface), (3) update every caller (invasive — only for real
> features, never for tests).

## Status
- ✅ all five templates composable from proven calls (T1/T2/T3/T5 proven end-to-end on scratch).
- ⚠️ T4 row-click + dynamic sort-rebuild + search-filter-replace: manual/SS or next batch.
