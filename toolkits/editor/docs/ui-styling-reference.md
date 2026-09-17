# UI Styling & Theme CSS — Live Editing Reference

Deep reference for styling an **open** OutSystems 11 module (SS 11.55.81) via the
OsLiveBridge plugin: widget **Style Classes** (the `Style` CustomProperty surface)
and **theme CSS** (the `UserCssSource` surface). Proven end-to-end while restyling
FitnessManager's Home as a FitFlow dark-card dashboard.

Companion skill: `.opencode/skills/styling-and-css-live/SKILL.md` (triggered runbook).
Mechanism (command system, bridge): the `live-editing` skill.

## The two model surfaces

### 1. Widget Style Classes — `Style` CustomProperty

A **Container** widget stores its CSS class in the `Style` **CustomProperty** as a
`ParsedExpression` **text literal** — NOT in the `CustomStyle` attribute. SS
serializes genuine container classes like this in the `.oml` `Widgets#<key>`
fragment:

```xml
<NRWebWidgets.CustomWidget-OutSystems.Plugin.NRWidgets.Container
    Key="bBUhs2fPvUWoJKfojzhv9Q" Name="App" CustomObjectImplKey="6cjGhm4AI_VtWsHoA4dyAw" Width="(fill parent)">
  <CustomProperties Count="8">
    ...
    <CustomProperty Key="sJ72VRhvRUKPv+_GoQjdjw" PropertyName="Style">
      <TextResources></TextResources>
      <ValueExpression>
        <ParsedExpression>
          <Text Type="%uROOBXPvQEyU76NWO+1uxQ" Value="app"></Text>
        </ParsedExpression>
      </ValueExpression>
      <Metadata></Metadata>
    </CustomProperty>
  </CustomProperties>
```

- The class value is the `<Text ... Value="..."/>` inside the `Style` CP's
  `ValueExpression` → `ParsedExpression`.
- A container written with `CustomStyle="app"` instead serializes that attribute on
  the element, has a **null** `_valueExpression` on the Style CP, and **SS drops the
  class on save/reload** — the bug we fixed.
- **Links** and **Text** widgets correctly use the `CustomStyle` attribute (their
  conventional surface) — do not route them through the Style CP.

#### The API (`ServiceStudio.Model.CustomProperty`)

| Member | Notes |
|---|---|
| `SetValueExpression(String)` | Public. Materializes `_valueExpression` as a `ParsedExpression` (proven: Style CP became `ParsedExpression [Type: Text] [Value: app]`). **Throws `No command is open...` outside a command scope.** |
| `Value` (string property) | Public. Fallback assignment surface. |
| `ValueExpression.Text` | Last-resort expression-text fallback. |

All writes must run inside `Command.ExecuteFromAsyncCode` (the bridge `RunCmd`).
⚠ **CORRECTED (proven on SS 11.55.83):** `SetValueExpression(String)` **parses** its input as an
expression — a bare identifier becomes a Reference element (invalid if not in scope), and a
quoted string becomes a Text element that **keeps the quote characters verbatim** (a Style class
set as `"header"` literally renders class `"header"`). The old "text literals are double-quoted"
assumption was WRONG. Use the bridge's **`set_block_cp_text`** (MCP `live_set_block_cp_text`)
for text-literal CPs: it sets the quoted literal, then rewrites the element's string field to the
bare value — `[Type: Text] [Value: header]`, byte-identical to SS-created widgets.

Bridge routing: `set_style_class` → `SetStyleClassesOn` — Container types
(`FullName` contains `"Container"`) go through `TrySetStyleCustomProperty`
(now **raw first**, quoted fallback → `Value` → `ValueExpression.Text`), then stray
`CustomStyle` is cleared; Links keep `CustomStyle`.

**Duplicate names:** widget names repeat across duplicate block instances
(first-match only in `set_style_class`).

### 2. Theme CSS — `WebStyleSheet` sources

`ServiceStudio.Model.WebStyleSheet` (theme's `StyleSheet`) has four CSS source
fields, plus a design-time value:

| Field / property | Editor shows it? | Notes |
|---|---|---|
| `_userCssSource` (`UserCssSource`) | **YES — the theme CSS editor reads this** | This is the one to write. |
| `_cssSource` (`CssSource`) | NO | Effective/render CSS. Writing only this renders the page but the editor stays empty. |
| `_generatedCssSource` (`GeneratedCssSource`) | NO | SS-generated effective CSS. |
| `_finalCssSource` (`FinalCssSource`) | NO | Final combined. |
| `designTimeValue` | — | Raw design-time string. |

#### Public write API (proven via `live_probe_sheet`)

`WebStyleSheet` exposes public instance methods — use these, NOT reflection into
private fields:

```
SetUserCssSource(String)      <-- what SS's theme editor uses (proven works)
SetCssSource(String)
SetGeneratedCssSource(String)
SetFinalCssSource(String)
SetCorrectCssSource(String)
```

Public settable properties that accept a string (last resort): `EffectiveValue`,
`UserEffectiveValue`, `ComesFromElsewhere`, `CreatedBy`, `LastModifiedBy`,
`LastModifiedDate`, `LastMergedBy`, `LastMergedDate`, `IsToolbarObject`.

The `_cssSource` expression type is
`ServiceStudio.LightweightExpressions.LightweightExpression` (NOT `ICloneable`).
The prior "create a fresh expression and assign to `_userCssSource`" reflection
approach corrupts state and makes the command commit throw — always use
`SetUserCssSource`.

## Bridge commands (raw pipe)

All are wired in `BridgeHost.cs` `Dispatch` and callable via
`bridge-cmd.ps1 -Json '{...}' -SSPid <pid>` (or the MCP tools below):

| Command | Args | Purpose |
|---|---|---|
| `set_style_class` | module, screen, widget, styleClass | Set a widget's class (container→Style CP; link/text→CustomStyle). |
| `set_style_cp` | module, screen, widget, styleClass | Test/command-scoped Style-CP setter (diagnostic). |
| `probe_style_prop` | module, screen, widget | Dump the widget's CustomProperties, esp. `Style` CP `_valueExpression`. |
| `probe_block_widget` | module, block, widget | Same, for a block's widget (safe). |
| `set_user_css` | module, css | `WebStyleSheet.SetUserCssSource` inside a command (theme editor-visible). |
| `set_module_css` | module, css | Legacy path — writes `_cssSource` only (renders, editor empty). Keep for reference; use `set_user_css`. |
| `read_theme_css` | module | Report `_cssSource`/`_userCssSource`/`_generatedCssSource`/`_finalCssSource`/`designTimeValue` lengths. |
| `probe_sheet` | module | Sheet concrete type + full public API + `_cssSource` expr type/Clone + ModelServices methods. |

## MCP tools (`outsystems-liveeditor`)

| Tool | Bridge cmd | Notes |
|---|---|---|
| `live_set_style_class` | `set_style_class` | Container-aware (Style CP) + clears stray CustomStyle. |
| `live_set_module_css` | `set_user_css` | Repointed — writes user CSS (editor-visible). |
| `live_set_user_css` | `set_user_css` | Explicit user-CSS write. |
| `live_probe_style_prop` | `probe_style_prop` | Verify a container class landed (ParsedExpression). |
| `live_read_theme_css` | `read_theme_css` | Verify CSS source field lengths. |
| `live_probe_sheet` | `probe_sheet` | Discover the sheet's public API. |

## Diagnostics table

| Symptom | Cause | Check / fix |
|---|---|---|
| Class "disappears" after save/reload | Class on `CustomStyle` of a **container** (Style CP `_valueExpression` null) | `live_probe_style_prop` → container Style CP must be `ParsedExpression` with the value; re-apply via `live_set_style_class`; clear stray `CustomStyle`. |
| Page looks styled but theme CSS editor is empty | Only `_cssSource` written; `_userCssSource` null | `live_read_theme_css` → `_userCssSource` must be non-null; `live_set_user_css`/`live_set_module_css`. |
| `InvalidOperationException: ... requires an element of type 'String', but the target element has type 'Object'` | PowerShell `ConvertTo-Json` wrapped the long CSS as `{"value":"..."}` (raw-pipe client) | Check `%TEMP%\OsLiveBridge.log` `read:` line for `"css":{"value":...`. Send via `scripts/Send-BridgeCmd.ps1` or `JavaScriptSerializer` + concat. |
| Command commit throws after a "create fresh expression" CSS write | Corrupt `_userCssSource` from reflection-assigned half-built `LightweightExpression` | Use `SetUserCssSource` only; no field reflection for CSS. |

## Client-side serialization (the gotcha, in detail)

Windows PowerShell 5.1 `ConvertTo-Json -Compress` wraps strings longer than ~4 KB
into `{"value":"..."}`. The bridge then receives an object where `GetStr("css")`
expects a string.

- **Wrong:** `@{ cmd=...; css=$bigCss } | ConvertTo-Json -Compress`
- **Right:** serialize the string alone with `JavaScriptSerializer` and concatenate:

```powershell
Add-Type -AssemblyName System.Web.Extensions
$ser = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$ser.MaxJsonLength = 10485760
$json = '{"cmd":"set_user_css","module":"FitnessManager","css":' + $ser.Serialize($bigCss) + '}'
```

`JavaScriptSerializer.Serialize($hashtable)` throws a circular-reference error on
PowerShell hashtables — serialize the string only. The MCP server's Bridge helper
already uses `JsonSerializer.Serialize` (safe). `scripts/Send-BridgeCmd.ps1` wraps
the raw pipe with correct serialization.

## The persistence loop (verified)

1. Mutate live (`live_set_style_class` per container; `live_set_user_css` for CSS).
2. Verify in-memory (`live_probe_style_prop` → ParsedExpression; `live_read_theme_css`
   → `_userCssSource` non-null).
3. User **Ctrl+S** → check the saved `.oml` (Style CP `Value="class"`, no stray
   `CustomStyle`; theme `UserCssSource` non-empty).
4. **Restart SS** + reopen → confirm theme CSS editor and container Properties panel.

## The OutSystemsUI CSS override hierarchy (O11 applicability verified)

OutSystems UI does not style components with flat rules — every overridable
property is a **CSS custom property with a default**, so apps/themes override
the variable instead of the rule. Condensed from the official OutSystems UI
framework docs (`outsystems-ui\CSS-ARCHITECTURE.md` and the compiled O11 bundle
`outsystems-ui\classic-theme\O11.OutSystemsUI.css` in the OutSystems ground-of-truth
repo; framework is MIT — summarizing here).

The chain (top = most specific, what a single widget instance needs):

```
property                     → var(--osui-{component}-{prop})   ← Tier 4 · component CSS API (per-instance)
  --osui-…                   → var(--{role})                    ← Tier 3 · theme role vars at :root (theme override)
    --{role}                 → $token-…                          ← Tier 2 · compile-time SCSS tokens (newer OSUI only)
      $token-…               → var(--token-…, <primitive>)       ← Tier 1 · runtime design tokens (newer OSUI only)
```

Concrete example (Card background):

```css
.card { --osui-card-background: var(--color-background-surface); }
.card { background-color: var(--osui-card-background); }
:root  { --color-background-surface: #ffffff; }
```

**O11 applicability (verified against the compiled O11 bundle):**
- **Tier 4 (`--osui-*`) EXISTS in O11** — the shipped
  `O11.OutSystemsUI.css` defines and consumes hundreds of them
  (`--osui-tooltip-background-color`, `--osui-layout-main-padding`,
  `--osui-balloon-shadow`, `--osui-menu-layer`, …). This is the preferred
  override surface: set the variable in the theme CSS or a widget's Style
  Classes context instead of fighting the component rule.
- **Tier 3 role variables in O11** use the *classic* vocabulary:
  `--color-*` (`--color-neutral-4`), `--space-*` (`--space-s/m/xl`),
  `--border-size-*` (`--border-size-s`), `--shadow-*` (`--shadow-s`),
  `--layer-*` (`--layer-global-off-canvas`, `--layer-local-tier-1/2`,
  `--layer-below/above`). Overriding these re-skins every OSUI component that
  consumes the role.
- **Tiers 1–2 (`--token-*`/`$token-*`) are NEWER-generation OutSystems UI**
  (post token-migration). Do not assume they exist in an O11 environment —
  check the deployed OutSystemsUI theme CSS before using `--token-*`.

**Practical rules for our styling work:**
1. Prefer, in order: (a) `--osui-{component}-{prop}` scoped to the widget/theme,
   (b) a Tier-3 role variable for app-wide re-skins, (c) a raw property override
   only as a last resort (it fights specificity and OSUI updates).
2. Z-index layering is variables too: OSUI computes popups/menus from
   `--layer-*` + `--osui-*` calc() chains — never hard-code `z-index` over an
   OSUI overlay; override the layer variable.
3. To discover which `--osui-{component}-*` variables exist for a component,
   grep the deployed OutSystemsUI CSS (helper: `scripts\Get-OsuiVars.ps1
   -Component tooltip`), then set them in the theme CSS via
   `live_set_user_css`.
