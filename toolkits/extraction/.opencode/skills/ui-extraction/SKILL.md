---
name: ui-extraction
description: Use when the user wants to extract the OutSystems presentation layer - themes, CSS, screens, web blocks, widget tree, or client action flows - from a running Service Studio instance. Triggers on keywords like "theme", "css", "extract theme", "screen", "web block", "widget tree", "ui tree", "client actions", "extract ui", "grid", "theme values", "layout blocks". Provides 6 MCP tools via the outsystems-ui server (ClrMD, read-only).
---

# OutSystems UI extraction tools

Six granular, read-only tools via the `outsystems-ui` MCP server. All attach to
a running Service Studio via ClrMD (no productKey, no UI focus needed). Each
tool writes its own split file(s) under `docs/<ModuleName>/` — call one tool
to get just that category, or `extract_ui` for everything.

## Pre-flight: verify exes are fresh (MANDATORY)

Before any `extract_*` call, run `.\scripts\Ensure-Built.ps1` via bash. If it
exits 1 (any server `STALE-LOCKED`, `FAILED`, or `MISSING`), **stop** and
instruct the user to close opencode, re-run `Ensure-Built.ps1`, and restart
opencode. Stale UI exes cause two failure modes: (1) nested output paths
(`docs/<Module>/<Module>/` instead of `docs/<Module>/`), and (2) missing
per-module `ownerESpace` attribution. Never extract with a stale exe.

## Tool index

| Tool | Args | Writes |
|------|------|--------|
| `extract_themes` | `pid?`, `outputDir?` | `<ThemeName>.css` (one per theme), `theme-values.json` |
| `extract_screens` | `pid?`, `outputDir?` | `screens.json` |
| `extract_web_blocks` | `pid?`, `outputDir?` | `web-blocks.json` |
| `extract_ui_tree` | `pid?`, `outputDir?` | `ui-tree-screens.txt`, `ui-tree-blocks.txt` |
| `extract_client_actions` | `pid?`, `outputDir?` | `client-actions-screens.json`, `client-actions-blocks.json` |
| `extract_ui` | `pid?`, `outputDir?` | all of the above + `summary.json` |

## Output layout (per module)

```
docs/<ModuleName>/
├─ summary.json                  # module, pid, counts (extract_ui only)
├─ <ThemeName>.css               # one per theme: main + invisible stylesheet (theme-level CSS only)
├─ theme-values.json             # name, baseTheme, grid, layoutBlocks, themeValues
├─ css-diag.txt                  # CSS source diagnostics (which source was selected, element counts, truncation warnings)
├─ screens.json                  # screen metadata (no widget tree)
├─ web-blocks.json               # web block metadata
├─ ui-tree-screens.txt           # visual widget tree + events + CSS, screens
├─ ui-tree-blocks.txt            # visual widget tree + events + CSS, web blocks
├─ client-actions-screens.json   # link-traced client action flows
└─ client-actions-blocks.json    # link-traced client action flows
```

This is intentionally granular: to get **only web blocks**, call
`extract_web_blocks` — you do not need to extract the whole module.

## How theme CSS is recovered (important)

NewRuntime/Reactive themes store CSS as a `LightweightExpression` — a list of
`lightweightElements` (`TextElement.text` + `LightweightReferenceElement`).
The legacy `tools/UiExtractor` read the wrong field names (`value`/
`expressionElement`) and got **empty CSS**. This server walks
`lightweightElements` and reconstructs the full CSS (TextElement text +
resolved reference names + suffixes).

**CSS source selection** (tries all, picks the **longest** non-empty result):
1. `designTimeValue` (Traditional WebStyleSheet — full CSS string)
2. `_cssSource` (raw LightweightExpression — reconstructed, always complete)
3. `_userCssSource` (user-edited LightweightExpression — reconstructed)
4. `_finalCssSource` (cached fully-resolved CSS — **only trusted if `_finalCssSourceValueIsValid` is true**; a false flag means the cache is partial/incomplete and would cause mid-statement truncation)
5. `_generatedCssSource` (pre-computed generated CSS — no validity flag, last resort)

The "pick longest" strategy ensures the most complete CSS wins. The validity
guard on `_finalCssSource` prevents a longer-but-partial cache from shadowing
the complete `_cssSource`.

**ClrMD 3.0 ReadString 4096-char truncation** (important): ClrMD 3.0's
`ClrField.ReadString` truncates strings at exactly 4096 chars (a
`ReadProcessMemory` partial-read issue). This silently cuts CSS mid-statement
(e.g. `color: var(--color-n`). The server works around this with
`ReadStrManual`: when `ReadStr` gets a result ≥ 4096 chars, it re-reads the
.NET string object directly from process memory via `DataReader.Read`,
bypassing ClrMD's `ReadString`. String layout: `[method-table-ptr (8 bytes
on 64-bit)] [_stringLength (4 bytes int32)] [UTF-16 chars...]`. Offsets are
`IntPtr.Size` for length and `IntPtr.Size + 4` for first char.

**`ReadListAll`** (not `ReadList`): the `lightweightElements` list's `_size`
field can be stale (smaller than the actual element count), causing truncated
CSS reconstruction. `ReadListAll` reads ALL elements in the backing array
(`arr.Length`), not just `_size`. Also tries `_data`, `_buffer`, `_elements`
field names for non-standard collections.

**Reference resolution** (`ResolveLightweightRef`): tries direct name fields
(`_name`, `name`, `refName`, `_refName`) on the reference element, then
follows `reference`/`_reference`/`referedObject`/`_referedObject`/
`referencedObject`/`_target`/`target`/`value`/`_value` chains. If the
referenced object's name is empty, performs a **generic object-name walk**
(iterates all object-reference fields). Falls back to `"?"` placeholder —
**never returns null**, preventing mid-statement CSS truncation (e.g.,
`color:var(-` was a prior bug when a reference couldn't be resolved).

**`<ThemeName>.css` contains ONLY theme-level CSS** (from `th._styleSheet` and
`th._invisibleStyleSheet`). Standalone `WebStyleSheet` CSS (stylesheets
defined inside web blocks or screens) is **NOT** included in `<ThemeName>.css` —
it is attributed to its parent block/screen and shown inline in the UI tree
(`ui-tree-*.txt`). `designTimeValue` is the fallback for Traditional sheets.

## theme-values.json shape

```json
{
  "themes": [{
    "name": "MobileTheme", "baseTheme": "OutSystemsUI", "public": false,
    "grid": { "useGrid": 0, "gridColumns": 12, "columnWidth": 60,
              "gutterWidth": 20, "gridWidth": 940, "gutterPercentage": 30,
              "minWidth": null, "maxWidth": null },
    "layoutBlocks": { "normalPageLayout": "Layout", "headerWebBlock": null,
                      "menuWebBlock": null, "footerWebBlock": null },
    "themeValues": [ /* generic dump; empty if theme inherits all from base */ ]
  }]
}
```

`themeValues` is a generic recursive dump (any model object → JSON, boilerplate
fields skipped). A theme that inherits everything from its base theme (e.g.
KOPA's MobileTheme ← OutSystemsUI) has an **empty** `themeValues` array — that
is correct, not a bug. The grid/layout/baseTheme fields are always captured.

## UI tree format (enriched)

Each screen or web block entry in `ui-tree-screens.txt` / `ui-tree-blocks.txt`
includes:

1. **Header** — `=== SCREEN: <name> ===` or `=== WEB BLOCK: <name> ===`
2. **Events** — lifecycle events (OnInitialize, OnReady, OnRender, OnDestroy,
   OnParametersChanged) and custom events, with handler action names:
   `Events: OnReady=InitData, OnClick=SaveData`
3. **CSS** — the block's/screen's own stylesheet CSS (from `WebStyleSheet`
   objects whose parent is this block/screen), shown inline:
   ```
   CSS:
   .custom-floating-actions { padding: 50px; ... }
   ```
4. **Widget tree** — visual tree with:
   - Widget name and type: `SaveButton (Button)`
   - CSS classes: `.btn-primary .custom-class`
   - Inline styles: `[style: text-align: right;]`
   - **Widget events**: `[OnClick=SaveData, OnChange=Validate]`

Example:
```
============================================================
WEB BLOCK: Custom_Floating_Actions
============================================================
Events: OnReady=Init
CSS:
.custom-floating-actions { padding: 50px; width: 100%; }
├─ Container .custom-float-action-background
├─ Button (Button) .btn-primary [OnClick=ClosePopup]
│  └─ Container
│     └─ Text
└─ Container .custom-floating-actions
```

Widget event handler names (e.g., `SaveData`) are cross-reference keys to the
full client action flows in `client-actions-screens.json` /
`client-actions-blocks.json` (inputs, outputs, link-traced flow steps).

## Pattern identification reference (OutSystems UI)

Extracted blocks, widgets, CSS classes and CSS variables usually come from the
**OutSystems UI framework** (the component library every Reactive/Mobile O11
theme builds on). To go from raw extraction output to *identified, explained*
UI, consult the committed reference catalog:

- `references/outsystems-ui/patterns.md` — every pattern with its CSS class
  root, full class list, CSS custom properties (the pattern's CSS API), public
  JS API, and provider library (Splide, Flatpickr, VirtualSelect, noUiSlider)
- `references/outsystems-ui/classic-theme-o11.css` — the verbatim O11 classic
  theme CSS (the base stylesheet themes extend). Use it to separate
  **framework/base CSS** from **app-specific overrides** when reading an
  extracted `<ThemeName>.css`: a class present in both is inherited base
  styling; present only in the extracted theme = app customization.

How to apply it:
- A web block named e.g. `Accordion` / `AccordionItem` → identify via the
  catalog (`.osui-accordion`, JS API `AccordionAPI`, CSS API vars
  `--osui-accordion-*`); document behavior from the pattern's known semantics
  instead of guessing.
- CSS classes `osui-*`, `splide*`, `flatpickr*`, `vscomp*` in a widget tree or
  theme CSS → framework classes; look them up in the catalog before describing
  them as app-specific.
- If `references/outsystems-ui/patterns.md` does not exist, run
  `scripts/Build-UiReference.ps1` (needs the ground-of-truth repo path in
  `references/source-path.txt`); if that is also unavailable, skip annotation
  and note it — never invent pattern semantics.

## Critical: keep the DataTarget alive

The ClrMD `DataTarget` **must** stay alive while the captured `ClrObject`s are
read. The server attaches in `Dispatch` and keeps the `using var target` in
scope across the whole extraction. If you ever refactor this into per-tool
methods, do NOT dispose the target before the file writes finish — field reads
return garbage/empty from a freed heap. (This was a real bug during
development: themes read as unnamed with grid=0.)

## Multi-module

The `outsystems-ui` extractors use **`ownerESpace` back-references** to
attribute every screen, block, theme, widget, event, client action, and flow
node to its owning module. `BuildEspaceMap()` finds all `ServiceStudio.Model.ESpace`
objects on the heap and maps their address → module name. `ResolveModule()`
reads each object's `ownerESpace` field to look up the owning module.

**Result:** when multiple modules are open in ONE `ServiceStudio.exe` process,
the extractors **automatically separate content per module** — output files
are written to `docs/<ModuleName>/` per module. No isolation needed.

If output shows all content under one module name, the exe is **stale** —
rebuild with `dotnet publish mcp\outsystems-ui\OutSystemsMcpUi.csproj -c Release
-o mcp\outsystems-ui\publish`. See the `multi-module-extraction` skill and
`AGENTS.md` "Multi-module targeting" for details.

## Key facts
- Service Studio 11 on .NET 8; module must be **open and loaded** (focus not needed).
- `pid` auto-detected only if exactly one `ServiceStudio.exe` is running;
  otherwise it errors and asks for `pid` (safer than the older auto-pick).
- `outputDir` defaults to `docs` (relative to the workspace root).
- See `docs/project_map.md` for the full extraction reference.
