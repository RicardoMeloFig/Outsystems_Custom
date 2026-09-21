---
name: creating-service-actions
description: Use when the user wants to create a Service Action in an OutSystems module headlessly (no UI, no running SS) by editing the .oml file — ONLY when the user explicitly requests headless/no-SS/.oml-file editing (default is live: creating-service-actions-live). Triggers on "create service action", "add service action", "headless service action", "create service action without UI". Proven on SS 11.55.81 via create_service_action (clone + from-scratch + flow clone). Read editor-workflow first. For live in-process editing (module open in SS, no reload), use creating-service-actions-live instead.
---

# Creating Service Actions (headless .oml editing)

**Gate: use only when the user explicitly requested headless / no-SS /
`.oml`-file editing. Default is live editing (`creating-service-actions-live`).**

Create a Service Action by editing the module's `.oml` **file** directly — no
Service Studio UI, no running SS. Proven end-to-end (SS 11.55.81): a
headlessly-created service action opens correctly in SS and appears under
Logic → Service Actions.

**Read `editor-workflow` first** — Save → edit → reload workflow, the load/edit/
regen/verify mechanism, and the file-vs-live constraint.

## Live Alternative (default)

**If the target module is OPEN in SS** (the normal case), use
`creating-service-actions-live` instead — mutations land in the live tree
immediately, no Save/edit/reload. This headless skill is used only when the
user explicitly asks for headless/.oml-file editing (e.g. SS not running, or
a saved `.oml` artifact is wanted).

## What a Service Action is in the .oml
- A `<Flows.ServiceAPIMethod>` element in the **`ServiceAPIMethods`** fragment.
- Attributes: `Key`, `Name`, `Public="Yes"` (service actions are public by
  default), optional `Folder` (a `ModelFolder` with `ESpaceTreeFolder=
  "ServiceActions"`), `LastModifiedDate`, etc.
- Its flow graph lives in a **`NodesNotShownInESpaceTree#<actionKey>`** fragment
  (Start → End nodes + links). The element's `<NodesNotShownInESpaceTree
  HasChildren="Yes">` signals that fragment exists.
- A module with **no** service actions has **no** `ServiceAPIMethods` fragment
  yet (and `eSpace <ServiceAPIMethods>` has no `HasChildren`). The first service
  action creates both.

## Prerequisites
- The `.oml` is on disk. Have the user **Save (`Ctrl+S`)** the module in SS first.
- An `outOml` path (new file — SS locks the source while open).

## Create (one tool call)

`create_service_action(omlPath, outOml, name, templateName?)`

The tool auto-selects the right path:
- **Module already has service actions** → clones `templateName` (or the first
  service action) and **clones its flow fragment with remapped node keys**. Result:
  a new service action with a real Start→End flow.
- **Module has no service actions** → builds the `ServiceAPIMethods` fragment from
  scratch (sets `eSpace <ServiceAPIMethods HasChildren="Yes">`, creates the
  fragment with one root-level `Flows.ServiceAPIMethod`, empty flow).

Then it regenerates signatures, writes `outOml`, and self-verifies
(`IsValidOml=True` + read-back of the new element).

## Example (proven)
```
# MyModule already had SourceAction; clone it into NewAction with a real flow:
create_service_action(
  omlPath = "...\MyModule.oml",
  outOml = "...\MyModule_new.oml",
  name = "NewAction")
# -> cloned SourceAction (Key remapped), flow fragment created with remapped
#    Start/End/Comment keys, IsValidOml=True, 2 service actions after reload.
# Open MyModule_new.oml in SS -> Service Actions shows both.
```
```
# AnotherModule had many service actions; add one more by cloning the first:
create_service_action(omlPath="...\AnotherModule.oml", outOml="...\AnotherModule_with_svc.oml",
                      name="NewAction")   # confirmed working in SS
```

## Verify
- The tool's output ends with `OK: created '<name>' … flow=cloned|empty|none`.
- Offline: `IsValidOml=True`, `Valid=True`, and `probe_oml` shows
  `ServiceAPIMethods` with the bumped count.
- In SS: open `outOml` → Logic → Service Actions → the new action; open its flow
  to see Start→End (if cloned).

## After creation
- **Reload in SS**: close the module, open `outOml`. The new service action
  appears in the tree.
- To **add input/output parameters**: not yet a headless tool (roadmap). For now,
  add params in SS after reload, or extend the tool (append a
  `<Variables.SerializableInputParameter>` to the element's `<InputParameters>`
  and regen — same pattern as the element edit).

## Gotchas
- **Name uniqueness**: names are unique across action types in a module. If a
  consumed server action shares the intended name, SS auto-suffixes. Use a suffix
  convention (e.g. `_BL`).
- **Empty flow (from-scratch)**: a from-scratch action has `HasChildren="No"`
  (no flow fragment). It appears in the tree; SS may auto-add Start/End when you
  open the flow. Prefer the **clone** path (clone any existing service action) to
  get a real flow — even across modules you can clone then rename.
- **File lock**: write to a new `outOml`; don't overwrite the file SS has open.
- **Public by default**: service actions are `Public="Yes"` (unlike server
  actions). The tool sets this.
- **Save before edit**: the `.oml` reflects the last Save. Have the user `Ctrl+S`.

## After creation
- **Reload in SS**: close the module, open `outOml`. The new service action
  appears in the tree.
- To **add input/output parameters**: add params in SS after reload, or
  extend the tool by appending a `<Variables.SerializableInputParameter>` to
  the element's `<InputParameters>` and regen — same fragment-edit pattern.
- To **build a custom flow** (exception handling, multiple paths): use the
  `creating-service-actions-live` skill after reloading in SS — it provides
  live node-level control via the OsLiveBridge plugin.

## Gotchas
- **Name uniqueness**: names are unique across action types in a module. If a
  consumed server action shares the intended name, SS auto-suffixes. Use a suffix
  convention (e.g. `_BL`).
- **Empty flow (from-scratch)**: a from-scratch action has `HasChildren="No"`
  (no flow fragment). It appears in the tree; SS may auto-add Start/End when
  you open the flow. Prefer the **clone** path to get a real flow.
- **File lock**: write to a new `outOml`; don't overwrite the file SS has open.
- **Public by default**: service actions are `Public="Yes"`. The tool sets this.
- **Save before edit**: the `.oml` reflects the last **Save** (`Ctrl+S`), not
  unsaved in-memory edits.

## Roadmap
- `add_service_action_param(action, name, type, direction, mandatory)` — append a
  parameter element + regen (same fragment-edit pattern; not yet a tool).
- Cross-module clone (clone a service action from a different module's `.oml`
  into the target) — needs reference/key fixup; roadmap.
- Headless flow building via .oml fragment editing — the live approach
  (`creating-service-actions-live`) handles this; headless fragment editing
  for flows is not yet tooled.
