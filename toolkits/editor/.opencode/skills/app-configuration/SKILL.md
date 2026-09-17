---
name: app-configuration
description: Use when the user wants site properties, timers, images/resources, validator texts, or environment config. LIVE-first (site props + timers proven). Read live-editing first.
---

# App Configuration — LIVE (proven core SS 11.55.83)

## Site properties (PROVEN, one caveat)

```
live_create_site_property(module, "PageSize", "Integer")   # IESpace.CreateSiteProperty(shared=true) + DataType — green
```

- `shared` defaults true (multi-tenant shared); pass `"false"` for tenant-private.
- ✅ creation + typing proven (`ScratchPageSize : Integer`).
- ⚠️ DEFAULT VALUE has no settable surface (tried Value/DefaultValue — absent).
  Set defaults in SS once, or write them at first use (`If(Site.PageSize = 0, 10, ...)` guard pattern).
- Inspect: `live_debug_eSpace_collection_items(module, "SiteProperties")`.
- Pattern: page sizes, feature flags, endpoint URLs, batch sizes — never hardcode.

## Timers (PROVEN shell)

```
live_create_timer(module, "NightlyCleanup")   # IESpace.CreateTimer — green
```

- **RULE: the wake action must take NO input parameters** — timers fire without
  arguments, so an action with inputs can never run (proven 2026-09-16: retargeted
  `ScratchTimer` from `ScratchProbe_Server` (has `OrderList` input) to a dedicated
  parameterless `ScratchTimerWake`, timer flipped to `IsValid: True`).
- Create the wake action parameterless from the start (`live_create_server_action` +
  Start→End skeleton; add flow later). Never reuse a parameterized action.
- Schedule + wake action are set in SS (Timer property grid) — no live tool (by design:
  schedules are ops config). The Wake action is a normal server action: build its flow
  with all flow tools, wire it as the timer's action in SS.
- G14 verdict: creation live ✅, scheduling manual, flow logic fully tooled.

## Images / Resources / Scripts

- Consumable via `live_consume_elements` (`Image:*`, `Resource:*`, `Script:*`) ✅.
- Creation needs binary payloads (`CreateImage(Byte[],...)`, `CreateResource(Byte[],...)`)
  — no live tool (payloads don't belong in chat-sized calls). Workflow: add the file in
  SS once → bind via `live_set_block_cp Image/Source` (proven UserProfileCard pattern
  incl. placeholder-image fallback).
- Module validator texts (`MandatoryValidatorMsg` … `EmailValidatorMsg` on ESpace):
  surface confirmed, setter unbuilt — nice-to-have batch item, not needed for apps.

## Environment discipline
- Dev vs Prod values = Site Properties (deployed per environment), never branches.
- `OnApplicationReady` / `OnSessionStart` system events exist for bootstrap seeds.

## Status
- ✅ site props, timers, consume images/resources/scripts, config patterns.
- ⚠️ defaults, schedules, binary uploads — SS-manual by design (documented, not gaps).
