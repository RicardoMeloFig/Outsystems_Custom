---
name: adding-attributes
description: Use when the user wants to add an attribute to an entity or structure. LIVE-first (proven): live_add_entity_attribute / live_add_structure_attribute. Headless .oml approach is fallback/roadmap.
---

# Adding Attributes — LIVE (proven SS 11.55.83)

## Entity attributes

```
live_add_entity_attribute(module, entity, attrName, type, isMandatory?, defaultValue?)
live_set_entity_attribute_type(module, entity, attrName, type)   # retype existing
```

- Type resolution: `es.<type>Type` (Text, Integer, LongInteger, Decimal, Boolean,
  DateTime, Date, Time, ...), then `es.ListTypes` by Name, then Structures, then
  Entities (reference attrs). Unknown type = clean error, no side effects.
- `isMandatory`: `"true"`/`"false"` — settable ✅. `defaultValue`: best-effort only;
  entity attrs report `(not set)` — no surface exists headlessly; set in SS.
- Proven: `ScratchOrder.Title(Text,mandatory)`, `Quantity`, `DueDate`, `IsDone`;
  retype Integer→LongInteger→Integer.

## Structure attributes

```
live_add_structure_attribute(module, structure, attrName, type)
live_set_structure_attribute_type(module, structure, attrName, type)
```

- Same resolution order; proven on `UserStats`/`ActivityTag`
  (`RecentActivityTags` = `ActivityTag List`).

## Verify
- `live_debug_eSpace_collection_items(module, "Entities")` — entity `IsValid: True`.
- `live_get_verify_errors(module, "screen"|"block", name)` — bindings that use the
  new attribute show no new errors.

## Headless fallback (ROADMAP)
Unimplemented — prefer live. Shape documented in `oml-editing-reference`
(`%`-prefixed basic-type keys, `EntityIdentifierType:/…` paths).
