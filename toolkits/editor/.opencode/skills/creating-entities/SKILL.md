---
name: creating-entities
description: Use when the user wants to create a server Entity in an OutSystems module. LIVE-first (proven): live_create_entity via IESpace.CreateServerEntity. Headless .oml approach is fallback/roadmap.
---

# Creating Entities — LIVE (proven SS 11.55.83)

Create a server Entity in a module that is OPEN in Service Studio. Proven on
FitnessManager (`ScratchOrder` + throwaway `ScratchTmp`, full lifecycle).

## Live runbook

```
live_create_entity(module, "ScratchOrder")                                    # 1. entity shell
live_add_entity_attribute(module, "ScratchOrder", "Title", "Text", "true")    # 2. attrs
live_add_entity_attribute(module, "ScratchOrder", "Quantity", "Integer")
live_add_entity_attribute(module, "ScratchOrder", "DueDate", "DateTime")
live_add_entity_attribute(module, "ScratchOrder", "IsDone", "Boolean")
live_debug_eSpace_collection_items(module, "Entities")                        # 3. verify (IsValid, Server kind)
```

- `live_create_entity` rejects duplicates; returns `ServiceStudio.Model.Entity`.
- `live_add_entity_attribute(entity, attrName, type, isMandatory?, defaultValue?)`:
  type resolves basic (`es.<type>Type`) + ListTypes + Structures + Entities.
  `isMandatory="true"` works; entity-attr **DefaultValue has no settable surface**
  (reported `(not set)`) — set defaults in SS or at write-time, not on the entity.
- `ExposeCreateAndChangeActions=True` gives free `Create/Update` server actions —
  consume them for CRUD wrappers.
- ⚠️ LIVE-created entities do NOT auto-get an `Id` attribute (proven 2026-09-16:
  `ScratchOrder.Id` unresolvable — no Id column existed — until an explicit
  `Id : LongInteger (mandatory)` attribute was added via `live_add_entity_attribute`).
  ALWAYS add `Id` explicitly right after creation. Whether the added Id carries true
  identifier/auto-number semantics at runtime is UNVERIFIED — check Data-tab behavior
  on publish before relying on it for keys/joins.
- Full entity lifecycle incl. retype + delete: see `deleting-elements`.

## Gotchas
- Names are unique per module; SS-equivalent dup check returns an error (no auto-suffix).
- Attribute `$select` visibility: use `MakeAttributesInUseVisible` semantics — bind in
  an aggregate and the attributes surface (see `building-lists`).
- Live edits are in-memory until `Ctrl+S`; `Ctrl+Z` restores a deleted entity.

## Headless fallback (ROADMAP)
Same shape as `creating-service-actions` (clone `<Entity>` + regen + verify), but
unimplemented — prefer live. See `editor-workflow` + `oml-editing-reference` if needed.
