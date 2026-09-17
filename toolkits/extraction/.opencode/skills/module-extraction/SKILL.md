---
name: module-extraction
description: Use when the user wants a full module report with flow tracing, expression text extraction, or asks about the get_module_report tool. Triggers on "full report", "module info", "extract module", "flow trace", "assignments", "expression text".
---

# Module extraction with flow tracing

## Prerequisites

- **Fresh exes (MANDATORY pre-flight):** before calling `get_module_report`,
  run `.\scripts\Ensure-Built.ps1` via bash. If it exits 1 (any server
  `STALE-LOCKED`, `FAILED`, or `MISSING`), **stop** and instruct the user to
  close opencode, re-run `Ensure-Built.ps1`, and restart opencode. Stale exes
  silently produce single-module output (no `MODULE:` sections) and break
  per-module attribution. Never proceed with a stale exe; never fall back to
  naming conventions.
- Service Studio 11 running with the target module open and **fully loaded
  into memory**. UI focus is not required (ClrMD reads process memory).
- **Multiple `ServiceStudio.exe` instances:** call `get_open_module` first and
  pass `pid` explicitly to `get_module_report`.
- **Multi-module situations:** all modules can stay open in one PID — the
  extractors attribute content per module automatically via `ownerESpace`. See
  the `multi-module-extraction` skill.

## MCP tool

**Tool**: `get_module_report` (in `outsystems-logic` server)
**Args**: `pid` (optional, auto-detected)
**Returns**: Full text report directly in chat

**Prerequisite**: target module open and **finished loading** in Service
Studio. UI focus is **not** required (ClrMD reads process memory). If
multiple `ServiceStudio.exe` instances are running, call `get_open_module`
first and pass `pid` explicitly. See `AGENTS.md` for full guidance.

## What the report contains

1. **Summary** — counts of all element types
2. **Server/Service/Client Actions** — with:
   - Public flag, description, inputs, outputs
   - Flow nodes traced by logical connections (not coordinates)
   - Branch handling: Cycle (loop body), Condition, Otherwise, True, False
   - Assign details: `variable = value` with expression text
   - ExecuteAction: called action name
   - Comment: text content
   - Error handlers: separate section
   - **DataSet (Aggregate) nodes**: Source entity, Joins (with join type + ON condition), Filters (WHERE conditions), Sort order, Group By attributes, Calculated attributes (Count/Sum/Min/Max/Avg)
   - **AdvancedQuery (SQL) nodes**: full reconstructed SQL text (from `sqlElements` tree — TextElement, EntityElement, ParameterElement, AttributeElement), input Parameters (with type + expand-inline flag), output structure
3. **Entities** — attributes with type, key, mandatory flags
4. **Structures** — named + anonymous, with attributes
5. **Site Properties** — type, read-only, description

## Expression text extraction

| Expression Type | How it's read | Example output |
|----------------|---------------|----------------|
| TextLiteral | `value` field | `"Hello"` |
| IntegerLiteral | `value` field | `42` |
| Identifier | `reference` -> `refName` or `referedObject._name` | `Out1` |
| CompoundIdentifier | `reference` + `rest` chain (recursive) | `Var1.Current.Attribute1` |
| BinaryOperation | `operator` enum + `leftSide`/`rightSide` | `Int + 1` |
| CallFunction | `reference` -> `refName` + `arguments` array | `If(Int >= 1, "yes", "no")` |

## BinaryOperator enum mapping

Confirmed: `0 = +`, `10 = >=`. Others best-guess (see `docs/project_map.md`).

## Reference
- Full extraction internals: `docs/project_map.md`

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| "Service Studio is not running" | SS not running, or module not loaded | Open the module in SS and wait until it finishes loading. |
| Report labeled with the wrong module / counts too high | Stale exe — source has `BuildEspaceMap`/`ResolveModule` but exe wasn't rebuilt | Rebuild with `dotnet publish` (see AGENTS.md troubleshooting). |
| `get_module_report` times out | Heap too large (many modules) | Isolate to fewer modules per instance. |
| Flow trace looks incomplete | Module referenced, not open → only public elements | Open that module directly and extract it. |
| Same action name in two modules, one missing | Dedup by name within a module | Check other module sections in the output — each module has its own section. |
| Expression text shows `?` or blanks | Expression type not in the captured set | Note it as unextracted rather than guessing; see the Expression Types table. |
| Module shows as `(unknown)` with 0 elements in `get_module_report` but `list_actions` finds its actions | `get_module_report`'s heap walk is slower (reads nodes, links, SQL text, aggregate details) and may miss objects due to GC movement or ClrMD enumeration limits. `list_actions` is a faster, simpler heap walk and is more reliable for action discovery. | Use `list_actions(pid)` to get action names, then `get_action_detail(name, pid)` for each action's parameters. Flow traces will be unavailable — note this in the doc. Do NOT skip the module. See `documentation-generation` skill Step 2b. |
