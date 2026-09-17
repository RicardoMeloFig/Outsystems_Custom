---
name: building-screens-and-buttons
description: Use when the user wants to build Reactive web screens and wire interactive widgets (containers, text, links, BUTTONS) in an OPEN module (live), including creating SCREEN-LEVEL Client Actions and wiring a Button's OnClick to them. Triggers on "create button", "add button", "wire button onclick", "button action", "client action on screen", "create web screen", "add container/text/link", "layout placeholder", "Actions placeholder". Proven on SS 11.55.81 via the OsLiveBridge plugin. Read live-editing + outsystems-liveeditor first; the Button is a plugin CustomWidget (NOT CreateWidget<T>), and a Button OnClick MUST target a screen-level ClientScreenActionFlow.
---
# Building screens & buttons — live (open module, no reload)

How to create web screens in a flow, fill layout placeholders with widgets, and add a
REAL Button wired to a screen-level Client Action's OnClick. Proven on SS 11.55.81 in
FitnessManager (TestScreen, zombieGame flow, Actions placeholder). All mutations land in
the live tree as undo units (`Ctrl+Z`), in-memory until `Ctrl+S`.

## The two hard-won Button facts

1. **A Reactive Button is a plugin `CustomWidget`** — `NRWebWidgets.CustomWidget-
   OutSystems.Plugin.NRWidgets.Button` (in the OML it carries
   `CustomObjectImplKey`). It CANNOT be created via `CreateWidget<T>` (no creatable
   widget interface). It is created with the **`live_add_button`** tool, which uses the
   plugin's registered `Button+Kind.Instance.Descriptor` via `CreateWidget(descriptor)`.
2. **A Button's OnClick can ONLY be wired to a SCREEN-level Client Action** — a
   `ClientScreenActionFlow` defined on the screen itself. The OnClick handler's
   `Destination` property is typed `IClientSideDestination`, which global
   `ClientActionFlow` does **not** implement (the destination setter throws
   "cannot be converted to IClientSideDestination"). Use
   **`live_create_screen_client_action`** (NOT `live_create_client_action`, which makes a
   global `ClientActionFlow`).

## The one-two-three-punch for a wired button

```
live_create_screen_client_action(module, screen, "Dummy")   # 1. screen-level Client Action (ClientScreenActionFlow)
live_add_button(module, screen, "Actions", "", "StartBtn", "Start", "\"btn btn-primary\"")
live_set_button_onclick(module, screen, "StartBtn", "Dummy") # 3. wire OnClick -> Destination
```

`live_set_button_onclick` resolves the action on the **screen's ClientActions**
first (then the module's), gets the OnClick EventHandler, and sets its **Destination**
to the action. Verify with `live_probe_widget_members` → `onClickDestination`
shows `Dummy (NRFlows.ClientScreenActionFlow:...)`.

> Name collisions: SS auto-suffixes (a leftover global `Dummy` makes the screen one
> `Dummy2`). If a button name already exists, `live_add_button` creates
> `Name2` too. Check current state with `live_list_widgets` first; prune duplicates
> with `live_delete_from_placeholder`.

## Web screens & layout placeholders

A Reactive screen uses a layout WebBlockInstance with **placeholders**: `Header`,
`Breadcrumbs`, `Title`, `Actions`, `MainContent`, `Footer`. Widgets dropped into a
placeholder land under the corresponding placeholder in the layout.

- `live_list_web_flows(module)` — flows + screens + themes (shows module Kind:
  Reactive vs Traditional).
- `live_create_web_screen(module, webFlow, name)` — new screen in a flow (live).

  ```
  live_create_web_screen(FitnessManager, zombieGame, TestScreen)   # creates a screen w/ empty layout placeholders
  ```

- `live_list_widgets(module, screen)` — dump the full widget tree incl. placeholders
  and their children (each widget's `concreteType`, interfaces, children, `styleClasses`).

### Filling a placeholder / adding widgets

| Want | Tool |
|---|---|
| Container (div) | `live_add_container(module, screen, parent, name, styleClass)` — `parent=''` for the screen top level, or a container name to nest |
| Static Text | `live_add_text(module, screen, parent, name, text, styleClass)` |
| Expression | `live_add_expression(module, screen, parent, name, value, styleClass)` |
| Link | `live_add_link(module, screen, parent, name, text, targetScreen?, styleClass)` |
| HTML element (`<h1>`/`<p>`…) | `live_add_html_element(module, screen, parent, name, tag, styleClass)` |
| **Button** | **`live_add_button(module, screen, placeholder?, parent?, name, text, styleClass)`** — pass `placeholder` to drop into a layout placeholder (e.g. `Actions`), else `parent` ('' = screen) |
| Into a layout placeholder (any kind) | `live_add_to_placeholder(module, screen, placeholder, kind, name, text?, targetScreen?, styleClass?)` — kind ∈ container\|text\|expression\|link |
| Inside a container inside a placeholder | `live_add_inside_placeholder(module, screen, placeholder, parent, kind, name, …)` — nests into the container's `content` placeholder |
| Into a web block (nav menu etc.) | `live_add_link_to_block(module, block, parent?, name, text, targetScreen?, styleClass?)` (also `live_list_block_widgets`, `live_set_style_class`) |
| List placeholders | `live_list_placeholders(module, screen)` |
| Delete from placeholder | `live_delete_from_placeholder(module, screen, placeholder, name)` |

### The Button's label

`live_add_button` sets `text` on the Button's **`content` CustomPlaceholderWidget**
Text child (SS auto-creates a default Text there — the tool reuses it, no duplicate).
`styleClass` becomes the Button's style (`"btn btn-primary"` etc.).

## Screen-level Client Actions (the "function" a button calls)

A button's OnClick runs a **Client Action that lives on the screen**. These appear under
the screen in SS and serialize as `NRFlows.ClientScreenActionFlow` in the screen's
`<ClientActions>` collection.

```
live_create_screen_client_action(module, screen, name)
```

Returns the created type (`ClientScreenActionFlow`) + screen ClientActions before/after.
Screen actions have flows you can edit with all `live_*` flow tools (`live_list_flow`,
`live_debug_create_node`, `live_add_assign_node`, …).

> `live_create_client_action(module, name)` creates a **global** `ClientActionFlow`
> (module-level) — fine for most logic, but **not assignable to a Button OnClick**.
> Use the screen variant for buttons.

## Wiring OnClick

```
live_set_button_onclick(module, screen, button, actionName)
```

- Finds the Button widget (recursively, incl. layout placeholders).
- Resolves `actionName` on the **screen's ClientActions** first, then the module.
- Gets (or creates) the button's OnClick EventHandler
  (`NRWebWidgetEvents+EventHandler`), then sets its **`Destination`** to the action.
- Verify: `live_probe_widget_members(module, screen, button)` →
  `onClickDestination` = `ActionName (NRFlows.ClientScreenActionFlow:...)`.

### Failure modes

| Error | Cause | Fix |
|---|---|---|
| `button widget not found` | Name doesn't exist on the screen (or a duplicate auto-suffix exists) | `live_list_widgets` to see actual names |
| `Cannot be converted to IClientSideDestination` | Wired a GLOBAL ClientActionFlow | Create it on the screen (`live_create_screen_client_action`), then re-wire |
| `action not found` | Screen action name mismatch (e.g. `Dummy` vs `Dummy2`) | check the screen's ClientActions names |

## End-to-end: button wired to a screen action

```
live_create_web_screen(FitnessManager, zombieGame, TestScreen)          # 1. screen
live_create_screen_client_action(FitnessManager, TestScreen, "Dummy")    # 2. screen Client Action
live_add_button(FitnessManager, TestScreen, "Actions", "", "StartBtn", "Start", "\"btn btn-primary\"")  # 3. button
live_set_button_onclick(FitnessManager, TestScreen, "StartBtn", "Dummy") # 4. OnClick -> Dummy
live_probe_widget_members(FitnessManager, TestScreen, "StartBtn")        # 5. verify onClickDestination
```

## Gotchas
- Live edits are **in-memory** until `Ctrl+S` in SS. Closing SS without saving loses
  them (buttons, client actions, wires all vanish — we hit this repeatedly).
- **Duplicate names:** re-running `live_add_button` with an existing name creates `Name2`.
  Check `live_list_widgets` first; clean up with `live_delete_from_placeholder`.
- `Ctrl+Z` in SS undoes each mutation (undo unit).
- For CSS classes on containers/themes see `styling-and-css-live`.

## Web blocks: the full widget zoo (proven — WidgetsStressTestBlock, SS 11.55.83)

`live_add_nr_widget` (block or screen) supports: button, label, input, textarea, checkbox,
dropdown, radio, radio-group, switch, list, **list-item**, table, image, icon, form,
button-group, container, expression, link, html, **upload, popover, popup, list-item-action**.
SS auto-creates children: RadioGroup → 3 RadioButtons, ButtonGroup → 3 items, Upload → Icon+Text,
Popover → topContent Text+Icon. `live_add_if_widget_to_block` adds an If (via the parent's
`CreateWidget<IIfWidget>` — NOT a Kind descriptor; condition via `SetCondition`).

### Parent addressing (add_nr_widget)

| parent | targets |
|---|---|
| `""` | block/screen root |
| `"ContainerName"` | the widget's `content` placeholder (containers, forms, lists) |
| `"IfName:True"` / `"IfName:False"` | the If's branch (`IfBranch.ChildWidgets`) |
| `"Widget:placeholderName"` | a named placeholder, e.g. `"DetailsListItem:rightActions"` |

Hosts without a CreateWidget surface get a ChangeParent fallback (create at root → re-parent).

### Binding widgets (the CPs that matter)

- **Input/TextArea/Switch/Checkbox/RadioGroup**: `Variable` CP → `live_set_block_cp(propName="Variable",
  value="LocalVar_X")` — bare identifiers parse to **valid Reference elements** (verify with
  `live_probe_block_cp`: `[Reference: LocalVar_X(ObjectElementReference)]`, no invalid refs).
- **Dropdown**: `Variable` (must be the entity **Identifier** — set the local's type with
  `live_set_block_variable_type(identifier=true, type="User", producerModule="(System)")`) +
  `List` (aggregate list or `User Record List` local).
- **Upload**: `FileContent` → BinaryData local, `FileName` → Text local.
- **RadioButton**: `Value` CP — quoted string literals (`"teste1"`) parse to clean Text elements.
- **Popup**: `ShowPopup` CP → Boolean local; toggle it from a button's client action
  (`Assign LocalVar_PopupOpen = Not LocalVar_PopupOpen`).
- **Icon**: `Icon` CP is a **plain string Value-attr, NOT an expression** — use the bridge
  `set_block_cp_value_attr` (`Send-BridgeCmd.ps1`, `propName="Icon", value="info"`); via
  `live_set_block_cp` it parses `info` as an identifier → "Can't identify 'info' element" error.
  The command now forces revalidation (stale SS errors flush). **List**: `Source` CP = `AggregateName.List`.
- **Expression**: `Value` CP — builtin names are **`CurrDateTime()`** (NOT CurrentDateTime()).
- **Image**: the Image CP needs a real Image **object** — use `live_set_block_cp_image`
  (strings fail with "Object data type required instead of Text").
- **Container Style classes**: use `live_set_block_cp_text` (verbatim literal, no quotes) —
  `live_set_block_cp` with a quoted string stores the quotes INTO the class name.

### Aggregates as list/dropdown sources

```
live_probe_references(module)                                        # 1. find which reference exposes the entity (User → "(System)")
live_add_aggregate_to_block(module, block, "UsersAggregate", entityName="User", producerModule="(System)")   # 2. aggregate WITH source
live_set_block_cp(module, block, "DetailsList", "Source", "UsersAggregate.List")   # 3. bind
```

### List actions (ListItemAction)

The auto-created ListItemAction in a list item's `rightActions` is UNNAMED — delete it with
`live_delete_anon_block_widgets(typeContains="ListItemAction", recursive=true)`, recreate named
(`live_add_nr_widget kind="list-item-action" parent="DetailsListItem:rightActions"`), then wire:
create a **block client action** (`live_create_block_client_action`), build its empty flow
(`live_debug_create_node` IStartNode + IEndNode → `live_set_node_target` Start→End; insert Assigns
with `live_add_assign_node where="beforeEnd"`), and wire with
`live_set_widget_handler(event="OnClick", actionName=...)`.
