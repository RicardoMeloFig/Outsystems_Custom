---
name: building-lists
description: Use when the user wants a data List (cards, tags, activity feeds) bound to a list source. LIVE-first (proven core). Read live-editing + expression-reference first.
---

# Building Lists — LIVE (core proven SS 11.55.83)

## Create + bind (PROVEN)

```
live_add_nr_widget(module, screen, name="OrdersList", kind="list")      # NRWidgets.List
live_set_widget_source(module, screen, "OrdersList", "GetOrders.List")  # SetSource(String) — verifies GREEN
```

Proven precedent (UserProfileCard, kept as reference): block List
`ActivityTagsList.Source = FetchUserStats.List.Current.RecentActivityTags`
(nested list path), item expression `Item.Tag`-shaped (`Current.<Attr>` inside Item).

## Item scope (pattern — HONOR EXACTLY)

```
<Agg>.List.Current.<Entity>.<Attr>            aggregate item (e.g. GetUserById.List.Current.User.Name)
<DataAction>.List.Current.<Field>             data-action item
<DataAction>.List.Current.<ListField>.List.Current.<Attr>   nested list item
```

`List` before EVERY `Current`; entity segment present for aggregates, absent for
structures. See expression-reference §6. A scope violation is the #1 list red
(use `get_verify_errors` per widget to isolate).

## List chrome (probed, wiring NEXT)

- `Mode`, `AnimateItems` (bool), `Tag`, `Widgets` (item children), `OnScrollEnding`
  (infinite-scroll event), `CreateWidget(String,IKey)` child factory.
- Item children via `CreateWidget` on the List instance — NO live tool yet; add widgets
  beside the list or in SS until `add_list_item_widget` lands.
- `ListItem` / `ListItemAction` kinds exist (Action rows with swipe/click) — unwired.

## Separator / empty state
Same as tables: If-over-Empty pattern (predicate verify-pending), separator via item
container bottom border (Style class).

## Verify
- Per-widget `get_verify_errors` (source + each item expression), then screen-level.
- List with Source verifies GREEN with zero items (proven: ProbeList, 0 errors).

## Status
- ✅ create, Source binding (green), scope grammar, nested-list precedent.
- ⚠️ item-child factory, Mode/OnScrollEnding wiring, empty predicate — next.
