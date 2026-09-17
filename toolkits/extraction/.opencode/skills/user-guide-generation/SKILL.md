---
name: user-guide-generation
description: Use when the user asks for a "user guide", "user documentation", "how users use the app", "business processes", "user walkthrough", "end-user documentation", "user manual", "click-by-click guide", or "process documentation" for an OutSystems application. Produces a single app-level, plain-language, process-centric document (docs/USER_GUIDE.md) describing what users can do and how to do it, step by step. This is the user-facing counterpart to the documentation-generation skill (which produces the technical reference). Triggered by any request for non-technical, process-oriented, or end-user documentation.
---

# OutSystems user guide generation

This skill generates a **plain-language, end-user, process-centric** guide for an
OutSystems application. It produces a **single app-level document** at
`docs/USER_GUIDE.md` covering cross-module end-to-end user journeys (e.g. "submit
an order on the mobile app → admin approves it on the back office → user sees
confirmation").

It is the **user-facing counterpart** to `documentation-generation`:
- `documentation-generation` → technical reference (entities, actions, SQL, flows)
- `user-guide-generation` → what users do and how (clicks, tasks, confirmations)

**Audience:** non-technical end users. **Language:** plain. **No jargon.**

## Consumer model (important)

This skill is designed to run in a **consuming project** (e.g. ArkkiAutomation,
KopaAutomation), not in the baseline toolkit repo. The skill file itself lives in
the baseline (`.opencode/skills/user-guide-generation/SKILL.md`) and is
auto-available to every linked consumer via that consumer's `opencode.json`
`skills.paths` (which points at the baseline's `.opencode/skills`).

When the skill runs, all paths are **relative to the current workspace root**
(the consumer project where opencode was launched):
- Reads `docs/<ModuleName>/` extraction files from the consumer's `docs/`.
- Reads `OMLs/<Module>.oml` from the consumer's `OMLs/` (for staleness only).
- Writes `docs/USER_GUIDE.md` to the consumer's `docs/` (git-tracked, unlike
  `docs/*/` module folders which are gitignored).

No Service Studio process is required — this is a **docs-first** skill that reads
already-extracted output produced by `documentation-generation` Mode 1.

## When to use

The user wants a non-technical, process-oriented write-up of how people use the app.
Trigger examples:
- "generate a user guide" / "user documentation" / "user manual"
- "how do users use the app" / "business processes" / "user walkthrough"
- "end-user documentation" / "click-by-click guide" / "process documentation"

If the user wants technical reference (entities, actions, flows, SQL), use the
`documentation-generation` skill instead. If they want raw extraction (screen
list, theme CSS), use `ui-extraction` / `module-extraction`. This skill is for
**synthesis** of a user-facing guide.

---

## CRITICAL FAILURE MODES (read before writing any doc)

### FAILURE MODE #1 — Fabricating user steps
Every "click X" / "fill in Y" / "you see Z" MUST trace to a real widget, field,
or message in the extraction output (`ui-tree-*.txt`, `client-actions-*.json`). If
the UI tree has a `SaveButton (Button) [OnClick=SaveData]`, you may write "Click
Save to confirm". If there is no such widget, do NOT invent one. Invented steps
are the worst failure mode — they mislead end users.

### FAILURE MODE #2 — Fabricating business intent
If a screen's name/description/URL does not reveal its purpose, write
**"Purpose not inferable from extraction — verify manually."** inline. Never
guess what a screen is for. Screens named `Common` / `Admin` / `Test123` are
candidates for this gap marker.

### FAILURE MODE #3 — Cross-module stitching errors
Do NOT claim "screen A in module X navigates to screen B in module Y" unless
there is evidence: a `WebDestination` node naming B, or a URL link. Cross-module
navigation in OutSystems often happens via external links / consumed REST that
the extractors do not fully capture. Mark inferred cross-module flows explicitly:
"users then move to the <Module> app to continue (navigation inferred, not
directly extracted)".

### FAILURE MODE #4 — Technical jargon leaking in
BANNED in user-facing text: client action, server action, service action,
WebDestination, widget, entity, eSpace, aggregate, REST, SOAP, site property,
structure, flow, node, JSON, PID, ClrMD. Translate everything:
- "WebDestination to OrderDetail" → "you are taken to the Order detail screen"
- "FeedbackMessage 'Saved'" → "you see a 'Saved' confirmation"
- "ExecuteAction SaveOrder" → "your order is saved"
- "widget SaveButton" → "the Save button"

### FAILURE MODE #5 — Building from stale docs
This skill reads already-extracted `docs/<ModuleName>/` files. If the module
changed in Service Studio but `documentation-generation` was not re-run, the user
guide is silently built from stale data. ALWAYS run the staleness check (Step 3)
first. If a module's `.oml` is newer than its docs folder, warn the user and
either re-run `documentation-generation` for that module or proceed with an
explicit staleness note in the guide.

### FAILURE MODE #6 — Missing prerequisite docs
If `docs/<ModuleName>/screens.json` does not exist for any module, the guide
cannot be built. Do NOT fabricate screens from memory. Halt and instruct the user
to run `documentation-generation` (Mode 1) first.

### FAILURE MODE #7 — Delegating to task agents
DO NOT use the Task tool to write `USER_GUIDE.md`. Task agents summarize and
invent steps to fit their context window. Write the file YOURSELF using the
Write tool, reading extraction output directly.

### FAILURE MODE #8 — Skipping a user-facing module
An app-level guide must cover ALL modules with screens. Skipping one breaks
end-to-end journeys (e.g. missing the admin module makes "submit → approve"
incomplete). Discover UI modules by checking every `docs/*/` folder for
`screens.json`.

### FAILURE MODE #9 — Inherited gaps with no recovery
If the source `TECHNICAL_DOCUMENTATION.md` for a module notes "Flow traces not
captured" (a known `get_module_report` heap-walk limitation), the user guide
inherits that gap. There is no live-extraction fallback in this docs-first skill.
Note the gap explicitly in the guide rather than papering over it.

---

## Output structure

```
docs/
├── USER_GUIDE.md              ← single app-level user guide (this skill's output)
├── TECHNICAL_DOCUMENTATION.md ← (may exist) app-level technical doc, from Mode 2
└── <ModuleName>/              ← per-module extraction files (source material)
    ├── screens.json
    ├── ui-tree-screens.txt
    ├── ui-tree-blocks.txt
    ├── client-actions-screens.json
    ├── client-actions-blocks.json
    └── web-blocks.json
```

One file (`docs/USER_GUIDE.md`) for the whole application. The per-module
extraction files are the **source material**, not the deliverable. The guide
sits alongside `docs/TECHNICAL_DOCUMENTATION.md` (the app-level technical doc
from `documentation-generation` Mode 2) as a complementary, user-facing view.

## Workflow (do these in order)

### Step 1 — Prerequisite check: extraction docs must exist (MANDATORY)
Enumerate `docs/*/` folders in the current workspace. A folder counts as a UI
module if it contains `screens.json`.

- If NO folder has `screens.json` → **HALT**. Tell the user:
  "No extraction docs found under `docs/<ModuleName>/`. This skill reads
  already-extracted output. Run `documentation-generation` (Mode 1) first to
  populate `docs/<ModuleName>/` with screens.json, ui-tree, and client-actions,
  then ask me to generate the user guide."
- Logic-only modules (folders with only `TECHNICAL_DOCUMENTATION.md`, no
  `screens.json`) are NOT skipped entirely — they may be referenced in
  cross-module processes — but they contribute no screen/widget material.

### Step 2 — Discover modules and read source material
For each `docs/<ModuleName>/` folder that has `screens.json` (UI module), read:
- `screens.json` → screen name, URL, `permissions` (roles), description, events
  (OnReady = what loads on entry).
- `ui-tree-screens.txt` / `ui-tree-blocks.txt` → clickable widgets with their
  OnClick handlers (the buttons/links users actually press) and visible widget
  names.
- `client-actions-screens.json` / `client-actions-blocks.json` → what happens on
  click: `WebDestination` (where the user is taken), `FeedbackMessage`
  (confirmation/error shown to user), `ExecuteAction` (data saved — translate to
  plain language).
- `web-blocks.json` → reusable components (may appear across screens).

Optionally read `TECHNICAL_DOCUMENTATION.md` (per-module) for its Module Overview
(description context) only — NEVER re-summarize the `.md`; always read the raw
`.json`/`.txt` files as the source of truth.

Large files: use the Read tool with offset/limit to read in chunks. Do NOT create
intermediate files.

### Step 3 — Staleness check (MANDATORY)
For each UI module, compare the module's `.oml` against its docs folder:

1. Look for `OMLs/<ModuleName>.oml` (or a matching `.oml` elsewhere in the
   workspace). If no `.oml` is found, skip the staleness check for that module
   with a note ("no .oml available; docs freshness not verified").
2. If a `.oml` is found, get its saved time:
   - **Primary:** call `parse_oml_header(path=<path-to-oml>)` (an
     `outsystems-tools` MCP tool — available in consumers via opencode.json). It
     returns `savedTime` (authoritative).
   - **Fallback:** if the `outsystems-tools` MCP server is inactive (check
     `/mcp`) or the call fails, use the `.oml` file's filesystem mtime instead
     (`(Get-Item <path>).LastWriteTime`) — an approximation, no exe needed.
3. Compare to the docs folder's newest file mtime
   (`(Get-ChildItem docs\<ModuleName> -Recurse -File | Measure-Object LastWriteTime -Maximum).Maximum`).
4. If the `.oml` is NEWER than the docs folder → **WARN**: "docs for <Module>
   may be stale (module saved <date>, docs extracted <date>). Re-run
   `documentation-generation` for this module, or proceed with a staleness note
   in the guide."

Interpretation:
- docs newer than .oml → OK (extraction happened after the last save).
- .oml newer than docs → STALE (module changed since last extraction).

### Step 4 — Synthesize the user journeys

#### 4a — Build the role/access map
From `permissions` in each `screens.json` (across all UI modules), list every
role and which screens it can access. Translate role names to plain-language
user types where possible (e.g. `BackOfficeUser` → "back-office staff"). If a
role name is opaque, keep it verbatim and mark "role name not descriptive".

#### 4b — Group screens into user processes
Infer processes from screen names, URLs, and on-screen navigation chains
(`WebDestination` nodes within a module link screens into a flow). A process is
a coherent user goal, e.g. "Create and Submit an Order", "Approve a Request",
"View Order History". Cross-module processes (order submitted in app A,
approved in app B) are stitched by inferred hand-off — mark as inferred
(FAILURE MODE #3).

#### 4c — Write each process as a plain-language walkthrough
For each process, using real widgets from the UI tree and real outcomes from
client action flows:
- **Goal:** what the user achieves (plain language).
- **Who can do this:** user type (from 4a).
- **Steps:** numbered, click-by-click. Each "click X" traces to a real widget in
  `ui-tree-*.txt`. Each "you see Y" / "you land on Z" traces to a
  `FeedbackMessage` / `WebDestination` in `client-actions-*.json`. Translate
  every technical term to user language (FAILURE MODE #4).

#### 4d — Build the Screen Quick Reference
One row per user-facing screen: actual screen name, plain-language purpose, main
actions (buttons/links available). This is the only place real screen names
appear — for support/traceability.

### Step 5 — Write docs/USER_GUIDE.md (yourself, no task agents)
Use the **fixed 5-section template** below. Write it with the Write tool, reading
extraction output directly. Do NOT create intermediate files. Do NOT use the
Task tool.

### Step 6 — Verify (MANDATORY)
```powershell
# Must return exactly 5 H2 headings, in this exact order:
#  1. About This App
#  2. Who Uses This App
#  3. Getting Started
#  4. Tasks You Can Do
#  5. Screen Quick Reference
Select-String -Path "docs\USER_GUIDE.md" -Pattern '^## ' | Select-Object -ExpandProperty Line
```
- Count MUST be exactly 5.
- Order and names MUST match the list above verbatim.
- Spot-check: pick 3 "click X" statements and confirm the widget exists in the
  relevant `ui-tree-*.txt`. If any is fabricated, rewrite.
- If any check fails, rewrite — do not hand the user a broken guide.

---

## Fixed template (MANDATORY 5-section structure)

**HARD RULES (non-negotiable):**
- Use EXACTLY these 5 section headings, in EXACTLY this order.
- Do NOT add, remove, rename, merge, split, or reorder sections.
- Do NOT change heading levels — every numbered section is a `## H2`.
- If a section has no data, KEEP the heading and write `(none found in
  extraction)` beneath it. Never drop a section.
- Only the content varies per app — the skeleton is identical for every guide.

```markdown
# User Guide: <Application Name>

> A plain-language guide to what users can do in <App> and how to do it.
> Generated <date> from extracted screen and UI data across <N> modules.
> Note: button/field names come from the app's internal naming and may differ
> slightly from the labels you see on screen.

## 1. About This App
- **What it is:** <2-3 plain sentences>
- **The main things users do here:**
  - <bullet per high-level task>

## 2. Who Uses This App
| User type | What they do in the app |
|-----------|--------------------------|
| <plain-language role> | <plain-language description> |

(From screen permissions across all modules. Translate role names to plain
language where possible; keep opaque names verbatim with a note.)

## 3. Getting Started
- **How to reach the app / log in:** <login flow is NOT extracted — write
  "Login flow not extracted — verify manually.">
- **Main areas and how to move around:** <plain-language navigation overview>

## 4. Tasks You Can Do

### 4.1 <Process name>
**Goal:** <what the user achieves, plain language>
**Who can do this:** <user type>
**Steps:**
1. Go to the <Screen> screen.
2. Click <button> to <action>.
3. Fill in <field>.
4. Click <button> to confirm.
5. You'll see <confirmation> / land on <next screen>.

### 4.2 <Process name>
...

## 5. Screen Quick Reference
| Screen | What it's for | Main actions |
|--------|---------------|--------------|
| <actual screen name> | <plain-language purpose> | <buttons/links available> |
```

### Rules for the template
- Use REAL widget/screen names from extraction — never invent.
- Every "click X" must trace to a widget in `ui-tree-*.txt`.
- Every "you see Y" / "you land on Z" must trace to a `FeedbackMessage` /
  `WebDestination` in `client-actions-*.json`.
- If a screen/process purpose can't be inferred, write
  "Purpose not inferable from extraction — verify manually." inline — do not
  guess.
- Translate all technical terms to user language (see FAILURE MODE #4).
- If a section has no data, write "(none found in extraction)".
- Do NOT use the Task tool to write the doc.

---

## Honesty about extraction gaps

This docs-first skill inherits whatever `documentation-generation` captured, plus
its own limits. The extractors do NOT capture:
- **Login / authentication flow** — not extracted. State
  "Login flow not extracted — verify manually." in Section 3.
- **Exact visible button labels** — extraction has developer widget names (e.g.
  `SaveButton`), often close to the visible label but not identical. The note at
  the top of the guide (in the blockquote) states this caveat once.
- **Cross-module navigation links** — inferred from screen names/URLs, not
  directly extracted. Mark inferred hand-offs explicitly.
- **Mobile gestures / device features** (camera, GPS, push) — not captured.
- **Business meaning of a screen** — inferred from names; mark unverifiable.
- **Runtime data / real user content** — not captured (and must not be
  fabricated).
- **Stale docs** — if the module changed since the last extraction, the guide
  reflects the old state. The staleness check (Step 3) mitigates but cannot
  prevent this; the user must re-run `documentation-generation` to refresh.
- **Inherited extraction gaps** — if Mode 1 noted "Flow traces not captured" for
  a module, the user guide cannot recover that without re-running live
  extraction.

Do NOT fabricate steps, labels, or outcomes. If something wasn't extracted, say
so explicitly. An honest gap is better than a confident lie that misleads an end
user.

## Official docs corpus (optional, for plain-language accuracy)

When describing generic platform behavior (what a login screen does, how
approval lists work in general terms, what a "site property" or "timer" is in
plain words), ground the wording in OutSystems' official documentation if the
ground-of-truth repo is configured locally:

1. Read `references/source-path.txt` (one line: absolute path to the repo).
   If missing, skip silently.
2. If present, useful sources:
   - `<path>\docs-product\src\getting-started\` — end-user / first-time concepts
   - `<path>\docs-product\src\building-apps\` — how apps behave (UI, logic,
     screens) explained for builders; useful to translate features into
     plain language without inventing behavior.
3. Use it to verify what a platform feature does before telling end users;
   never copy builder jargon into the guide (FAILURE MODE #4 still applies).

Scope guard: prefer O11-relevant pages; ignore `docs-odc\` and
`migration-to-odc` / `extending-with-odc` content. Grep for terms; do not read
entire folders.

## Relationship to other skills
- **Hard prerequisite:** `documentation-generation` Mode 1 must have been run in
  the same workspace, producing `docs/<ModuleName>/` with `screens.json`,
  `ui-tree-*.txt`, and `client-actions-*.json`. Without it, this skill halts
  (Step 1).
- This skill does NOT call `ui-extraction` tools directly — it reads their
  output files.
- The only MCP tool this skill may call is `parse_oml_header` (for staleness),
  with a filesystem-mtime fallback if that tool is unavailable.
- Complementary to `documentation-generation` Mode 2 (app-level technical doc):
  `docs/TECHNICAL_DOCUMENTATION.md` (technical) + `docs/USER_GUIDE.md`
  (user-facing) together cover both audiences.

## Reference
- Extraction internals: `docs/project_map.md`
- Multi-module rules: `multi-module-extraction` skill + `AGENTS.md`
- Sibling skill: `documentation-generation`
- Consumer model: `AGENTS.md` "Linked consumer projects" section
- Tool reference: `outsystems-ui`, `outsystems-logic`, `outsystems-tools` skills
- Ground-of-truth setup: `references/README.md`
