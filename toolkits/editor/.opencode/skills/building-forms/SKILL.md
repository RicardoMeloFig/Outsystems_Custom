---
name: building-forms
description: Use when the user wants a data-entry Form (create/edit, validation, save). LIVE-first (binding proven; Valid-property wiring next). Read live-editing + expression-reference first.
---

# Building Forms — LIVE (bindings PROVEN)

Proven precedent: UserProfileCard `ProfileForm` (form + radio-group/input/dropdown/
checkbox/switch + Save button → `SaveProfile` with If-empty validation → JS log →
Message → RaiseEvent). Follow that shape.

## Compose (PROVEN calls)

```
live_add_nr_widget(kind="form", name="OrderForm")                     # NRWidgets.Form
live_add_nr_widget(kind="input", parent="OrderForm"?, ...)            # fields (parent nesting where factory allows)
live_set_block_cp(block, widget, "Variable", "TempTitle")             # bind each field to a var (parsed-CP path)
live_add_button_to_block(block, "SaveBtn", "Save")                    # or live_add_button on screens
live_set_block_button_onclick(block, "SaveBtn", "SaveOrder")          # block client action
```

Field→var matrix (all PROVEN): input→Text, textarea→Text, dropdown→Integer (+Labels/
Values CPs), checkbox→Boolean, switch→Boolean, radio-group→Text (+OnChange action).

## Validation — the PROVEN pattern (client + server)

```
SaveOrder flow: Start → If (TempTitle = "") → True: Message("Title is required.", Error) → End
                                            → False: (per-field checks…) → save ExecuteAction → Message(Success) → End
```

- Validate in the CLIENT action first (instant feedback), re-validate server-side
  around the write (never trust client alone).
- One Message per failed field (Error kind) + abort; success Message + RaiseEvent
  (`OnSaved(SavedId)`) so parents refresh. See UserProfileCard SaveProfile as canon.
- Mandatory marker: label suffix `" *"` + Style class (convention, no widget needed).

## Form.Valid / field-validity properties (NEXT)

- `HasValidationProperties` / `IsValidationAggregator` probed on widgets; the `Valid`
  flag wiring (`Form.Valid`, per-field `Valid`, ValidationMessage display) is UNVERIFIED.
  Until then: the explicit If-check pattern above (it is also the clearer UX).
- Module-level validator texts exist (`MandatoryValidatorMsg` etc. on ESpace) —
  `set_module_validator_message` is a nice-to-have, not needed for forms to work.

## Create vs Edit modes
- Optional input param (`OrderId`, Text, via `live_add_screen_input_param` ✅ proven) —
  empty = create, present = load aggregate by Id → fill vars → same form → save branches.
- After save: navigate back to List (navigation params = `set_link_params`, NEXT batch).

## Verify
- Per-widget errors on fields + button; action-flow errors on SaveOrder; screen clean.
- Negative test: empty submit → error Message, no write (check entity row count).

## Status
- ✅ form+fields+bindings+button+validate+save+event (UserProfileCard canon).
- ⚠️ Valid-flag wiring, link-params navigation — next batch.
