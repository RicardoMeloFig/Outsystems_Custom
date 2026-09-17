---
name: documentation-generation
description: Use when the user asks to "generate documentation", "extract documentation for all open modules", "document all modules", "per-module documentation", "create technical documentation", "overall document", "solution architecture document", "document the application", or wants a synthesized written report covering an OutSystems application. Triggers on "documentation", "tech doc", "architecture document", "solution architect", "document the module", "document all modules", "write up the app", "4-layer canvas", "module interactions". Produces per-module TECHNICAL_DOCUMENTATION.md files (one per module folder) by default, and an app-level overview document only when explicitly requested.
---

# OutSystems technical documentation generation

This skill generates technical documentation for OutSystems applications. It
operates in **two modes**:

- **Mode 1 — Per-module (DEFAULT):** one `TECHNICAL_DOCUMENTATION.md` per module
  folder (`docs/<ModuleName>/TECHNICAL_DOCUMENTATION.md`), plus UI extraction
  files (screens.json, <ThemeName>.css, client-actions.json, ui-tree.txt) for UI
  modules. Triggered by "extract documentation for all open modules", "document
  all modules", "generate documentation", "per-module documentation".
- **Mode 2 — App-level (ONLY on explicit request):** a single overview document
  at `docs/TECHNICAL_DOCUMENTATION.md` covering the Architecture Canvas, module
  interactions, and architecture. Triggered by "application architecture
  document", "4-layer canvas", "module interactions", "overall document",
  "solution architecture".

**When in doubt, use Mode 1 (per-module).** Mode 2 is only generated when the
user explicitly asks for an app-level / architecture / Architecture Canvas document.

## When to use

The user wants a written-up document of the app or modules. Trigger examples:
- "extract documentation for all open modules" → **Mode 1** (per-module)
- "document all modules" → **Mode 1** (per-module)
- "generate documentation" → **Mode 1** (per-module)
- "application architecture document" → **Mode 2** (app-level)
- "4-layer canvas" → **Mode 2** (app-level)
- "module interactions" → **Mode 2** (app-level)
- "overall document" → **Mode 2** (app-level)

If the user only asks for raw extraction (entities list, action list, theme
CSS), use the specific extractor skill instead — this skill is for **synthesis**.

---

## CRITICAL FAILURE MODES (read before writing any doc)

These are the most common ways documentation generation goes wrong. Read them
BEFORE you start writing. Violating any of them produces broken docs.

### FAILURE MODE #1 — Summarizing flow traces

DO NOT summarize, condense, rephrase, or reformat flow traces. Copy them
CHARACTER BY CHARACTER from the extraction output. The extraction output has
indented blocks like:

```
 2. [AdvancedQuery] DELETE_Endorsers
      Parameters: UUID (Text), BatchAPIExtensionId (EntityIdentifierType)
      Output: BULK_PayloadDocuments -> BULK_PayloadDocuments
      SQL:
        DELETE {Endorsers}
        from {TempDocs}
        inner join {Documents}
        on {DocumentId} = {DocumentId}
        where {PayloadUUID} = @UUID
```

Writing `2. [AdvancedQuery] DELETE_Endorsers` WITHOUT the Parameters/Output/SQL
sub-lines is **WRONG**. The sub-lines ARE the query definition. If you omit
them, the documentation is useless. Write MORE lines, not fewer.

### FAILURE MODE #2 — Delegating to task agents

DO NOT use the Task tool to write `TECHNICAL_DOCUMENTATION.md`. Task agents
have their own context window and naturally summarize/condense content to fit.
They WILL drop SQL text, aggregate details, and flow sub-lines. Write the file
YOURSELF using the Write tool, reading the extraction output directly.

### FAILURE MODE #3 — Creating intermediate files

DO NOT create files like `full-module-report.txt`, `module-report.txt`, or any
other intermediate extraction file. Write extraction content directly into
`TECHNICAL_DOCUMENTATION.md`. The only files in `docs/<ModuleName>/` should be
`TECHNICAL_DOCUMENTATION.md` and UI extraction files (screens.json, etc.). If
the MCP tool output is truncated, read it in chunks via the Read tool with
offset/limit — do NOT save to an intermediate file.

### FAILURE MODE #4 — Missing SQL/aggregates in the final doc

After writing each `TECHNICAL_DOCUMENTATION.md`, you MUST verify:
1. Grep the file for `SQL:` — if the extraction had `AdvancedQuery` nodes, this
   must match. If 0 matches but extraction had SQL, the doc is BROKEN.
2. Grep the file for `Source:` — if the extraction had `DataSet` nodes, this
   must match. If 0 matches but extraction had aggregates, the doc is BROKEN.
3. Check line count — a doc with 30+ actions and full flow traces should be
   3000+ lines. If it's 500 lines, you summarized. Rewrite it.

### FAILURE MODE #5 — Skipping modules that show as "(unknown)"

`get_module_report` may show a module as `MODULE: (unknown)` with 0 elements
even though the module IS open and loaded. This happens because
`get_module_report`'s heap walk is slower (it reads nodes, links, SQL text)
and may miss objects. The `list_*` tools (list_actions, list_entities, etc.)
are faster and more reliable for discovery. If a module from `get_open_module`
is missing from `get_module_report`, do NOT skip it — use `list_actions` +
`get_action_detail` as a fallback (see Step 2b).

---

## Mode 1 — Per-module documentation (DEFAULT)

### Output structure

```
docs/
├── <ModuleName>/
│   ├── TECHNICAL_DOCUMENTATION.md   ← technical reference doc for this module
│   ├── screens.json                 ← (UI modules only) from extract_screens
│   ├── <ThemeName>.css              ← (UI modules only) from extract_themes (one per theme)
│   ├── theme-values.json            ← (UI modules only) from extract_themes
│   ├── ui-tree-screens.txt          ← (UI modules only) from extract_ui_tree
│   ├── ui-tree-blocks.txt           ← (UI modules only) from extract_ui_tree
│   ├── client-actions-screens.json  ← (UI modules only) from extract_client_actions
│   ├── client-actions-blocks.json   ← (UI modules only) from extract_client_actions
│   └── web-blocks.json              ← (UI modules only) from extract_web_blocks
├── <ModuleName>/
│   └── TECHNICAL_DOCUMENTATION.md   ← logic-only modules have just the .md
└── ...
```

Each module gets its own folder. UI modules (Reactive Web / Mobile apps with
screens) get extraction files alongside the doc. Logic-only modules (extensions
with no screens) get only the `TECHNICAL_DOCUMENTATION.md`.

### Workflow (do these in order)

#### Step 0 — Pre-flight: verify exes are fresh (MANDATORY)
Before any extraction, run the staleness gate via bash:

```powershell
.\scripts\Ensure-Built.ps1
```

Interpret the exit code:
- **Exit 0** (all `FRESH` or `REBUILT`): proceed to Step 1.
- **Exit 1** (any `STALE-LOCKED`, `FAILED`, or `MISSING`): **STOP. Do NOT proceed
  with extraction.** Tell the user exactly:

  ```
  Exes are stale and locked by opencode. I cannot rebuild them from here.
  Steps:
    1. Close opencode
    2. Open a terminal in the repo root and run: .\scripts\Ensure-Built.ps1
    3. Restart opencode
    4. Ask me to continue
  I have stopped all extraction work. No documentation will be written until
  the exes are fresh.
  ```

**Why this matters:** stale exes (source newer than exe) silently produce
single-module output — no `MODULE:` sections, no per-module attribution. That
makes correct documentation **impossible**. The classic failure mode is the
extractor labeling all content under one module name (usually the first
`OmlHeader` found), which then tempts the AI to "attribute" content by naming
conventions. **Never do this.** Naming conventions are fundamentally unreliable
(e.g. 51 of 53 entities lived in `*_CS`, not `*_BL`; `RequestQueue` lived in
`*_AWS_IS`, not `*_BL`). Always fix the exes first.

#### Step 1 — Enumerate open modules
1. Call `get_open_module` (no args). Note every open module name + PID.
2. For each `.oml` file on disk, call `parse_oml_header` (`path` arg). This
   returns authoritative per-module metadata (name, eSpaceKey, IsExtension,
   versions, saved time, source path) — offline, deterministic.

   **WARNING — `IsExtension` is NOT a reliable module-type indicator.** In
   OutSystems 11 .oml files, `IsExtension` is `True` for **ALL** modules —
   including Reactive Web apps, Mobile apps, and Theme modules that have
   screens, web blocks, and client actions. Do NOT use `IsExtension` to decide
   whether a module has UI. The only reliable way to know is to run `extract_ui`
   (Step 3) and check whether `screens.json` / `web-blocks.json` were produced.

#### Step 2 — Extract logic/data (all modules in one PID)
Call these extractors ONCE on the shared PID (all modules open in one instance).
The extractors **automatically attribute content per module** via `ownerESpace`
— output includes `MODULE: <name>` sections. No isolation or AI attribution
needed.

| Tool | Returns |
|------|---------|
| `get_module_report` | Full report: all actions with link-traced flows, entities, structures, site properties — grouped by module |
| `list_entities` | Entities + attributes per module |
| `list_structures` | Named + Anonymous Structures with attributes per module |
| `list_site_properties` | Site Properties per module |
| `list_actions` | Server Actions + Service Actions per module |
| `list_client_actions` | Client Actions per module |

**Important:** if output shows all content under one module name (no `MODULE:`
headers), the exe is **stale** — the `ownerESpace` attribution code is in the
source but not compiled into the running exe. Stop immediately and run
`.\scripts\Ensure-Built.ps1` (see Step 0). Do NOT attempt to attribute content
by naming conventions — it is unreliable and will produce wrong documentation.

**Large output handling:** `get_module_report` can be very large (500KB+ for
9 modules with flow traces). If the MCP tool output is truncated, the full
output is saved to a file by the tool runner — use the Read tool with
offset/limit to read it in chunks (e.g. 500 lines at a time). Do NOT create
your own intermediate files. Write content directly into
`TECHNICAL_DOCUMENTATION.md` as you read each chunk.

#### Step 2b — Validate module coverage (MANDATORY)

After `get_module_report`, compare the modules it found against the modules
from `get_open_module` (Step 1). For each module in `get_open_module`:

- **If it appears in `get_module_report` with actions > 0:** OK, proceed.
- **If it appears as `MODULE: (unknown)` with 0 elements:** FALLBACK. The
  module's actions exist in memory but `get_module_report`'s slower heap walk
  missed them. Run `list_actions(pid)` — it will find the actions (it's a
  faster, simpler heap walk). Then for each action, run
  `get_action_detail(name, pid)` to get its parameters (inputs/outputs/public
  flag). Flow traces will be unavailable for this module — note this
  explicitly in the doc ("Flow traces not captured — get_module_report heap
  walk did not find this module's actions. Action list from list_actions.").
- **If it's missing entirely from `get_module_report`:** Same FALLBACK as above.

Also cross-check: if `list_entities` found entities for a module but
`get_module_report` shows 0 entities for that module, use the `list_entities`
data as the authoritative source.

**Why this happens:** `get_module_report` reads far more per object (flow
nodes, links, SQL text, aggregate details) than the `list_*` tools. The longer
heap walk can miss objects due to GC movement or ClrMD enumeration limits. The
`list_*` tools are faster and more reliable for discovery. This is a known
issue, not a stale-exe problem.

#### Step 3 — Extract UI data (automatically per module)
Call `extract_ui` with `outputDir`. The UI extractor **automatically attributes
screens/blocks/themes to each module** via `ownerESpace` and writes files to
`docs/<ModuleName>/` per module:

```
extract_ui(pid=<PID>, outputDir="docs")
```

This generates per module: `screens.json`, `<ThemeName>.css` (one per theme), `theme-values.json`,
`ui-tree-screens.txt`, `ui-tree-blocks.txt`, `client-actions-screens.json`,
`client-actions-blocks.json`, `web-blocks.json`, `summary.json`.

**ALWAYS run `extract_ui` for every module — do NOT skip based on `IsExtension`.**
The `IsExtension` field from `parse_oml_header` is `True` for ALL modules
(including Mobile apps and Theme modules with screens/blocks), so it cannot be
used to identify logic-only modules. The UI extractor **automatically skips**
modules with no screens/blocks/themes (it filters on
`Screens.Count > 0 || Blocks.Count > 0 || Themes.Count > 0`), so running it on
a logic-only module is harmless — it simply produces no output for that module.

**Why this matters:** `get_module_report` and `list_client_actions` only capture
*global* client actions. Screen-embedded and web-block-embedded client actions
(e.g. `LayoutReady`, `SetMenuListeners`, `ToggleSideMenu`, `AddFavicon`) are
ONLY captured by `extract_ui` → `client-actions-screens.json` /
`client-actions-blocks.json`. Skipping `extract_ui` silently drops these.

#### Step 4 — Write per-module technical reference docs
For EACH open module, create `docs/<ModuleName>/TECHNICAL_DOCUMENTATION.md`
using the **tree-structure template** below. The content comes directly from
the extraction output — no AI attribution needed (the tools did it).

### Per-module template (tree structure)

```markdown
# Technical Reference: <ModuleName>

> Generated <date> from live Service Studio extraction (PID <pid>).
> Per-module attribution via ownerESpace field (ClrMD). All modules open in one instance.

## Module Overview
- **Name:** <ModuleName>
- **Type:** <Reactive Web / Mobile App / Extension>
- **Description:** <from parse_oml_header>

## Interface (UI & Screen Elements)
(Skip this section only if `screens.json` and `web-blocks.json` are absent or empty after extraction. Do NOT skip based on `IsExtension` — it is unreliable.)

### Screens & Blocks
- Screens: <count> (see screens.json)
- Web Blocks: <count> (see web-blocks.json)

### Widget Tree
(see ui-tree-screens.txt and ui-tree-blocks.txt)

### Screen/Block Client Actions
(see client-actions-screens.json and client-actions-blocks.json)

### Screen/Block CSS
(see <ThemeName>.css)

## Logic (Actions & Integrations)

### Server Actions (with full flow traces)
<For each server action: name, public/private, description, inputs, outputs, full flow trace>
<Include EVERY node: Start, ExecuteAction, If/True/False, Assign, JSONSerialize, ErrorHandler, End, DataSet, AdvancedQuery>
<CRITICAL — DataSet (Aggregate) nodes: include ALL indented sub-lines from the extraction output: Source, Joins (with join type + ON condition), Filters (WHERE conditions), Sort, Group By, Calculated attributes>
<CRITICAL — AdvancedQuery (SQL) nodes: include ALL indented sub-lines: Parameters, Output, and the full reconstructed SQL text>
<These sub-lines appear indented under the node in get_module_report output — copy them VERBATIM, do not omit>

### Service Actions (with full flow traces)
<Same format as Server Actions>

### Global Client Actions
<If any: name, inputs, outputs, flow trace>

## Module Theme (CSS)
(see <ThemeName>.css and theme-values.json)

## Data & Variables

### Entities & Attributes
<For each entity: name, public/private, all attributes with type, key, mandatory flags>

### Static Entities
<List with their values>

### Site Properties
<Name, type, read-only flag, description>

### Structures
<For each structure: name, public/private, all attributes with type>

### Client Variables
<If any>

### Session Variables
<If any>
```

**Rules:**
- Use REAL names from the extraction — never invent names
- Include ALL actions with their FULL flow traces (every node, every step)
- Copy flow traces VERBATIM from extraction output — character by character. Do NOT rephrase, condense, reformat, or summarize. If the extraction has 10 indented lines under a `[DataSet]` or `[AdvancedQuery]` node, write all 10 lines.
- PRESERVE aggregate and SQL detail lines verbatim: DataSet nodes have Source/Filters/Joins/Sort/Group By/Calculated sub-lines; AdvancedQuery nodes have SQL/Parameters/Output sub-lines. These contain the actual query definitions and SQL text — omitting them loses the most important logic details.
- DO NOT use the Task tool to write documentation — task agents summarize and drop SQL/aggregate details. Write the file YOURSELF using the Write tool.
- DO NOT create intermediate files (full-module-report.txt, etc.). Write directly to TECHNICAL_DOCUMENTATION.md.
- Include ALL entities with ALL attributes
- Include ALL structures with ALL attributes
- Include ALL site properties
- Do NOT truncate or summarize — write everything to the file
- If a section has no data, write "(none found in extraction)"
- Skip the Interface section only if no screens/blocks were extracted (check `screens.json` / `web-blocks.json`). Do NOT use `IsExtension` to decide — it is `True` for all modules.

### Example: DataSet and AdvancedQuery flow trace format

When writing flow traces, preserve the indented detail lines exactly as they appear in `get_module_report` output:

```
 2. [DataSet] GetBatchesToPack
      Source: Batch
      Filters:
        - Index(SelectedBatches, Batch.FullBarcode) <> +?
        - Batch.InUse

 2. [AdvancedQuery] DELETE_Endorsers
      Parameters: UUID (Text), BatchAPIExtensionId (EntityIdentifierType)
      Output: BULK_PayloadDocuments -> BULK_PayloadDocuments
      SQL:
        DELETE {Endorsers}
        from {TempDocs}
        inner join {Documents}
        on {DocumentId} = {DocumentId}
        where {PayloadUUID} = @UUID
```

Do NOT reduce these to just `[DataSet] GetBatchesToPack` or `[AdvancedQuery] DELETE_Endorsers` — the sub-lines ARE the query definition.

#### Step 5 — Verify documentation (MANDATORY)

After writing EACH `TECHNICAL_DOCUMENTATION.md`, run these checks via bash:

```powershell
# Check 1: SQL present? (if extraction had AdvancedQuery nodes)
Select-String -Path "docs\<ModuleName>\TECHNICAL_DOCUMENTATION.md" -Pattern "SQL:" | Measure-Object | Select-Object -ExpandProperty Count

# Check 2: Aggregate Source present? (if extraction had DataSet nodes)
Select-String -Path "docs\<ModuleName>\TECHNICAL_DOCUMENTATION.md" -Pattern "Source:" | Measure-Object | Select-Object -ExpandProperty Count

# Check 3: Line count (30+ actions with flows should be 3000+ lines)
(Get-Content "docs\<ModuleName>\TECHNICAL_DOCUMENTATION.md").Count

# Check 4: No intermediate files left behind
Get-ChildItem "docs\<ModuleName>\" -Filter "*.txt" | Select-Object Name
```

Interpret results:
- **SQL: count = 0** but extraction had `AdvancedQuery` nodes → **BROKEN**. Rewrite the doc, copying SQL text verbatim from the extraction.
- **Source: count = 0** but extraction had `DataSet` nodes → **BROKEN**. Rewrite the doc, copying aggregate details verbatim.
- **Line count < 1000** for a module with 20+ actions → **LIKELY SUMMARIZED**. Rewrite with full flow traces.
- **Any .txt files found** → **Delete them**. Only `TECHNICAL_DOCUMENTATION.md` and UI extraction files should exist.

If any check fails, do NOT tell the user "done" — rewrite the file first.

---

## Mode 2 — App-level documentation (ONLY on explicit request)

### Workflow

1. **Complete Mode 1 first** — generate per-module docs for all modules.
2. **Synthesize** a single `docs/TECHNICAL_DOCUMENTATION.md` from the per-module
   docs + `parse_oml_header` metadata, using the **mandatory 10-section
   template** below.

### App-level template (MANDATORY 10-section structure)

**HARD RULES (non-negotiable):**
- Use EXACTLY these 10 section headings, in EXACTLY this order.
- Do NOT add, remove, rename, merge, split, or reorder sections.
- Do NOT change heading levels — every numbered section is a `## H2`.
- If a section has no data for this app, KEEP the heading and write
  `(none found in extraction)` beneath it. Never drop a section.
- Only the content (facts, names, counts) varies per app — the skeleton is
  identical for every app-level document.

```markdown
# Technical Documentation: <Application Name>

> Generated <date> from live Service Studio extraction (PID <pid>).
> Synthesized from <N> per-module technical reference documents.

## 1. Application Overview
- **Purpose:** <2-3 sentence summary of what the app does>
- **Key Integrations:** <bulleted list of external systems consumed>
  - <System> (<module>) — <one-line role>

## 2. Module Architecture (Architecture Canvas)
### Module Table
| Module | Type | Layer | Role |
|--------|------|-------|------|
### Architecture Canvas
<ASCII diagram: End-User / Core / Foundation layers, each listing its modules; integrations and utilities belong to the Foundation sub-layer>
### Key Architectural Patterns
<3-5 bullets: tenant routing, audit queue, timer-driven upload, offline sync, SSO, etc.>

## 3. Module Interaction Map
<ASCII call-dependency diagram showing which module calls which. One tree per top-level entry point (UI app, REST API).>

## 4. Data Model & Storage
### Core Entities
| Entity | Description | Key Attributes |
|--------|-------------|----------------|
<group rows by category with sub-headers (Core Transactional / Order & Exception / Reference-Static / Audit-Support) if the app has enough entities; otherwise a single table>
### Static Entities
<comma-separated list>
### Site Properties & Timers
<per-module bullets of site properties; note timers if known>

## 5. Core Logic & Integration
### Server & Service Actions Summary
| Module | Server Actions | Service Actions | Key Flows |
|--------|---------------|-----------------|----------|
### Integrations (REST/SOAP)
<per-integration: endpoints, auth, tenant routing>
### Role-Based Security
<roles inferred from screen permissions; SSO/tenant notes>

## 6. Critical Workflows & Processes
<2-4 key business workflows as numbered step-by-step flows (ASCII). Each step names the module + action.>

## 7. Cross-Cutting Concerns
<bullets: audit logging, multi-tenancy, PDF generation, retry mechanisms, etc.>

## 8. Module Summary Table
| Module | Server Actions | Service Actions | Entities | Structures | Site Props |
|--------|---------------|-----------------|----------|------------|------------|

## 9. Extraction Notes & Gaps
<bulleted list of what was NOT auto-extracted: timers, BPT/Light BPT, consumed/exposed REST-SOAP endpoint URLs+auth, full role list, mobile screens. State each gap explicitly.>

## 10. Per-Module Documentation Index
| Module | Full Reference Document |
|--------|------------------------|
| <name> | `docs/<name>/TECHNICAL_DOCUMENTATION.md` |
```

### Verification (MANDATORY after writing the app-level doc)

```powershell
# Must return exactly 10 H2 headings, in this exact order:
#  1. Application Overview
#  2. Module Architecture (Architecture Canvas)
#  3. Module Interaction Map
#  4. Data Model & Storage
#  5. Core Logic & Integration
#  6. Critical Workflows & Processes
#  7. Cross-Cutting Concerns
#  8. Module Summary Table
#  9. Extraction Notes & Gaps
# 10. Per-Module Documentation Index
Select-String -Path "docs\TECHNICAL_DOCUMENTATION.md" -Pattern '^## ' | Select-Object -ExpandProperty Line
```

- Count MUST be exactly 10.
- Order and names MUST match the list above verbatim.
- If not, rewrite the doc — do not hand it to the user broken.

### Why this fixes divergence

The old template was a 4-section *suggestion*; one session followed it, another
invented 9 sections. The new template is a **fixed 10-section skeleton with
hard rules and a mechanical H2-count verification step**. Two different apps now
produce documents with an identical skeleton — only the facts (entity names,
action counts, workflow steps) differ.

---

## Honesty about extraction gaps

The ClrMD extractors do NOT currently capture:
- **Timers / Scheduled Processes** — not extracted. State "No timers extracted"
  if the user asks.
- **BPT / Light BPT process flows** — not extracted. Mark Section 4 as "BPT
  data not auto-extracted — verify in Service Studio > Processes tab".
- **Consumed/Exposed REST & SOAP** — partially inferable from Structures and
  Service Actions, but endpoints/auth are not extracted. Flag for manual
  completion.
- **Roles** — screen **permissions** ARE captured by `extract_screens`
  (`screens.json` includes per-screen roles). The full Role list itself is not
  separately enumerated; infer from permissions.

Do **not** fabricate names, flows, or endpoints. If something wasn't extracted,
say so explicitly.

## Official docs corpus (optional terminology grounding)

When documenting platform concepts (entities, aggregates, themes, roles,
lifecycles), verify terminology against OutSystems' **official documentation
source** if the ground-of-truth repo is configured locally:

1. Read `references/source-path.txt` (one line: absolute path to the repo).
   If missing, skip this section silently — do not ask the user.
2. If present, the corpus lives at:
   - `<path>\docs-product\src` — product docs (`building-apps/`, `ref/`,
     `security/`, `manage-platform-app-lifecycle/`, ...)
   - `<path>\docs-howtos\src` — how-to guides
3. Use it to: confirm the official name of a platform concept before using it
   in a doc; quote/link the doc path (e.g. `docs-product/src/building-apps/...`)
   when a concept needs authoritative explanation.

Scope guard: prefer **O11-relevant** pages. `docs-odc\` and pages under
`migration-to-odc` / `extending-with-odc` describe OutSystems Developer Cloud,
not OS11 — do not cite them in OS11 documentation. Grep the corpus for terms;
do not read entire folders.

## Reference
- Extraction internals: `docs/project_map.md`
- Multi-module rules: `multi-module-extraction` skill + `AGENTS.md`
- Tool reference: `outsystems-logic`, `outsystems-ui`, `outsystems-tools` skills
- UI pattern identification: `references/outsystems-ui/patterns.md` (see `ui-extraction` skill)
- Ground-of-truth setup: `references/README.md`
