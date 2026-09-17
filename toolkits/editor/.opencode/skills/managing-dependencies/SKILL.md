---
name: managing-dependencies
description: Use when the user wants to add a module dependency (consume a producer module's elements) into an OutSystems module. Triggers on "add dependency", "consume module", "consume elements", "reference another module", "add reference", "manage dependencies". TWO approaches: LIVE (preferred — both modules open in SS, instant tree update, single undo unit) and HEADLESS (.oml file edit, no SS running). Live proven on SS 11.55.81 via live_consume_elements (24 elements consumed from Diet_CS into Diet_BL). Read live-editing first for the live approach; editor-workflow for headless.
---

# Managing Dependencies

Consume elements from a producer module into a consumer module — the equivalent
of SS's "Manage Dependencies" dialog, but headless/live. Covers **all 15 consumable
element types**: ServiceActions, ServerActions, ClientActions, Entities, Structures,
Roles, Processes, Scripts, Images, Resources, WebThemes, MobileThemes, WebFlows,
MobileFlows, Folders.

## TWO approaches — LIVE FIRST

### Live (PREFERRED — both modules open in SS)

**Use when:** both the consumer and producer modules are OPEN in Service Studio.
No Save needed, no reload, instant tree update, single undo unit (`Ctrl+Z`).

**Tools** (from `outsystems-liveeditor` MCP):
- `live_list_consumable_elements(module)` — enumerate all public consumable elements
  in the producer, grouped by type. Read-only.
- `live_consume_elements(consumer, producer, what)` — consume elements. `what="*"`
  for all; `"ServerAction:*,Entity:*"` for all of specific types;
  `"ServerAction:GetUser,Entity:User"` for specific elements.

**Mechanism:** `IESpace.AddDependency<T,S>(IShareable<T,S>)` inside
`Command.ExecuteFromAsyncCode`. The generic method is resolved per-element via
reflection (type args extracted from the source object's `IShareable<T,S>` interface).
All consumptions in one command = one undo unit. Proven: 24 elements consumed from
`Diet_CS` into `Diet_BL` (16 server actions + 4 entities + 4 folders, 0 errors).

**Read `live-editing` first** — it covers the command system, PresenterContext, and
the bridge architecture.

```
live_list_consumable_elements(Diet_CS)                              # 1. see what's available
live_consume_elements(diet_BL, Diet_CS, "*")                        # 2. consume everything
live_consume_elements(diet_BL, Diet_CS, "ServerAction:*,Entity:*")  # 3. or just specific types
```

### Headless (fallback — SS not running / module not open)

**Use when:** SS isn't running or the modules aren't open. Requires `.oml` files on
disk. Save → edit → reload workflow.

**Read `editor-workflow` first** — Save → edit → reload workflow + mechanism.

## Headless: what a dependency is in the .oml
- A `<Reference>` element inside the **`References`** fragment.
- Key attributes: `Name` (producer module name), `Key`, `ReferenceKey`,
  `eSpaceKey` (the producer's key), `LastModifiedDate`, plus child collections
  (`<ReferenceActions>`, `<ReferenceEntities>`, …) listing the consumed elements.
- The fragment root `<eSpaceFragment Count="N">` counts the references.
- Adding a reference = append the `<Reference>` + bump `Count` + regen signatures.

## Prerequisites
- The consumer `.oml` is on disk (user **Save** in SS first).
- The **`<Reference>` XML** for the producer. Obtain it from a module that
  already consumes that producer:
  `get_fragment(omlPath=<any-consumer>.oml, fragment="References")` → copy the
  `<Reference Name="<Producer>">…</Reference>` block. (The reference carries the
  producer's `eSpaceKey`/`ReferenceKey` + the consumed-element list; reuse it
  verbatim.)
- If no existing consumer is available, you must construct the `<Reference>` from
  the producer's header (`parse_oml_header` gives `Name` + `eSpaceKey`) — the
  consumed-element collections can start empty and be populated in SS after reload.

## Add (one tool call)

`add_dependency(omlPath, outOml, referenceXml)`

- `referenceXml` = the full `<Reference …>…</Reference>` element (string).
- The tool appends it to the `References` fragment, bumps `Count`, regenerates
  signatures, writes `outOml`, and verifies the reference is present after reload
  (`IsValidOml=True` + read-back).

## Verify
- Tool output: `OK: reference '<Name>' present`.
- Offline: `get_fragment(outOml, "References")` lists the new reference.
- In SS: open `outOml` → the producer appears under the module's dependencies;
  open Manage Dependencies to pick the specific elements to consume.

## After adding
- **Reload in SS** (close + open `outOml`).
- The `<Reference>` lists the consumed elements (actions/entities) you copied. To
  consume **different** elements, open Manage Dependencies in SS and tick them
  (or edit the `<ReferenceActions>`/`<ReferenceEntities>` children before writing
  — same fragment-edit pattern).

## Gotchas
- **Producer must be published** before SS can fully resolve the reference (the
  `eSpaceKey` must match a deployed module). The `.oml` edit itself doesn't
  require this, but opening in SS may warn if the producer isn't on the server.
- **Count attr**: the writer normalizes the fragment root's `Count` (benign — SS
  recomputes; `IsValidOml` still passes).
- **File lock**: write to a new `outOml`.
- **Save before edit**: the `.oml` reflects the last Save.

## Strong vs weak dependencies (platform semantics)

Condensed from the official O11 doc
`docs-product\src\building-apps\reuse-and-refactor\strong-weak-dependencies.md`
(OutSystems docs, CC BY-NC-ND 4.0). The dependency's strength is decided by
**what element types** the consumer reuses from that producer:

| Dependency | Consumer reuses... | Runtime needs | Producer change effect |
|---|---|---|---|
| **Strong** (tight) | Server actions, Client actions, Blocks, Images, Resources, Scripts, Themes, Roles, Processes, Process activities | Signature **+ implementation** | Any signature OR implementation change → consumer **outdated** → republish required |
| **Weak** (loose) | Screens, **Service actions**, Database entities, Local storage entities, Static entities, Structures | Signature only | Logic elements run in the producer's request (impl changes take effect immediately); entity/structure changes hit the DB immediately; consumer does **not** go outdated on impl-only changes |

Rules that decide the strength when multiple elements are consumed:
- **One strong-listed element ⇒ the whole dependency to that producer is strong.**
- The dependency stays weak only if **all** consumed elements are weak-listed.

Agent implications (directly relevant to our consume tooling):
- Our proven `Diet_CS` consumption (server actions + entities) creates a
  **strong** dependency — the consumer must be republished after any producer
  change. Expect the `Outdated producer/consumer` warnings from the
  `editor-workflow` validity checklist and refresh references after producer
  publishes.
- If a wrapper layer only needs *callable logic without tight coupling*,
  **service actions** (weak) are the coupling-friendly choice vs server actions
  (strong). Prefer exposing service actions on producers whose consumers should
  not be republished on every producer change.
- Entities consumed for aggregates/CRUD keep queries in the **consumer's
  transaction** (weak) — data-model changes are immediate at the DB level, but
  signature-level changes (new attribute) still need the consumer refreshed.

## Roadmap
- A higher-level `add_dependency_by_name(producerName)` that auto-finds the
  producer's `eSpaceKey` (via `parse_oml_header` on the producer's `.oml`) and
  constructs a minimal `<Reference>` — so the caller doesn't need to source the
  XML from an existing consumer.
- `live_remove_dependency` — `IESpace.RemoveUnusedDependencies` or targeted removal
  (live, inside a command).
