---
name: deleting-elements
description: Use when the user wants to delete an entity, attribute, action, aggregate, screen, flow, or widget. LIVE-first (proven): one live_delete_* per element type, each a single undo unit. Headless .oml approach is fallback/roadmap.
---

# Deleting Elements — LIVE (proven SS 11.55.83)

Every delete is its own SS command — `Ctrl+Z` in SS restores it. Verify with a
before/after count or a follow-up list call.

| Element | Tool | Proven |
|---|---|---|
| Server entity | `live_delete_entity(module, name)` → `{entitiesBefore, entitiesAfter}` | ✅ ScratchTmp/ScratchDelete_Me (2→1) |
| Entity attribute | `live_delete_entity_attribute(module, entity, attrName)` | ✅ TmpAttr removed |
| Service action | `live_delete_service_action` / generic `live_delete_action` (service/server/client) | ✅ (existing runbook) |
| Action (any, by name) | `live_delete_action` (searches ServiceActions/UserActions/ServerActions/ClientActions) | ✅ |
| Flow node (any type) | `live_delete_node_by_index(module, action, nodeIndex)` (relinks prev→target) | ✅ |
| Assign node (by match) | `live_delete_node(module, action, matchVar, matchValue)` | ✅ |
| Single assignment | `live_remove_assignment(module, action, nodeIndex, var)` | ✅ |
| Input/output param | `live_remove_input_param` / `live_remove_output_param` (+`_from_block`) | ✅ |
| Aggregate / data action | `live_delete_aggregate` / `live_delete_data_action` (block OR screen) | ✅ |
| Screen / flow | `live_delete_screen_from_flow` / `live_delete_web_flow` | ✅ |
| Widget (screen/block) | `live_delete_widget` / `live_delete_widget_from_block` / `live_delete_from_placeholder` / `live_delete_layout` | ✅ |

## Order of demolition (full cleanup)
1. Widgets referencing data → handlers → aggregates/data actions → actions →
   attributes → entity → screens → flow. Deleting an entity first orphans
   aggregates/expressions (red errors until SS re-verify; harmless on scratch).
2. After each delete: `live_list_flow` / `live_list_widgets` / collection count.

## Headless fallback (ROADMAP)
Unimplemented — prefer live. Shape: remove element + companion fragment
(`NodesNotShownInESpaceTree#<key>` for actions), fix `Count`, regen, verify
`IsValidOml`. See `oml-editing-reference`.
