---
name: outsystems-omleditor
description: Use when the user wants to edit an OutSystems module's .oml file headlessly (no UI, no running SS) via the outsystems-omleditor MCP server — ONLY when the user explicitly requests headless/no-SS/.oml-file editing (default is the live editor, outsystems-liveeditor). Triggers on "edit oml", "headless edit", "create service action without UI", "add dependency oml", "regen signatures", "probe oml". Provides 6 tools. Read editor-workflow first for the mechanism + save-edit-reload workflow.
---

# outsystems-omleditor (headless .oml editor MCP)

**Gate: use only when the user explicitly requested headless / no-SS /
`.oml`-file editing. Default is `outsystems-liveeditor` (live).**

One MCP server, six tools. All operate on `.oml` **files** (not the live module).
SS 11 must be installed (the server loads its model DLLs); SS need not be running.

**Read `editor-workflow` first** — it covers the load/edit/regen/verify mechanism
and the mandatory **Save → edit → reload** workflow.

## Prerequisites
- The `.oml` to edit exists on disk. Have the user **Save (`Ctrl+S`)** the module
  in Service Studio first (the on-disk file reflects the last save, not unsaved
  edits). Note the `.oml` path.
- An output path for the result (`outOml`). Write to a **new** file (SS locks the
  source while the module is open); the user reloads the result in SS.

## Tools

### `probe_oml(omlPath)` — inspect first
Lists every fragment name, `IsValidOml`, header `Valid`/`IsValid`, productKey
cache. **Always call this before editing** to learn the module's fragment layout
(e.g. does it have `ServiceAPIMethods`? which `NodesNotShownInESpaceTree#<key>`
fragments exist?). Read-only, safe.

### `get_fragment(omlPath, fragment)` — read one fragment
Dumps the XML of a fragment. Use to see an element's exact structure before
cloning, or to read entities/actions/references. Read-only.
Examples: `ServiceAPIMethods`, `UserActions`, `eSpace`, `References`,
`NodesNotShownInESpaceTree#<actionKey>`.

### `scan_oml(omlPath, needle)` — find where something lives
Lists fragments whose XML contains the needle (an element name, key, or tag like
`Flows.ServiceAPIMethod`). Use to locate the fragment for an element you want to
clone or edit. Read-only.

### `create_service_action(omlPath, outOml, name, templateName?)` — create a Service Action
- If the module already has service actions: **clones** `templateName` (or the
  first service action if omitted), giving it a fresh key/name and **cloning its
  Start→End flow fragment with remapped node keys** (so links stay valid).
- If the module has **no** service actions yet: builds the `ServiceAPIMethods`
  fragment from scratch (marks `eSpace <ServiceAPIMethods HasChildren="Yes">`,
  creates the fragment with one root-level `Flows.ServiceAPIMethod`, empty flow).
- Regenerates signatures, writes `outOml`, verifies `IsValidOml` + read-back.
- After: open `outOml` in SS → Logic → Service Actions shows the new action.

### `add_dependency(omlPath, outOml, referenceXml)` — add a module dependency
Appends a `<Reference …>` element (provided as XML) to the `References` fragment,
bumps `Count`, regenerates signatures, writes `outOml`, verifies the reference is
present after reload. Obtain the `<Reference>` XML from a module that already
consumes the producer (via `get_fragment` on its `References` fragment).

**Live alternative (PREFERRED):** if both modules are OPEN in SS, use
`live_consume_elements(consumer, producer, "*")` instead — it consumes all public
elements (all 15 types) live, with no `.oml` file, no Save, no reload. See the
`managing-dependencies` + `live-editing` skills.

### `regen_and_write(omlPath, outOml)` — round-trip / fixup
Loads a `.oml`, regenerates signatures, writes it to `outOml`. Use to normalize a
hand-edited `.oml` or to verify a file round-trips cleanly (`IsValidOml`).

## Self-verification (every write tool)
Each write tool reloads the output and reports `IsValidOml` + `Valid`/`IsValid` +
a read-back of the new element. Trust only the read-back. If it says `FAIL:`, the
edit did not land — re-`probe_oml` and retry; do not proceed.

## Decision flow
1. `probe_oml` → learn fragments.
2. Need to see an element? `get_fragment` / `scan_oml`.
3. Create service action → `create_service_action`.
4. Add dependency → `add_dependency` (get the `<Reference>` XML first).
5. Hand-edited a fragment externally? `regen_and_write` to fix signatures.
6. Tell the user to **reload the result in SS** and confirm.

## Gotchas
- Write to a **new** `outOml` (don't overwrite a file SS has open).
- `Count` attr on fragment roots is normalized away by the writer — benign.
- A from-scratch service action has an empty flow (`HasChildren="No"`); clone an
  existing one to get a real Start→End flow.
- The first baseline (`AI Outsystem Automation`) is **extraction-only** now — its
  old `outsystems-editor` (FlaUI) server has been removed. Editing lives entirely in
  this baseline: `outsystems-omleditor` (headless files) + `outsystems-liveeditor`
  (live). Don't confuse the two editor servers here.
- **Live alternative:** if the module is OPEN in SS and you want the change in the
  tree immediately (no Save/edit/reload), use the `outsystems-liveeditor` MCP
  (`live_create_service_action`, `live_consume_elements`, etc.) instead — see the
  `live-editing` skill. This headless server edits `.oml` files with no SS running;
  the live server edits the open module with SS running. For dependencies specifically,
  `live_consume_elements` is strongly preferred (consumes all 15 element types
  automatically; the headless `add_dependency` requires manually sourcing the
  `<Reference>` XML).
