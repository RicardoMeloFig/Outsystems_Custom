---
name: multi-module-extraction
description: Use when the user wants to extract from MORE THAN ONE OutSystems module (e.g. "kopa and kopa_admin", "both modules", "all modules", "two modules", "multi-module", "extract multiple modules"). Explains how per-module attribution works via ownerESpace and when to use isolation vs merged extraction. Read this BEFORE writing any extraction code.
---

# Multi-module extraction: per-module attribution works natively

When the user asks to extract from two or more modules, **follow this flow**.

## The key fact (read first)

All three ClrMD extractors (`outsystems-logic`, `outsystems-ui`,
`outsystems-tools`) use **`ownerESpace` back-references** to attribute every
model object (entity, action, structure, site property, screen, block, widget,
flow node) to its owning module. This is done via `BuildEspaceMap()` which
finds all `ServiceStudio.Model.ESpace` objects on the heap and maps their
address → module name, then `ResolveModule()` reads each object's
`ownerESpace` field to look up the owning module.

**Result:** when multiple modules are open in ONE `ServiceStudio.exe` process,
the extractors **automatically separate content per module** — no isolation
needed. The output includes `MODULE: <name>` headers grouping actions,
entities, structures, site properties, screens, blocks, etc. under their
correct owning module.

**Important:** the extractors must be **rebuilt** after source changes. If
output shows all content under a single module name (usually the first
`OmlHeader` found), the exe is stale — rebuild with
`dotnet publish mcp\outsystems-logic\OutSystemsMcpLogic.csproj -c Release -o
mcp\outsystems-logic\publish` (and similarly for the other servers).

## Step 1 — Enumerate with `get_open_module`

Call `get_open_module` (no args). It reads the AutoSave cache and lists every
open module **with its PID**:
```
Service Studio running: PID(s) 7320, 9012
PID 7320: ModuleName=KOPA ...
PID 9012: ModuleName=KOPA_AdminTool ...
```
Note the PID. **Do not skip this step.**

> Note: `get_open_module` reads the AutoSave `.bak` files. If a module was just
> opened and never saved, it shows "no AutoSave file" even though the module is
> loaded in memory. In that case, the module is still extractable — just
> identify its PID another way (Task Manager / `Get-Process ServiceStudio`) and
> confirm by running an extractor (the module name appears in the output).

## Step 2 — Run Path A (always): per-module metadata via `parse_oml_header`

For every `.oml` file on disk that you care about, call `parse_oml_header` with
its `path`. This reads the file header directly (offline, no Service Studio,
no heap) and returns: module name, eSpaceKey, IsExtension, SS/platform
versions, description, saved time, source path.

**Why always:** this confirms the authoritative per-module identity and
disambiguates which PID holds which module. Do it even if you plan to extract
from a merged heap — it confirms you targeted the right modules.

## Step 3 — Extract per-module content (merged heap, no isolation needed)

**Condition:** any number of modules open in one or more `ServiceStudio.exe`
instances. The extractors attribute content per module automatically via
`ownerESpace`.

Call the extractors with `pid`:
- Logic/data: `list_entities`, `list_structures`, `list_actions`,
  `list_client_actions`, `list_site_properties`, `get_module_report`
  (`outsystems-logic`, `pid` arg) — output includes `MODULE: <name>` sections
- UI: `extract_themes`, `extract_screens`, `extract_web_blocks`,
  `extract_ui_tree`, `extract_client_actions`, `extract_ui`
  (`outsystems-ui`, `pid` arg) — writes files under `docs/<ModuleName>/`
- Live tree: `live_reader` (`outsystems-tools`, `pid` arg) — output includes
  per-module grouping

**No isolation needed.** All modules can stay open as tabs in one instance.
The extractors will separate their content automatically.

### When isolation (Path B) might still be needed

- **Referenced but not open modules:** a module that is referenced but not
  open exposes only its **public** elements. For that module's complete
  **private** content, open it directly and extract.
- **Stale exe:** if the extractor output shows all content under one module
  name, the exe wasn't rebuilt after the latest source changes. Rebuild.
- **Very large module sets:** if the heap walk times out with many modules
  open, isolate to fewer modules per instance.

### Step 3b — Validate module coverage (MANDATORY)

After extraction, verify that ALL modules from `get_open_module` (Step 1)
appear in the extraction output with content. For each module:

1. **Check `get_module_report` output:** does the module appear with
   `Server Actions > 0` or `Entities > 0`? If yes → OK.
2. **If module shows as `MODULE: (unknown)` with 0 elements:** this is a known
   issue — `get_module_report`'s slower heap walk (it reads flow nodes, links,
   SQL text) can miss objects. The module IS loaded and its data IS in memory.
   FALLBACK: run `list_actions(pid)` — it will find the actions (faster heap
   walk). Then run `get_action_detail(name, pid)` for each action to get
   parameters. Use `list_entities(pid)` for entities, `list_structures(pid)`
   for structures, etc.
3. **If module is missing entirely from `get_module_report`:** same FALLBACK.
4. **Cross-check:** compare `list_actions` module list against
   `get_module_report` module list. If `list_actions` finds a module that
   `get_module_report` missed, use the `list_*` data as authoritative for
   that module.

**Do NOT skip a module** just because `get_module_report` didn't find it.
The `list_*` tools are faster and more reliable for discovery. This is not a
stale-exe issue — the exes can be FRESH and this can still happen.

## Step 4 — Write per-module documentation

For per-module technical reference docs (entities, structures, actions with
full flow traces, site properties, UI elements), write one file per module
under `docs/<ModuleName>/TECHNICAL_DOCUMENTATION.md`. Use the tree-structure
format (Module Overview → Interface → Logic → Theme → Data & Variables), NOT
the 4-section app-level template.

For an app-level synthesized document (Architecture Canvas, module interactions,
critical cross-module workflows), write a single
`docs/TECHNICAL_DOCUMENTATION.md` at the `docs/` root. See the
`documentation-generation` skill.

## Building / rebuilding extractors

If extraction output doesn't have per-module `MODULE:` headers, the exe is
stale. Rebuild:

```powershell
# Build all 4 MCP servers
.\scripts\Build-All.ps1

# Or build just one
dotnet publish mcp\outsystems-logic\OutSystemsMcpLogic.csproj -c Release -o mcp\outsystems-logic\publish
dotnet publish mcp\outsystems-ui\OutSystemsMcpUi.csproj -c Release -o mcp\outsystems-ui\publish
dotnet publish mcp\outsystems-tools\OutSystemsMcpTools.csproj -c Release -o mcp\outsystems-tools\publish
```

If the exe is locked by a running opencode MCP server, stop the process first:
```powershell
Get-Process OutSystemsMcpLogic | Stop-Process -Force
# Then copy the rebuilt files to publish\
```

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Output labeled with only one module name (all content merged) | Stale exe — source has `BuildEspaceMap`/`ResolveModule` but exe wasn't rebuilt | Rebuild the exe (see above) |
| Entity/action counts are far too high for one module | Stale exe — all modules' content attributed to first `OmlHeader` | Rebuild the exe |
| A referenced module's private actions are missing | It is referenced but not open | Open that module directly and extract it |
| `get_open_module` shows "no AutoSave file" | Module just opened, never saved | Find PID via `Get-Process ServiceStudio`; pass `pid` to extractors. |
| Extractor times out | Heap too large (many modules in one PID) | Isolate: fewer modules per instance |
| `dotnet publish` says file locked | opencode is running and holding the exes open | Stop the MCP process, re-publish, restart opencode |
| Module shows as `(unknown)` with 0 elements in `get_module_report` but `list_actions` finds its actions | `get_module_report`'s heap walk is slower and may miss objects. Exes can be FRESH — this is not a stale-exe issue. | Use `list_actions` + `get_action_detail` as fallback. See Step 3b. Do NOT skip the module. |

## Quick reference
- `get_open_module` → enumerate PIDs (Step 1)
- `parse_oml_header` per `.oml` → per-module metadata, always (Step 2 / Path A)
- All modules open in one PID → extractors attribute per module automatically via `ownerESpace` (Step 3)
- Per-module docs → `docs/<ModuleName>/TECHNICAL_DOCUMENTATION.md` (tree format)
- App-level doc → `docs/TECHNICAL_DOCUMENTATION.md` (4-section synthesis)
- If output is merged → rebuild the exe (stale build)
- see `AGENTS.md` "Multi-module targeting" and `docs/project_map.md`
