---
name: styling-and-css-live
description: Use when the user wants to style widgets/screens in an OPEN module (live) or write the module's theme CSS so it actually shows in Service Studio's theme CSS editor. Triggers on "add style", "style class", "CSS class", "container class", "theme css", "user css", "stylesheet", "dark card ui", "dashboard styling". Proven on SS 11.55.81 via the OsLiveBridge plugin + the public CustomProperty.SetValueExpression / WebStyleSheet.SetUserCssSource APIs. Read live-editing first; deep reference in docs/ui-styling-reference.md.
---

# Styling & theme CSS — live (the open module, no reload)

How to (1) put CSS classes on widgets so they **persist and display in SS**, and
(2) write the theme stylesheet so it **appears in SS's theme CSS editor**. Proven on
SS 11.55.81 while restyling FitnessManager's Home screen as a FitFlow dark-card
dashboard. Everything lands in the live tree as an undo unit (`Ctrl+Z`), in-memory
until `Ctrl+S`.

## The two hard-won facts

1. **A container's CSS class lives in its `Style` CustomProperty** as a
   `ParsedExpression` **text literal** — NOT in `CustomStyle`. SS silently drops
   `CustomStyle` on containers after a save/reload, so the class "disappears".
2. **SS's theme CSS editor reads `_userCssSource`** (public `UserCssSource`), not
   `_cssSource`. Writing only `_cssSource` renders the page but leaves the editor
   empty.

There is also a **client-side trap** (PowerShell `ConvertTo-Json` wrapping long
strings) that masqueraded as a plugin bug — see the JSON gotcha at the bottom.

## Container Style Classes — the correct surface

`live_set_style_class(module, screen, widget, styleClass)` writes the class for any
widget. But the *storage surface depends on the widget type*:

| Widget | Class storage | Why |
|---|---|---|
| **Container** (`NRWebWidgets.Container` / `CustomWidget-OutSystems.Plugin.NRWidgets.Container`) | `Style` **CustomProperty** → `ParsedExpression` text literal (via `SetValueExpression`) | SS serializes genuine container classes as `PropertyName="Style"` → `<Text Value="class"/>`. `CustomStyle` is NOT shown for containers after reload. |
| **Link** | `CustomStyle` attribute | Correct SS convention for links. |
| **Text** | `CustomStyle` attribute | Correct SS convention for text widgets. |

So when styling containers, the class must end up in the **`Style` CustomProperty**:

```xml
<NRWebWidgets.CustomWidget-OutSystems.Plugin.NRWidgets.Container Key="..." Name="GoalCard" ...>
  <CustomProperties>
    ...
    <CustomProperty Key="..." PropertyName="Style">
      <TextResources></TextResources>
      <ValueExpression>
        <ParsedExpression>
          <Text Type="%uROOBXPvQEyU76NWO+1uxQ" Value="stats-card"></Text>
        </ParsedExpression>
      </ValueExpression>
      <Metadata></Metadata>
    </CustomProperty>
  </CustomProperties>
  ...
```

A genuine container class set this way **persists across save/reload** and shows in
SS's Properties panel. A container with `CustomStyle="..."` instead does not survive.

### The API behind it

- `ServiceStudio.Model.CustomProperty` exposes **`SetValueExpression(String)`** and a
  **`Value` (string) property** — both materialize the `_valueExpression`
  `ParsedExpression`. Both throw `No command is open. The model cannot be changed
  outside the scope of a command!` unless called inside `Command.ExecuteFromAsyncCode`
  (the bridge's `RunCmd`).
- ⚠ **CORRECTED (proven on SS 11.55.83):** `SetValueExpression(String)` **parses the
  string as an expression**:
  - a bare identifier (`header`) becomes a **Reference element** — an *invalid*
    reference if nothing in scope has that name (verify error "Unknown object");
  - a quoted string (`"header"`) becomes a Text element whose value **keeps the
    quote characters verbatim** — the SS Style Classes field then literally shows
    `"header"` (wrong class name).
  The old "try quoted first" assumption was WRONG — it produced classes named
  `"header"` with quotes included (visible in the Properties panel).
- **The correct surface for text-literal CPs (Style classes): `set_block_cp_text`**
  (bridge) / `live_set_block_cp_text` (MCP) — sets the quoted literal then rewrites
  the expression element's string field to the bare value, producing
  `[Type: Text] [Value: header]` — byte-identical to SS-created widgets
  (e.g. the Header block's PageLinks container: `Style=app-menu-links`, no quotes).
- The bridge's `set_block_cp_expression` (and the screen-side `SetStyleCp`) now try
  **raw first**, quoted only as fallback; `SetStyleClassesOn` routes **Container**
  types through the Style-CustomProperty path; **Links** keep `CustomStyle`
  (correct SS convention).

### Verification

- `live_probe_style_prop(module, screen, widget)` — read-only. Confirms the `Style`
  CustomProperty has `valueExpressionType: ServiceStudio.Expressions.ParsedExpression`
  and `_valueExpression` shows `"<class>"` (e.g. `"stats-card"`). A `null`
  `_valueExpression` means the class is on the WRONG surface (the old bug).
- In the saved `.oml`: `Value="stats-card"` appears inside a `PropertyName="Style"`
  CustomProperty, and the container element has **no** `CustomStyle="..."` attribute.

### Duplicate widget names

Widget names can repeat across duplicate block instances (e.g. `RunInfo` x8,
`StatSteps` x4 in a Home that instantiates a block several times). `set_style_class`
finds only the **first** match by name. For per-instance differences, address the
widget inside the block (or accept the first-match limitation).

## Theme CSS — write it so the editor shows it

`live_set_module_css(module, css)` (repointed to the user-CSS path) and
`live_set_user_css(module, css)` both call the bridge `set_user_css`, which invokes
the **public `WebStyleSheet.SetUserCssSource(String)`** method — the real SS API the
theme editor uses.

### Why the old approach left the editor empty

The bridge used to write only `_cssSource` / `CssSource`. That source **renders the
page** (the effective CSS) but the theme CSS editor reads **`_userCssSource`**
(public `UserCssSource`). With only `_cssSource` set, the page looks right but the
editor is blank.

### Correct call chain

```
live_set_user_css(module, css)  # or live_set_module_css
  -> bridge set_user_css
     -> RunCmd (Command.ExecuteFromAsyncCode)
        -> TrySetUserCssOnSheet(sheet, css)
           -> CallMethod(sheet, "SetUserCssSource", [css])   # public API - works
```

Other public setters discovered on `WebStyleSheet`
(`live_probe_sheet` lists them): `SetCssSource`, `SetGeneratedCssSource`,
`SetFinalCssSource`, `SetCorrectCssSource`.

### Diagnostics

- `live_read_theme_css(module)` — reports the theme stylesheet's source field
  lengths: `_cssSource` / `_userCssSource` / `_generatedCssSource` / `_finalCssSource`
  / `designTimeValue`. After a successful write, `_userCssSource` is non-null and
  matches the CSS length. (A previous partial/binary-search test may leave a half
  value — write the full CSS to fix.)
- `live_probe_sheet(module)` — dumps the sheet's concrete type, full public API
  (methods + settable properties), the `_cssSource` expression type + Clone support,
  and `ModelServices` methods. Use to choose the write surface.

### End-to-end persistence loop (what we verified)

1. `live_set_user_css(module, css)` → `live_read_theme_css` shows `_userCssSource`
   non-null.
2. Ask the user to **Ctrl+S** in SS.
3. Verify in the saved `.oml`: the theme's `UserCssSource` is non-empty (scan for the
   CSS text).
4. **Restart SS** and reopen the module.
5. Open the theme → CSS tab: the stylesheet is there. Containers still carry their
   Style Classes (the container fix persists too).

## PowerShell JSON gotcha (the "mystery" failure)

**Symptom:** small CSS payloads work; a large one (the full stylesheet, >~4 KB) fails
with a confusing:

```
InvalidOperationException: The requested operation requires an element of type 'String', but the target element has type 'Object'.
```

**Root cause:** Windows PowerShell 5.1's `ConvertTo-Json -Compress` **wraps long
strings into `{"value":"..."}` objects**. The bridge received an object where it
expected a string. It is NOT a plugin bug — the setter was fine all along. The bridge
log (`%TEMP%\OsLiveBridge.log`, `read:` line) shows `"css":{"value":".app {...` vs a
plain `"css":".app {...`.

**Fix** — serialize the long string on its own and concatenate:

```powershell
Add-Type -AssemblyName System.Web.Extensions
$ser = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$ser.MaxJsonLength = 10485760
$cssStr = $ser.Serialize($css)                 # proper JSON string literal
$json   = '{"cmd":"set_user_css","module":"FitnessManager","css":' + $cssStr + '}'
```

Do **not** use `ConvertTo-Json` on a hashtable that contains a long `css` value, and
do **not** use `JavaScriptSerializer.Serialize($hashtable)` (it throws a circular
reference on PowerShell hashtables) — serialize the string only, then concatenate.
The MCP tool (`live_set_user_css`) already serializes CSS safely with
`JsonSerializer.Serialize` internally, so this only bites when driving the raw
`bridge-cmd.ps1` pipe directly. The recommended safe client is
`scripts/Send-BridgeCmd.ps1` (see `docs/ui-styling-reference.md`).

## Worked example — FitFlow Home (FitnessManager)

All containers set via `live_set_style_class`, one call per unique name (first-match
per duplicate name):

```
App->app, Header->header, Greeting->greeting, Avatar->avatar,
GoalCard->stats-card, GoalMain->stats-main, GoalRing->stats-ring,
DailyStats->daily-stats, StatSteps/StatHeart/StatWater->stat-item,
SectionHeader->section-title, WorkoutList->workout-list,
WorkoutRun/WorkoutUpper/WorkoutYoga->workout-card,
RunInfo/UpperInfo/YogaInfo->workout-info
```

Text icons/labels use `CustomStyle` (correct SS convention): `stat-icon`, `stat-value`,
`stat-label`, `workout-icon orange/blue/pink`, `workout-time`. Then the theme CSS is
written with `live_set_user_css` (the full stylesheet, `.app` … `@media`).

Verify with `live_probe_style_prop` (ParsedExpression on every container), save,
check the `.oml`, restart, and confirm the theme CSS editor + Properties panel.

## OutSystemsUI CSS API — override variables, not rules (O11 OK)

Before writing raw CSS overrides against an OutSystems UI pattern, check whether
the component exposes a **`--osui-{component}-{prop}` variable** — OSUI styles
every overridable property through one, with a default. Setting the variable
beats fighting the rule. Verified against the compiled O11 bundle
(`outsystems-ui\classic-theme\O11.OutSystemsUI.css`): `--osui-*` variables and
the classic role variables (`--color-*`, `--space-*`, `--border-size-*`,
`--shadow-*`, `--layer-*`) all exist in O11; only the newer `--token-*` tiers
are post-token-migration (don't assume them on O11 — check the deployed
OutSystemsUI theme CSS first).

```
background-color: var(--osui-tooltip-background-color);   ← the rule you'd override
--osui-tooltip-background-color: var(--color-neutral-9);  ← override THIS instead
```

How-to:
- Find the variables for a component: `scripts\Get-OsuiVars.ps1 -Component tooltip`
  (greps the compiled bundle; pass `-CssPath` to point at your environment's
  deployed OutSystemsUI CSS).
- Per-instance: set the variable in a widget's Style Classes context or a
  container's theme CSS block. App-wide: set the Tier-3 role variable
  (`--color-*` etc.) in the theme CSS via `live_set_user_css`.
- Z-index: OSUI overlays compute from `--layer-*` + `--osui-*-layer` calc()
  chains — override the layer variable, never hard-code `z-index`.

Full hierarchy + examples: `docs/ui-styling-reference.md` → "The OutSystemsUI
CSS override hierarchy".

## Related
- `live-editing` — command-system mechanism (`Command.ExecuteFromAsyncCode`), the
  bridge, build/deploy/restart (the clean-restart procedure for plugin swaps).
- `outsystems-liveeditor` — MCP tool guide.
- `docs/ui-styling-reference.md` — deep reference: model surface, `.oml`
  serialization, diagnostics table.
