---
name: editor-workflow
description: The meta-process for headless OutSystems .oml editing (no UI, no running SS, no productKey). Documents the breakthrough - load a .oml via Oml.LoadWithoutUpgrades(bytes,""), mutate XML fragments, regenerate signatures, write a valid .oml. Read this before any headless edit. Proven on SS 11.55.81.
---

# Editor Workflow (headless .oml editing — the breakthrough)

This baseline edits OutSystems modules by manipulating the `.oml` **file**
directly — no Service Studio UI, no running process, no `productKey`. It is the
headless/file counterpart to the live editor (`outsystems-liveeditor`) and to
the first baseline's extraction tools (ClrMD readers, read-only).

## The core mechanism (proven)

1. **Load** the `.oml` with an **empty product key**:
   `Oml.LoadWithoutUpgrades(bytes, "")` — the empty string is accepted; the
   product key is only used for the server "open" handshake, not local integrity.
2. **Read/write fragments**:
   - `oml.GetFragmentXmlReader(name).ToXElement()` → fragment XML.
   - `oml.GetFragmentXmlWriter(name).Write(root)` → write it back.
   - **`GetFragmentXmlWriter(string)` CREATES a new fragment** when the name does
     not yet exist (verified: a cloned flow fragment was created on write). This
     is the key fact that makes from-scratch element creation possible.
3. **Regenerate signatures**: `oml.SetNeedsSignatureRegeneration()` then
   `oml.GetBytes()` → a self-consistent `.oml`. Signatures are **content hashes**
   (`GeneralHash`, `ContentHash`, per-element `FullSignatureHash`), **not** a
   product-key crypto signature — so they recompute without any key.
4. **Verify offline**: reload the output bytes and check `Oml.IsValidOml(bytes)`
   + the `OmlHeader.Valid`/`IsValid` flags. `IsValidOml=True` + `Valid=True` is
   the green light. (Final confirmation: open the `.oml` in Service Studio.)

The editor loads the SS model DLLs (`OutSystems.Model.Implementation.dll` etc.)
from the SS install dir via `Assembly.LoadFrom` at runtime. **SS 11 must be
installed** (for the DLLs) but **need not be running**.

## The file-vs-live constraint (critical)

This editor mutates `.oml` **files**, not the live in-memory module. So the
canonical workflow is:

1. **Save** the module in Service Studio (`Ctrl+S`) → writes the `.oml` to disk.
   Note its path (the file SS opened, or copy it somewhere editable).
2. **Edit headlessly** — call an `outsystems-omleditor` tool with `omlPath` (the
   saved file) + `outOml` (where to write the result). The tool regenerates
   signatures and self-verifies (`IsValidOml` + read-back).
3. **Reload in SS** — close the module in Service Studio and open the edited
   `.oml` (or overwrite the source while SS has it closed, then reopen). The new
   element appears in the tree. Script: `scripts\Open-OmlInSS.ps1 -OmlPath <outOml>`
   (SS CLI `servicestudio.exe <oml>`; also `-Diff`/`-Merge` for compare-and-merge,
   `-Recover` for corrupted modules, `-Refresh -HostName <env> -VerifyXml <path>`
   for reference refresh).

To see live changes reflected / to keep editing in SS, the user must reload. **Live
in-process editing of the open module is now proven** (no reload) via the
`outsystems-liveeditor` MCP + the OsLiveBridge plugin — see the `live-editing` skill.
This covers service actions, flow editing, AND dependency management
(`live_consume_elements` — consumes all 15 element types from a producer, single
undo unit). Use this headless workflow when SS isn't running or you need a saved
`.oml` artifact; use live when the module is open and you want instant tree updates.

## Self-verifying edits (hard rule)

Every write tool ends by reloading the output and confirming:
- `IsValidOml=True`, `Valid=True`, `IsValid=True`.
- The new/changed element is present after reload (read-back).
If the read-back does not confirm, the edit failed — do not proceed; re-`probe_oml`
and retry. Never assume success from a "wrote N bytes" message alone.

## Which tool for what

| Goal | Tool |
|------|------|
| Understand a module before editing (fragments, validity) | `probe_oml` |
| See one fragment's XML (entity/action/reference/flow) | `get_fragment` |
| Find which fragment holds an element/name/key | `scan_oml` |
| Create a Service Action (clone or from-scratch) | `create_service_action` |
| Add a module dependency (Reference) | `add_dependency` (headless) or `live_consume_elements` (live, preferred) |
| Round-trip / fix signatures on a .oml | `regen_and_write` |

## ObjectKeys

Element/fragment keys are 22-char base64 (16 random bytes, no padding, `/`→`_`).
When **cloning** an element that has a flow fragment, you must regenerate **all**
node/link keys inside the cloned flow and update internal references (e.g.
`Start → End` link `TargetNode="Nodes.End:^/^/NodesNotShownInESpaceTree.<endNodeKey>"`
must point at the new End key). The `create_service_action` tool does this
remapping automatically. See `oml-editing-reference` for the fragment model.

## Fragment model (quick map)

- `eSpace` — the main fragment; collection placeholders (`<ServiceAPIMethods
  HasChildren="Yes">`, `<UserActions>`, `<Entities>`, `<Folders>`).
- `ServiceAPIMethods` — one `<Flows.ServiceAPIMethod>` per service action.
- `UserActions` — `<Flows.UserAction>` / `…InsideFolder` (server actions).
- `NodesNotShownInESpaceTree#<actionKey>` — the flow graph for one action
  (Start/End/nodes/links). Created on demand; `HasChildren="Yes"` on the owner
  element signals a separate fragment exists.
- `References` — `<Reference>` entries (module dependencies).
- `Signature` — per-element `FullSignatureHash`/`CompatibilitySignatureHash`
  (recomputed by `SetNeedsSignatureRegeneration`).

Full reference: `oml-editing-reference` skill + `docs/oml-editing-reference.md`.

## Proven vs roadmap

- **Proven (v1):** create Service Action (clone + from-scratch + flow clone),
  add dependency (Reference), round-trip/regen, probe/get/scan.
- **Roadmap (same pattern, not yet verified):** entities, attributes, structures,
  delete element, CRUD wrappers. See the stub skills (`creating-entities`, …).

## Gotchas

- **File lock:** SS holds the source `.oml` open while the module is open. Write
  to a different `outOml` path, then reload in SS (don't try to overwrite a file
  SS has open).
- **`Count` attribute:** the `Writer.Write` path normalizes the fragment root's
  `Count` attribute (drops it). This is benign — SS recomputes from children;
  `IsValidOml` still passes. (Confirmed on prior edits.)
- **Empty flow:** a from-scratch service action has `NodesNotShownInESpaceTree
  HasChildren="No"` (no flow fragment). It appears in the tree; SS may auto-add
  Start/End when you open the flow. Clone an existing action to get a real flow.
- **Save before edit:** the on-disk `.oml` reflects the last **Save**, not
  unsaved in-memory edits. Have the user `Ctrl+S` first.

## Validity checklist (what TrueChange flags after reload)

`IsValidOml=True` means the file loads — **not** that the module has no TrueChange
errors. SS re-validates on open; these are the rules our edits most commonly
touch (condensed from the official O11 errors-and-warnings catalog,
`docs-product\src\ref\errors-and-warnings\`, CC BY-NC-ND 4.0):

**Flow integrity (Invalid Flow / Invalid Action Flow):**
- Every element needs **≥ 1 incoming connector** and the **required outgoing
  connectors** (If needs 2; an unconnected End is an error). This is why the
  live runbook links Start→End before anything else.
- A **For Each** Cycle body must loop back to the For Each element (close the
  cycle).

**Exception handlers (Invalid Error Handler / Invalid Error Handler Flow):**
- At most **one handler per exception type** per flow — duplicates error.
- A handler catching **Security** exceptions must navigate to an
  **Anonymous**-role screen.
- The handler's flow path **must not cross the main flow path**.

**Public elements (Invalid Public Action / Structure / Block):**
- A `Public` action's Record-typed parameters must be **Public** entities/
  structures **defined in the same module** — a parameter typed with a
  reference (consumed entity) blocks exposing. (Same constraint mirrored in
  `docs/element-class-reference.md`.)

**Variables and expressions (Invalid Variable / Invalid Expression):**
- Assign targets and widget Variables must exist in scope (`Unknown variable`).
- Expressions are type-checked at verify — see the `expression-reference`
  skill (e.g. `IsNull()` and `NullText()` do not exist).

**Naming:**
- Screen/page names must be unique (Duplicated Page Name) and match
  identifier rules (Invalid Page Name); module names must not conflict.

**Style sheets (Unknown Object):**
- A Theme/Block style sheet referencing an image/resource must point at an
  object **in the module** with `Deploy Action ≠ Do Nothing`.

**Dependencies (warnings to expect after headless `add_dependency`):**
- `Missing/Inconsistent dependency`, `Outdated producer/consumer` — resolved by
  SS's **refresh references** (live: `servicestudio.exe -refresh <oml> <env>`;
  see `managing-dependencies`).

**Input parameters:**
- Mandatory path parameters must precede optional ones (Invalid Parameter
  Order, screens with custom URLs); REST methods constrain Body parameters.
