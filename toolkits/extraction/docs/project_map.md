# Project Map — OutSystems Application

> Source of truth. Refer here FIRST before reading deeper files.
> Last updated: 2026-07-24

## Architecture
- Platform: OutSystems 11 (Service Studio .NET 8)
- Extraction: ClrMD heap walk (bypasses productKey) — **read-only baseline**
- MCP servers: outsystems-tools (3 tools), outsystems-logic (7 tools), outsystems-ui (6 tools)
- **Editing** (live in-process + headless `.oml`) lives in the separate
  `AI Outsystems Automation Editor` baseline — not here.

## Module loading & focus

Extractors need the target module **open and finished loading** in
`ServiceStudio.exe`. UI focus is **not** required; multiple open modules are
all reachable via the heap. (Open the module manually in SS — opening is not
automated in this baseline.)

**Multi-module**: when several modules or SS instances are open, call
`get_open_module` to enumerate, then pass `pid` explicitly to target one. See
`AGENTS.md` for the full decision flow.

## Modules
<!-- Add modules as you work with them. Run get_module_report or ModuleInfoExtractor to generate reports. -->

| Module | Type | Status | Entities | Actions | Structures | Site Props | Report |
|--------|------|--------|----------|---------|------------|------------|--------|
| KOPA | Extension (Mobile App) | Active | (merged) | (merged) | (merged) | (merged) | docs/KOPA/ |
| KOPAAdmin_Tool | Extension (Admin UI) | Active | — | — | — | — | docs/KOPAAdmin_Tool/ |
| KOPA_BL | Extension (Business Logic) | Active | 53 (merged) | 276 server + 132 service (merged) | 72 (merged) | — | docs/KOPA_BL/ |
| KOPA_API | Extension (REST API) | Active | — | (merged) | (merged) | — | docs/KOPA_API/ |
| KOPA_AWS_IS | Extension (AWS Integration) | Active | — | (merged) | — | 22 AWS props | docs/KOPA_AWS_IS/ |
| KOPA_AdminTool_BL | Extension (Admin BL) | Active | — | (merged) | — | — | docs/KOPA_AdminTool_BL/ |
| KOPA_IS | Extension (Iron Mountain) | Active | — | (merged) | — | 10 IM props | docs/KOPA_IS/ |
| KOPA_Common | Extension (Common) | Active | — | — | (merged) | — | docs/KOPA_Common/ |
| KOPA_CS | Extension (Common Services) | Active | — | (merged) | — | — | docs/KOPA_CS/ |

> **Documentation method:** Two modes (see `documentation-generation` skill):
> - **Per-module (default):** `docs/<ModuleName>/TECHNICAL_DOCUMENTATION.md` per module + UI extraction files (`extract_ui` with `outputDir`) for UI modules. Triggered by "extract documentation for all open modules".
> - **App-level (on request):** single `docs/TECHNICAL_DOCUMENTATION.md` with Architecture Canvas + module interactions. Triggered by "application architecture document", "architecture canvas".
> Content from the merged heap is attributed per module using naming conventions + functional role (see attribution rules in the skill).

## MCP Tool Index

| Tool | Server | Purpose | Key Args |
|------|--------|---------|----------|
| get_open_module | outsystems-tools | Detect open SS module (AutoSave) | none |
| parse_oml_header | outsystems-tools | Offline .oml header parse | `path` |
| live_reader | outsystems-tools | Entity/Structure tree from memory | `pid?` |
| list_actions | outsystems-logic | Server + Service Actions with I/O | `pid?` |
| list_client_actions | outsystems-logic | Client Actions | `pid?` |
| get_action_detail | outsystems-logic | Action inputs/outputs/public | `name`, `pid?` |
| list_entities | outsystems-logic | Entities + attributes | `pid?` |
| list_structures | outsystems-logic | Named + Anonymous Structures | `pid?` |
| list_site_properties | outsystems-logic | Site Properties | `pid?` |
| get_module_report | outsystems-logic | Full report with link-traced flows | `pid?` |
| extract_themes | outsystems-ui | Themes: CSS + theme-values.json (grid, layout, themeValues) | `pid?`, `outputDir?` |
| extract_screens | outsystems-ui | Screen metadata → screens.json | `pid?`, `outputDir?` |
| extract_web_blocks | outsystems-ui | Web block metadata → web-blocks.json | `pid?`, `outputDir?` |
| extract_ui_tree | outsystems-ui | Widget-tree maps → ui-tree-screens.txt, ui-tree-blocks.txt | `pid?`, `outputDir?` |
| extract_client_actions | outsystems-ui | Link-traced client action flows (split per screen/block) | `pid?`, `outputDir?` |
| extract_ui | outsystems-ui | Umbrella: all UI files + summary.json | `pid?`, `outputDir?` |

> Editing tools (`snapshot`, `add_entity_attribute`, etc.) used to live here in
> `outsystems-editor` (FlaUI). They have been **removed** — editing now lives in
> the `AI Outsystems Automation Editor` baseline (live `live_*` tools + headless
> `.oml`). This baseline is extraction-only.

## Extraction Reference

### Node Detection
- Method: parent-ref heap scan (not C5 HashSet internals)
- Node types: `ServiceStudio.Model.Nodes+{Start,End,Assign,ExecuteAction,ForEach,Switch,If,Comment,ErrorHandler,DataSet,AdvancedQuery,RecordListToExcel,...}`
- Match: node.parent.address == action.address

### Flow Tracing
- Algorithm: DFS from Start node via `_links._targetNode`
- Link types: Sequence (main flow), Cycle (loop body), Condition (Switch branch), Otherwise (default), True/False (If), Comment (annotation)
- Error handlers: traced separately (not connected from Start)
- Output: numbered nodes with indentation for branches, `(loops back to [type])` for cycles

### Aggregate (DataSet) Detail Extraction
Aggregates are `DataSet` nodes whose `_table` (DataTable) field holds the full query definition.
The DataTable's `_rootOperation` (CombineSources or GroupBy) contains sources, joins, filters, sorts, and group-by.

| Element | Model Path | Fields |
|---------|-----------|--------|
| Source entity | `_table._masterSource._source` (ReferenceEntity) | `_name` → entity name |
| Joined sources | `_table._rootOperation._sources` (DataSource collection) | `_name` → source alias |
| Joins | `_table._rootOperation._joins` (JoinCondition collection) | `_leftSource._name`, `_rightSource._name`, `_joinType` (0=Left,1=Right,2=Full,3=Inner), `_condition` (ParsedExpression) |
| Filters (WHERE) | `_table._rootOperation._filters` (Filter collection) | `_condition` (ParsedExpression → resolved via ReadElementText) |
| Sorts (ORDER BY) | `_table._rootOperation._sorts` (AttributeSort collection) | `_sort` (0=Asc,1=Desc), `_originalAttribute` (ParsedExpression) |
| Calculated attrs | `_table._rootOperation._calculatedAttributes` (CalculatedAttribute collection) | `_name`, `_expression`, `_type` |
| Group By | `_table._rootOperation` (if GroupBy) or `_table._tableOperations` | `_groupByAttributes` (GroupByAttribute), `_aggregateAttributes` (AggregateAttribute with `_aggregationType`: 1=Count,2=Sum,3=Min,4=Max,5=Avg) |

### SQL Advanced Query Detail Extraction
SQL queries are `AdvancedQuery` nodes with three key fields.

| Element | Model Path | Fields |
|---------|-----------|--------|
| SQL text | `_sql.sqlElements` (List\<SQLExpressionElement\>) | Reconstructed by iterating elements in order |
| Input parameters | `_queryParameters` (sequence of AdvancedQueryParameter) | `_name`, `_type`, `_expandInline` |
| Output structure | `_outputStructure` (sequence of AdvancedQueryRecord) | `_name`, `_record` (Entity reference) |

SQL text reconstruction element types:
| Element Type | Key Field | Reconstructed As |
|-------------|-----------|-----------------|
| TextElement | `text` (String) | Raw SQL text fragment |
| UnboundElement | `text` (String) | Raw text (unbound reference) |
| EntityElement | `reference` → `referedObject._name` | `{EntityName}` |
| ParameterElement | `reference` → `referedObject._name` | `@ParameterName` |
| AttributeElement | `reference` → `referedObject._name` | `{AttributeName}` |

### Expression Types
| Type | Key Fields | Example |
|------|-----------|---------|
| TextLiteral | `value` (String) | `"Hello"` |
| IntegerLiteral | `value` (String) | `42` |
| Identifier | `reference` → `refName` or `referedObject._name` | `Out1` |
| CompoundIdentifier | `reference` + `rest` chain (each has own `reference`) | `Var1.Current.Attribute1` |
| BinaryOperation | `operator` (enum), `leftSide`, `rightSide` | `Int + 1` |
| CallFunction | `reference` → `refName`, `arguments` array | `If(Int >= 1, "yes", "no")` |

### BinaryOperator Enum (Int32)
| Value | Operator | Confirmed? |
|-------|----------|------------|
| 0 | + (Add) | Yes |
| 1 | - (Subtract) | No |
| 2 | * (Multiply) | No |
| 3 | / (Divide) | No |
| 4 | mod | No |
| 5 | = (Equal) | No |
| 6 | <> (NotEqual) | No |
| 7 | < (LessThan) | No |
| 8 | <= (LessThanOrEqual) | No |
| 9 | > (GreaterThan) | No |
| 10 | >= (GreaterThanOrEqual) | Yes |
| 11 | and | No |
| 12 | or | No |

### Reference Resolution
| Reference Type | Key Field | Resolves To |
|---------------|-----------|-------------|
| ExpressionElementReferenceByName | `refName` (String) | Direct name (e.g., "If", "Current", "List") |
| ObjectElementReference | `referedObject` → `_name` | Model element name (variable, entity, etc.) |

## Workspace Structure
```
.opencode/skills/     → 7 skills, auto-discovered (documentation-generation, module-extraction, multi-module-extraction, outsystems-logic, outsystems-tools, ui-extraction, user-guide-generation)
mcp/                  → 3 MCP servers (outsystems-tools, outsystems-logic, outsystems-ui) — extraction-only
docs/                 → this file (+ generated <Module>/ output, gitignored)
schemas/              → metadata.schema.json, client-actions.schema.json (generic UI output schemas)
tools/                → standalone CLI tools (ModuleInfoExtractor, LiveReader, OmlExtractor, probe, ApiProbe, UiProbe, UiExtractor)
scripts/              → PowerShell/Bash scripts (extract-ui.ps1 UI wrapper, ...)
open/                 → scratch folder for .oml files (gitignored except .gitkeep)
.env.example          → template for OUTSYSTEMS_MODULE (copy to .env, gitignored)
docs/<ModuleName>/    → generated UI extraction output (gitignored)
```

## UI / Presentation-Layer Extraction

The `UiExtractor` tool (`tools/UiExtractor/`) captures the full presentation layer
of ANY OutSystems module. It is 100% generic: the target module name is never
hardcoded - it is resolved at runtime from a CLI flag, env var, or `.env` file,
and matched against the module open in Service Studio.

### Module resolution (priority order)
1. `--module=<Name>` CLI flag
2. `OUTSYSTEMS_MODULE` environment variable
3. `OUTSYSTEMS_MODULE` key in a local `.env` file (see `.env.example`)

### Usage
```
dotnet run --project tools/UiExtractor -c Release -- --module=<ModuleName>
.\scripts\extract-ui.ps1 -Module <ModuleName>
npm run extract-ui -- --module=<ModuleName>
```

### Output (docs/<ModuleName>/)
```
docs/<ModuleName>/
├── <ThemeName>.css      one raw CSS file per theme (main + invisible stylesheet)
├── ui-tree.txt          visual widget-tree map of every screen & web block
├── metadata.json        screens/blocks, parameters, variables, events (no widget tree)
└── client-actions.json  client action flows, JS nodes, step sequences
```

| Output file | Schema |
|-------------|--------|
| `metadata.json` | `schemas/metadata.schema.json` |
| `client-actions.json` | `schemas/client-actions.schema.json` |
| `<ThemeName>.css` | (raw CSS - no schema, one file per theme) |
| `ui-tree.txt` | (plain text tree - no schema) |

### Extraction method
ClrMD read-only heap walk against a running Service Studio instance (same
productKey-bypassing approach as the logic extractors). No module-specific
hardcoding; `--module` selects which open SS instance to extract.

### Model coverage
| Section | Model types (Reactive / NewRuntime) | Captured |
|---------|--------------------------------------|----------|
| Themes & CSS | `NewRuntime.Theme` | name, description, baseTheme, public, grid (`_useGrid`, `_gridColumns`, `_columnWidth`, `_gutterWidth`, `_gridWidth`, `_gutterPercentage`, `_minWidth`, `_maxWidth`), layoutBlocks (`_normalPageLayout`, `_headerWebBlock`, `_menuWebBlock`, `_footerWebBlock`), themeValues (`_themeValues` collection, generic dump), raw CSS |
| Style sheets | `WebStyleSheet` | css (designTimeValue, or LightweightExpression reconstruction), kind |
| Screens | `NRNodes+WebScreen` | metadata, URL, permissions, I/O params, local vars, events, client actions, widget tree |
| Web Blocks | `NRNodes+WebBlock` | same as screens + custom events |
| Widget tree | `NRWebWidgets+*` (concrete) | type, name, htmlClass (`_customStyle`), inline style, type-specific props, **widget events (OnClick etc.) with handler names**, nested children |
| Events | `NREvents+*` (lifecycle), `NRWebWidgetEvents+EventHandler`, `WebBlockCustomEvent` | system / widget / custom, with handler action name — shown in UI tree per widget AND per block/screen |
| Client actions | `NRFlows+ClientScreenActionFlow`, `DataScreenActionFlow` | inputs, outputs, local vars, link-traced flows, JS nodes |

### Key implementation notes
- **Widget tree**: built from `parent`/`owner` back-references; `WebBlockInstance`
  resolves `_sourceWebBlock`; `PlaceholderArgument` resolves `_placeholder` name;
  `Container` = `CustomPlaceholderWidget`; `Text` content from `_value`.
- **Client actions are grouped by `parent` back-reference** (not the lazy
  `_clientActions` collection, whose backing array is often null at read-only
  attach time).
- **Flow tracing**: DFS from `Start` via `_links._targetNode` (Sequence/Cycle/
  Condition/Otherwise/True/False), same algorithm as the logic extractors. Covers
  both `Nodes+*` (Traditional) and `NRNodes+*` (Reactive, incl. JS nodes) flow nodes.
- **Lifecycle event handlers**: read via the event slot's `_destination` field.
- **Theme CSS (NewRuntime)**: a theme's `_styleSheet._cssSource` is a
  `LightweightExpression` holding a `lightweightElements` list of `TextElement`
  (`text` field = literal CSS) and `LightweightReferenceElement` (`reference`
  → resolved object name + `suffix`). The `outsystems-ui` server reconstructs
  CSS by concatenating these. CSS source selection: tries ALL sources
  (`designTimeValue`, `_finalCssSource`, `_generatedCssSource`, `_cssSource`,
  `_userCssSource`) and picks the **longest** non-empty result — this avoids
  the truncation bug where a partial `_finalCssSource` cache (non-empty but
  incomplete) would shadow the full `_cssSource`. One `<ThemeName>.css` file
  is written per theme (main + invisible stylesheet with separator).
  Reference resolution tries direct name fields, then `reference`/
  `referedObject`/`target`/`value` chains, then a generic object-name walk;
  falls back to `"?"` placeholder (never null — prevents mid-statement
  truncation). `<ThemeName>.css` contains **ONLY theme-level CSS** — standalone
  `WebStyleSheet` CSS is attributed to its parent block/screen and shown
  inline in the UI tree. The legacy `tools/UiExtractor` read the wrong
  field names (`value`/`expressionElement`) and got **empty CSS** — now fixed
  in both the `outsystems-ui` MCP server and `tools/UiExtractor`.
- **UI tree enrichment**: each block/screen entry in `ui-tree-*.txt` now
  includes lifecycle events (OnInitialize, OnReady, etc.) with handler names,
  custom events, the block's/screen's own stylesheet CSS (inline), and
  per-widget events (OnClick, OnChange, etc.) with handler action names.
  Handler names are cross-reference keys to full flows in
  `client-actions-*.json`.
- **DataTarget lifetime**: the `outsystems-ui` server keeps its ClrMD
  `DataTarget` alive across the whole extraction (attach in `Dispatch`, dispose
  after all file writes). Disposing before reads finish makes every field read
  return garbage/empty.
- The discovery probe `tools/UiProbe/` dumps a census of all `ServiceStudio.Model.*`
  types (counts + field samples) - use it when targeting new element kinds.
  `tools/ThemeProbe/` deep-dumps a live `NewRuntime.Theme` (grid, layout, CSS
  expressions, themeValues) — the schema reference used to build `extract_themes`.

### Multi-module (shared-heap) behavior
Every ClrMD extractor (all three servers + `tools/UiExtractor`, `LiveReader`,
`ModuleInfoExtractor`) does a flat `heap.EnumerateObjects()` walk with **no
per-object module attribution**. When two or more modules are open in ONE
`ServiceStudio.exe` process, they are merged into one bucket and the module
name is the first `OmlHeader` found. This is **not fatal** — handle it with the
3-path model (do NOT rewrite extractors):
- **Path A** — `parse_oml_header` per `.oml` file → deterministic per-module
  metadata (offline). Always do this first.
- **Path B** — isolate to one module per PID, pass `pid` → clean per-module
  content (deterministic; preferred for UI extraction).
- **Path C** — extract the merged heap, then synthesize per-module docs by
  attributing content via the dependency graph + naming, keyed by Path A
  metadata. Referenced-but-not-open modules expose only public elements.
See the `multi-module-extraction` skill for the full decision flow and
`AGENTS.md` "Multi-module targeting" for the canonical model.
