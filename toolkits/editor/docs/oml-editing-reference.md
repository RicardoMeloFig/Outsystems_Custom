# Headless .oml Editing — Reference

The technical reference behind the `outsystems-omleditor` MCP tools. This
documents the breakthrough: how to edit an OutSystems module's `.oml` file with
no UI, no running Service Studio, and no `productKey`.

> Proven on Service Studio 11.55.81. A headlessly-created Service Action opens
> correctly in SS (confirmed end-to-end).

## 1. The core mechanism

1. **Load** the `.oml` with an **empty product key**:
   `Oml.LoadWithoutUpgrades(bytes, "")`. The empty string is accepted — the
   product key is only used for the server "open" handshake, not local file
   integrity. (`ProductKeyCache` is normally empty in the file.)
2. **Read/write fragments**:
   - `oml.GetFragmentXmlReader(name).ToXElement()` → the fragment's XML.
   - `oml.GetFragmentXmlWriter(name).Write(root)` → write it back.
   - **`GetFragmentXmlWriter(string)` creates a new fragment** when the name does
     not yet exist. (Verified: a cloned `NodesNotShownInESpaceTree#<newKey>` flow
     fragment was created on write.) This is what makes from-scratch element
     creation possible.
3. **Regenerate signatures**: `oml.SetNeedsSignatureRegeneration()` then
   `oml.GetBytes()` → a self-consistent `.oml`.
4. **Verify offline**: reload the output and check `Oml.IsValidOml(bytes)` +
   `OmlHeader.Valid` / `IsValid`. `True`/`True` is the green light.

The editor loads the SS model DLLs (`OutSystems.Model.Implementation.dll` and
deps) from the SS install dir
(`C:\Program Files\OutSystems\Service Studio 11\Service Studio`) via
`Assembly.LoadFrom` + an `AssemblyResolve` handler. **SS 11 must be installed**
(for the DLLs); **SS need not be running**.

## 2. How signatures work (the enabler)

`.oml` integrity is **content hashes**, **not** a product-key crypto signature:

- `OmlHeader.Signature` (e.g. `C3Z7iDpumaJe/tq5BhC3grWxN+M=`) — a hash.
- `GeneralHash`, `ContentHash`, `CompilationHash` — module-level hashes.
- The `Signature` **fragment** holds `<ElementSignatures>` with per-element
  `CompatibilitySignatureHash` and `FullSignatureHash`.

`SetNeedsSignatureRegeneration()` + `GetBytes()` recomputes all of these. **No
key needed.** A headlessly-edited `.oml` therefore passes `IsValidOml=True` +
`Valid=True` and loads in SS. (Confirmed: prior edits' outputs reload cleanly and
SS opens them.)

## 3. The Oml .NET API surface

Type: `OutSystems.Model.Implementation.Oml.Oml` (in
`OutSystems.Model.Implementation.dll`).

| Member | Purpose |
|---|---|
| `static Oml LoadWithoutUpgrades(byte[], string productKey)` | load with `""` |
| `bool HasFragment(string)` | fragment exists? |
| `XElementReader GetFragmentXmlReader(string)` → `.ToXElement()` | read a fragment (throws `MissingFragmentException` if absent) |
| `Writer GetFragmentXmlWriter(string)` → `.Write(XElement)` | write/create a fragment |
| `void SetNeedsSignatureRegeneration()` | mark signatures stale |
| `byte[] GetBytes()` | serialize (recomputes hashes) → valid `.oml` |
| `static bool IsValidOml(byte[])` | structural validity |
| `static OmlHeader GetOmlHeader(byte[])` → `.Valid`, `.IsValid`, `.Name`, `.Key`, `.Signature`, … | header info |
| `string GetProductKeyCache()` | usually empty |
| `void DeleteFragment(string)` | delete a fragment |
| `IEnumerable<string> DumpFragmentsNames()` | all fragment names |
| `void ApplyToXmlFragments(Action<XElement>, bool)` | visit every fragment |
| `void SetNeedsFullVerify()` / `FullVerifyDone()` | full-verify hooks |
| `void SetProductData(productId, productName, productKey, isOpenSource)` | set product data |

## 4. The fragment model

- **`eSpace`** — the main fragment. Collection placeholders:
  `<UserActions HasChildren="…">`, `<ServiceAPIMethods HasChildren="…">`,
  `<Entities>`, `<Structures>`, `<References>`, `<Folders>`, …
  `HasChildren="Yes"` ⇒ children live in a **separate** fragment; absent/`No` ⇒
  inline or empty.
- **`ServiceAPIMethods`** — one `<Flows.ServiceAPIMethod>` per service action.
  Attrs: `Key`, `Name`, `Public`, `Folder` (→ `ModelFolder:/Folders.<key>`),
  `GeneralHash`, `DebuggerHash`, … Children: `Image`, `TextResources`,
  `NodesShownInESpaceTree`, `NodesNotShownInESpaceTree HasChildren="Yes">`,
  `Metadata`, `LocalVariables`, `InputParameters`, `OutputParameters`.
- **`UserActions`** — `<Flows.UserAction>` / `<Flows.UserActionInsideFolder>`
  (server actions). Same child shape as ServiceAPIMethod.
- **`NodesNotShownInESpaceTree#<ownerKey>`** — the flow graph for one action:
  `<Nodes.Start>`, `<Nodes.End>`, `<Nodes.If>`, `<Nodes.Assign>`,
  `<Nodes.Comment>`, `<Nodes.ErrorHandler>`, … and `<Links.Sequence>` /
  `<Links.True>` / `<Links.False>` with
  `TargetNode="Nodes.<Type>:^/^/NodesNotShownInESpaceTree.<targetNodeKey>"`.
  **References use the target node's key**, not the action key. The fragment is
  named by the owner action's key; internal links reference node keys.
- **`References`** — `<Reference>` entries (dependencies). `Count` on the root.
- **`Signature`** — per-element signature hashes (regenerated automatically).
- Other: `Structures`, `SiteProperties`, `ClientVariables`, `AnonymousStructures`,
  `ListTypes`, `NodesShownInESpaceTree#<key>`, `Widgets#<key>`, …

## 5. ObjectKeys

- 22-char base64 of 16 random bytes, no `=` padding, `/` → `_`.
- Charset observed: `A–Z a–z 0–9 + _` (no `/`).
- Must be **globally unique** within the module. Random 128-bit is collision-safe.

## 6. Cloning an element with a flow (key remapping)

When cloning an action that owns a `NodesNotShownInESpaceTree#<key>` flow
fragment:

1. Give the cloned `Flows.ServiceAPIMethod` a **new** `Key`.
2. Create the flow fragment under the **new** name
   `NodesNotShownInESpaceTree#<newKey>`.
3. Inside the cloned flow XML, **regenerate every node/link `Key`** (collect all
   `Key` attrs, map old→new) and string-replace old→new — this fixes both
   `Key="…"` attrs **and** `TargetNode="…<targetNodeKey>"` references. (Keys are
   unique 22-char tokens; safe to replace.)
4. Keep the flow's node structure otherwise (Start→End, comments, …).

The `create_service_action` tool does this automatically when cloning.

## 7. Proven operations

| Operation | How | Verified |
|---|---|---|
| Add element to existing fragment | append child, bump `Count`, `Write`, regen | ✓ service action, Reference |
| Create a new fragment | `GetFragmentXmlWriter(newName).Write(xe)` | ✓ flow fragment |
| Clone element + flow | new key + remap flow node keys + create flow fragment | ✓ `TestHeadlessSvc` in Diet_BL |
| From-scratch element + fragment | set eSpace `HasChildren="Yes"`, create fragment with new element | ✓ `mksvc` |
| Round-trip / regen | `SetNeedsSignatureRegeneration` + `GetBytes` | ✓ `IsValidOml=True` |

## 8. Roadmap (same pattern, not yet tooled/verified)

- **Entity**: add `<Entity>` to the entities collection + an attribute fragment;
  default `Id : LongIdentifier`.
- **Attribute**: append `<EntityAttribute>` + regen.
- **Structure**: add to `Structures` + attributes.
- **Delete element**: `DeleteFragment` + remove owner reference + regen.
- **CRUD wrappers**: compose entity + service-action + flow-node editing.

> **Live alternative for dependencies:** the live editor's `live_consume_elements`
> tool handles dependency management (consuming all 15 element types from a producer)
> via `IESpace.AddDependency<T,S>` inside `Command.ExecuteFromAsyncCode` — no `.oml`
> editing needed. Proven: 24 elements consumed from `Diet_CS` into `Diet_BL`. See the
> `managing-dependencies` + `live-editing` skills. The headless `add_dependency`
> (above) is the fallback for when SS isn't running.

## 9. Gotchas

- `Writer.Write` normalizes the fragment root's `Count` attribute (drops it) —
  benign; SS recomputes; `IsValidOml` still `True`.
- Reading a missing fragment throws `MissingFragmentException`; always
  `HasFragment` first.
- The on-disk `.oml` reflects the last **Save** in SS, not unsaved edits.
- SS locks the source `.oml` while the module is open — write to a new file.
- The editor edits **files**, not the live in-memory module. Workflow:
  **Save → edit .oml → reload in SS**. **Live in-memory editing is now proven**
  (not future) via the `outsystems-liveeditor` MCP + OsLiveBridge plugin — see the
  `live-editing` skill. Use live when the module is open (instant tree update, no
  reload); use headless when SS isn't running or you need a saved `.oml` artifact.
