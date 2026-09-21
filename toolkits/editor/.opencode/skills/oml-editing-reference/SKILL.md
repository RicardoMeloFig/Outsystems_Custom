---
name: oml-editing-reference
description: Deep reference for headless .oml editing - the OutSystems Oml .NET API surface (LoadWithoutUpgrades, GetFragmentXmlReader/Writer, SetNeedsSignatureRegeneration, GetBytes, IsValidOml), the fragment model (eSpace, ServiceAPIMethods, UserActions, NodesNotShownInESpaceTree, References, Signature), how signatures work (content hashes), ObjectKey format, key remapping for clones, and the proven-operations table. Read when extending the editor to new element types. **Headless editing happens only on explicit user request; the default is live editing (outsystems-liveeditor).**
---

# .oml editing reference (headless)

> **Gate: headless `.oml` editing is used ONLY when the user explicitly
> requests it — the default editing path is live in-process
> (`outsystems-liveeditor`).**

The factual reference behind the `outsystems-omleditor` tools. Use when extending
editing to a new element type (entity, attribute, structure, …) or debugging a
fragment edit.

> **Live (in-process) editing** is a separate mechanism: it mutates the **open**
> module via the SS model API inside `ServiceStudio.Commands.Command.Execute`
> (the command-opener), not by editing `.oml` fragments. The command system (the
> `UndoManager` guard/state, `PresenterContext`, `Command.ExecuteFromAsyncCode`)
> is documented in the `live-editing` skill. This skill covers the **headless
> file** API only.

## The Oml .NET API surface
Type: `OutSystems.Model.Implementation.Oml.Oml` (in
`OutSystems.Model.Implementation.dll`, under the SS install dir). Loaded via
`Assembly.LoadFrom` + an `AssemblyResolve` handler that pulls dependencies from
the same dir. **SS need not be running.**

Key members (reflection-used):
- `static Oml LoadWithoutUpgrades(byte[] omlContent, string productKey)` — load
  with `""` (empty product key is accepted; the key is only for the server open
  handshake, not local integrity).
- `bool HasFragment(string)` — does a fragment exist?
- `XElementReader GetFragmentXmlReader(string)` → `.ToXElement()` gives the
  fragment's `XElement`. Throws `MissingFragmentException` if absent.
- `Writer GetFragmentXmlWriter(string)` → `.Write(XElement)` writes the fragment.
  **Creates the fragment if the name does not yet exist** (proven: a new flow
  fragment was created on write). Also overloads take `(XElement, CollectionIndex)`
  / `(ObjectKey, CollectionIndex)` — the string overload is enough for our use.
- `void SetNeedsSignatureRegeneration()` — mark signatures stale.
- `byte[] GetBytes()` — serialize (recomputes hashes when marked stale) → valid `.oml`.
- `static bool IsValidOml(byte[])` — structural validity check.
- `static OmlHeader GetOmlHeader(byte[])` → `.Valid`, `.IsValid`, `.Name`,
  `.eSpaceKey`, `.Signature`, `.GeneralHash`, `.ContentHash`, …
- `string GetProductKeyCache()` — usually empty (the key isn't stored in the file).
- `void SetNeedsFullVerify()`, `FullVerifyDone()` — full verification hooks.
- `void DeleteFragment(string)` / `DeleteFragment(ObjectKey, CollectionIndex)`.
- `IEnumerable<string> DumpFragmentsNames()` — all fragment names.
- `void ApplyToXmlFragments(Action<XElement>, bool)` — visit every fragment.
- `static bool IsValidOml(Stream)` / `Oml.IsValidOml(byte[])`.

## How signatures work (the breakthrough enabler)
The `.oml` integrity is **content hashes**, **not** a product-key crypto
signature:
- `OmlHeader.Signature` (e.g. `C3Z7iDpumaJe/tq5BhC3grWxN+M=`) — a hash.
- `GeneralHash`, `ContentHash`, `CompilationHash` — module-level hashes.
- The `Signature` **fragment** holds `<ElementSignatures>` with per-element
  `CompatibilitySignatureHash` and `FullSignatureHash`.
`SetNeedsSignatureRegeneration()` + `GetBytes()` recomputes all of these. No key
needed. A headlessly-edited `.oml` therefore passes `IsValidOml=True` + `Valid=True`
and loads in SS. (Confirmed: prior edits' outputs reload cleanly; SS opened them.)

## The fragment model
- `eSpace` — the main fragment. Contains collection **placeholders**:
  `<UserActions HasChildren="…">`, `<ServiceAPIMethods HasChildren="…">`,
  `<Entities>`, `<Structures>`, `<References>`, `<Folders>`, …
  `HasChildren="Yes"` means the collection's children live in a **separate**
  fragment; absent/`No` means inline (or empty).
- `ServiceAPIMethods` — one `<Flows.ServiceAPIMethod>` per service action.
  Attrs: `Key`, `Name`, `Public`, `Folder` (→ `ModelFolder:/Folders.<key>`),
  `GeneralHash`, `DebuggerHash`, `LastModifiedByCommand`, … Children: `Image`,
  `TextResources`, `NodesShownInESpaceTree`, `NodesNotShownInESpaceTree
  HasChildren="Yes">`, `Metadata`, `LocalVariables`, `InputParameters`,
  `OutputParameters`.
- `UserActions` — `<Flows.UserAction>` / `<Flows.UserActionInsideFolder>` (server
  actions). Same child shape as ServiceAPIMethod.
- `NodesNotShownInESpaceTree#<ownerKey>` — the flow graph for one action: nodes
  (`<Nodes.Start>`, `<Nodes.End>`, `<Nodes.If>`, `<Nodes.Assign>`,
  `<Nodes.Comment>`, `<Nodes.ErrorHandler>`, …) and `<Links.Sequence>` /
  `<Links.True>` / `<Links.False>` with `TargetNode="Nodes.<Type>:^/^/
  NodesNotShownInESpaceTree.<targetNodeKey>"`. **References use the target node's
  key**, not the action key. (A fragment is named by the owner action's key;
  internal links reference node keys.)
- `References` — `<Reference>` entries (dependencies). `Count` on the root.
- `Signature` — per-element signature hashes (regenerated automatically).
- `Structures`, `SiteProperties`, `ClientVariables`, `AnonymousStructures`,
  `ListTypes`, `NodesShownInESpaceTree#<key>`, `Widgets#<key>`, … — other
  collections.

## ObjectKeys
- 22-char base64 of 16 random bytes, no `=` padding, `/` → `_`.
- Charset observed: `A–Z a–z 0–9 + _` (no `/`). `+` and `_` both appear.
- Must be **globally unique** within the module. Random 128-bit is collision-safe.

## Cloning an element with a flow (key remapping)
When you clone an action that has a `NodesNotShownInESpaceTree#<key>` flow
fragment, you must:
1. Give the cloned `Flows.ServiceAPIMethod` a **new** `Key`.
2. Create the flow fragment under the **new** name
   `NodesNotShownInESpaceTree#<newKey>`.
3. Inside the cloned flow XML, **regenerate every node/link `Key`** (collect all
   `Key` attributes, map old→new) and string-replace old keys with new ones —
   this fixes both `Key="…"` attrs **and** `TargetNode="…<targetNodeKey>"`
   references. (Keys are unique 22-char tokens; safe to replace.)
4. Keep the flow's node structure otherwise (Start→End, comments, etc.).
The `create_service_action` tool does this automatically when cloning.

## Proven operations table
| Operation | How | Verified |
|---|---|---|
| Add element to existing fragment | append child, bump `Count`, `Write`, regen | ✓ service action, Reference |
| Create a new fragment | `GetFragmentXmlWriter(newName).Write(xe)` | ✓ flow fragment |
| Clone element + flow | new key + remap flow node keys + create flow fragment | ✓ service action |
| From-scratch element + fragment | set eSpace `HasChildren="Yes"`, create fragment with new element | ✓ service action |
| Round-trip / regen | `SetNeedsSignatureRegeneration` + `GetBytes` | ✓ IsValidOml=True |

## Roadmap operations (same pattern, not yet tooled/verified)
- **Entity**: add `<Entity>` to `Entities` (or its collection fragment) + an
  `EntityAttributes` fragment; default `Id : LongInteger` attribute.
- **Attribute**: append `<EntityAttribute>` to the entity's attribute collection
  fragment + regen.
- **Structure**: add to `Structures` fragment + attributes.
- **Delete element**: `oml.DeleteFragment(name)` + remove the owner reference +
  regen (or remove the element from its collection fragment + regen).
- **CRUD wrappers**: compose entity + service-action creation.

> **Live alternative for dependencies:** the live editor's `live_consume_elements`
> tool handles dependency management (consuming all 15 element types from a producer)
> via `IESpace.AddDependency<T,S>` — no `.oml` editing needed. See the
> `managing-dependencies` + `live-editing` skills. The headless `add_dependency`
> (above) is the fallback for when SS isn't running.

> **Element semantics ground truth:** for what each element type means, its
> property defaults, and validation constraints (exposure rules, mandatory
> defaults), see `docs/element-class-reference.md` — condensed from the official
> O11 element-class reference.

## Gotchas
- `Writer.Write` normalizes the fragment root's `Count` attribute (drops it) —
  benign; SS recomputes; `IsValidOml` still `True`.
- Reading a missing fragment throws `MissingFragmentException`; always
  `HasFragment` first.
- The on-disk `.oml` reflects the last **Save** in SS, not unsaved edits.
- SS locks the source `.oml` while the module is open — write to a new file.
- **`IsValidOml=True` ≠ error-free.** TrueChange re-validates on open: flow
  connector rules, one handler per exception type, public-element parameter
  constraints, unknown variables, naming. Checklist: `editor-workflow` skill →
  "Validity checklist" (from the official errors-and-warnings catalog).
