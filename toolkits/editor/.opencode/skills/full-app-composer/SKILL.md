---
name: full-app-composer
description: The master checklist the model follows to build a complete OutSystems Reactive app start-to-finish. Orchestrates all other skills. Read this first for any "build me an app" request.
---

# Full-App Composer — the start-to-finish checklist

> Rule: LIVE FIRST (module open in SS → `outsystems-liveeditor`). Headless only when
> SS is down. Every step ends with its verify line — never stack unverified work.

## 0. Open + baseline
1. `live_status` → bridge + module visible. If not: user opens SS + module (bridge cannot).
2. Receipt baseline: `live_module_info`, current `get_verify_errors` on touched screens (should be clean).

## 1. Data model (`creating-entities`, `adding-attributes`, `managing-dependencies`)
1. `live_create_entity` per domain entity + `live_add_entity_attribute` each
   (mandatory flags where true; defaults in SS — documented limitation).
2. Consume producer elements needed (`live_consume_elements`).
3. Verify: `live_debug_eSpace_collection_items("Entities")` → `IsValid: True`.

## 2. Logic (`building-service-action-flows`, `creating-crud-wrappers`, `exception-handlers-in-service-actions`)
1. Per entity: Create/Get/Update/Delete wrappers (clone-and-modify from the first green one).
2. Server actions creatable live (`try_create_server_action` ✅); service-action creation
   via collection paths (deploy-6; until then prefer server actions + screen/block client actions).
3. ForEach loops per `building-service-action-flows` + G9 canon (CycleTarget=body, body links back, Target=exit).
4. Flows to avoid: empty-lifecycle node creation (guard refuses — do it in SS).

## 3. Aggregates (`building-lists`, `pagination-sorting-search`, `expression-reference`)
1. One aggregate per list/table + Count twin for pagination.
2. Source (local entities ✅ via fallback; consumed ✅), filters (`Entity.Attr` scope ✅),
   sorts (CreateSort+SetOriginalAttribute path), paging expressions, calc attrs (SetValue path).
3. Expressions ONLY from `expression-reference` (bible) — 39 functions green-listed.

## 4. UI (`building-tables`, `building-lists`, `building-forms`, `screen-templates`*)
1. Screens from templates: List (search+table+sort+page) → Detail (input Id) → Form
   (optional Id, validate→save→event) → master-detail events.
   (*`screen-templates` lands after link-params + screen-vars tools; until then compose
   from the three widget skills + `building-screens-and-buttons`.)
2. Bind via parsed CPs; handlers to block/screen client actions; `get_verify_errors`
   per widget, then per screen — zero before moving on.

## 5. Style (`styling-and-css-live`)
1. Container classes via Style CP **quoted literals** (never raw — the `expr MINUS bible`
   lesson); `live_fix_style_literal` repairs old ones. Links/text via CustomStyle.
2. Theme CSS via `live_set_user_css` (shows in SS editor). Save → reopen → confirm both.

## 6. Security + config (`app-security`, `app-configuration`)
1. Roles + `Public=False` on private screens (+ grants when wrapper lands).
2. Site props for tunables; timers created live, scheduled in SS.

## 7. Publish (`publishing`)
1. Headless verify loop to zero → `Ctrl+S` → 1CP → triage → receipt under `docs/<Module>/`.

## G14 verdicts (Timers/Email/Excel/REST/Files)
- Timers: create live ✅, schedule SS-manual. Email/Excel/JSON/Download/Upload/SQL nodes
  EXIST as flow types (`IForEachNode`-style probing applies) — node tools land on demand.
  REST/SOAP: consume first (`managing-dependencies`), then standard action-call nodes.

## Status
- ✅ composer checklist + all referenced skills EXCEPT `screen-templates` (waits on 3 tools).
- Dry-run app (ScratchApp: 1 entity, List+Detail+Form) = the acceptance run (T15).
