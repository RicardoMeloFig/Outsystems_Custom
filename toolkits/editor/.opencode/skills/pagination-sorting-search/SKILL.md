---
name: pagination-sorting-search
description: Use when the user wants paginated/sorted/searched tables (page vars, prev/next, sort headers, search box). LIVE-first (mechanics proven; page vars + refresh need next-batch tools). Read building-tables + expression-reference first.
---

# Pagination + Sorting + Search (mechanics PROVEN, wiring NEXT)

## Aggregate mechanics (PROVEN live)

```
live_set_aggregate_paging(module, screen, "GetOrders", "10", "0")   # SetMaxRecords/SetStartIndex, any expression text
live_add_aggregate_sort(module, screen, "GetOrders", "Title")       # CreateSort+SetOriginalAttribute, delta-verified winner
live_add_screen_aggregate_filter(module, screen, "GetOrders", "ScratchOrder.IsDone = False")  # Table.AddFilter, green
```

MaxRecords/StartIndex accept ARBITRARY expression text — the pagination pattern binds
them to page vars (below). A second Count aggregate (same filters) drives `of Z`.

## Canonical pattern (compose; tool gaps marked)

```
PageNumber (1-based), PageSize (= site prop ScratchPageSize pattern), TotalCount,
SearchText (Text), SortColumn (Text), SortAsc (Boolean)
StartIndex = (PageNumber - 1) * PageSize        -- aggregate StartIndex expression
MaxRecords = PageSize                            -- aggregate MaxRecords expression
Prev: PageNumber = Max(1, PageNumber - 1) → refresh; Next: clamp to Ceil(TotalCount / PageSize)
Search input OnChange: PageNumber = 1 → refresh
Sort header click: toggle SortAsc / set SortColumn → rebuild sort → refresh
Label: "Showing " + IntegerToText((PageNumber-1)*PageSize+1) + "–" + ... + " of " + IntegerToText(TotalCount)
```

## Tool gaps (CLOSED 2026-09-16 — all live)

1. ~~Screen local variables~~ ✅ `live_add_screen_variable` (type best-effort).
2. ~~Refresh~~ ✅ `live_add_refresh_node` with screen aggregates (dual-Target fix inside).
3. ~~Dynamic sort/filter rebuild~~ ✅ `live_delete_aggregate_sort` / `live_delete_aggregate_filter`
   (by index) + re-add — replace cycle proven green (delete 2→1, re-add, 0 errors).
4. **Count aggregate**: same-filters twin is manual composition (no shortcut).
5. **Runtime rows**: page-turning with real data needs seeded rows (no insert tool).

## Verify
- After each op: screen `get_verify_errors` (sort/filter/paging must not add errors).
- Runtime: 25-row seed check (T6) once vars+refresh land.

## Status
- ✅ paging/sort/filter mechanics + delta-verify discipline.
- ⚠️ vars, refresh, dynamic sorts, count twin — block T6; next batch.
