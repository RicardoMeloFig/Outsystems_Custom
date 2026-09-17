---
name: outsystems-logic
description: Use when extracting action flows, entities, structures, or site properties from a running Service Studio instance. Triggers on keywords like "list actions", "list entities", "action detail", "flow", "module report", "site properties", "structures". Provides 7 MCP tools via the outsystems-logic server.
---

# OutSystems logic extraction tools

Seven tools via the `outsystems-logic` MCP server. All attach to running
Service Studio via ClrMD (read-only, no productKey needed).

## Prerequisites

- Service Studio 11 running with the target module open and **fully loaded
  into memory**. UI focus is not required (ClrMD reads process memory).
- **Multiple `ServiceStudio.exe` instances:** pass `pid` explicitly to every
  tool (auto-detect only works when exactly one instance is running).
- **Multi-module situations:** all modules can stay open in one PID — the
  extractors attribute content per module automatically via `ownerESpace`. See
  the `multi-module-extraction` skill.

## Tool index

| Tool | Args | Returns |
|------|------|---------|
| `list_actions` | `pid?` | Server Actions + Service Actions with I/O params |
| `list_client_actions` | `pid?` | Client Actions with I/O params |
| `get_action_detail` | `name` (req), `pid?` | Single action: public, description, inputs, outputs |
| `list_entities` | `pid?` | Entities + attributes (name, type, key, mandatory) |
| `list_structures` | `pid?` | Named + Anonymous Structures with attributes |
| `list_site_properties` | `pid?` | Site Properties (name, type, read-only, description) |
| `get_module_report` | `pid?` | Full report: all actions with link-traced flows, expressions, aggregate details (Source/Joins/Filters/Sort/Group By/Calculated), SQL text (reconstructed from sqlElements), entities, structures, site properties |

## When to use

- **`list_actions`** — quick overview of server/service actions
- **`get_action_detail`** — deep-dive one action's parameters
- **`get_module_report`** — comprehensive report with flow nodes, assignments, connections, expression text
- Others — targeted lists for specific element types

## Key facts
- PID auto-detected from `ServiceStudio.exe` process if not provided
- Module just needs to be **loaded in memory** — UI focus is **not** required
  for any extractor tool. Multiple open modules are all reachable via the heap.
- Node detection uses parent-reference heap scan (robust, no C5 internals)
- Flow tracing: DFS from Start via `_links._targetNode`, handles Cycle/Condition/Otherwise/True/False
- Expression text: reads `expressionElement` tree (TextLiteral, Identifier, CompoundIdentifier, BinaryOperation, CallFunction)
- Aggregate (DataSet) details: reads `_table._rootOperation` tree — Source entities, Joins (join type + ON condition), Filters (WHERE), Sort, Group By, Calculated attributes (Count/Sum/Min/Max/Avg)
- SQL (AdvancedQuery) details: reconstructs full SQL text from `_sql.sqlElements` (TextElement, EntityElement, ParameterElement, AttributeElement), plus input Parameters and output structure
- BinaryOperator enum at offset 80 on BinaryOperation (0=+, 10=>= confirmed)
- See `docs/project_map.md` for full extraction reference

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| "Service Studio is not running" | SS not running, or module not loaded | Open the module in SS and wait until it finishes loading. |
| Wrong module name / all content under one module | Stale exe — source has `BuildEspaceMap`/`ResolveModule` but exe wasn't rebuilt | Rebuild with `dotnet publish` (see AGENTS.md troubleshooting). |
| `get_module_report` times out | Heap too large (many modules) | Isolate to fewer modules per instance. |
| Action flow looks incomplete | Module referenced, not open → only public elements | Open that module directly and extract it for its private content. |
| Same action name in two modules, one missing | `get_module_report` dedups by name within a module | Check other module sections in the output — each module has its own section. |
