---
name: html-docs-generation
description: Use when the user asks to "generate an interactive site", "make HTML documentation", "create a website that explains the app", "build a docs site", "turn the docs into a website", "interactive documentation", or wants rich HTML pages (instead of PDF/PPTX slides) from the project's documentation. Produces a STANDARD 4-page static HTML site (Overview, Technical, Integrations, User Guide) with sidebar nav, search, dark mode, live Mermaid diagrams, and an interactive module graph in docs/site/, rendered by running the baseline script Render-Site.ps1 by absolute path â€” NEVER call a static-site generator, NEVER install Node/Astro/VitePress/Docusaurus, and NEVER improvise a build command. The site is pure vanilla HTML/CSS/JS with vendored mermaid.min.js; no Node, no Python, no Docker. Mermaid diagrams are written as <div class="mermaid">â€¦</div> blocks and render client-side. The interactive module graph is authored as a JSON data block. The 4-page structure and per-page section skeletons are MANDATORY (hard rules, fixed headings, fixed order).
---

# HTML documentation site (vanilla HTML/CSS/JS â†’ static site)

This skill turns a project's existing documentation into an **interactive,
multi-page HTML documentation site** â€” sidebar navigation, client-side search,
dark/light theme, live Mermaid diagrams, and a clickable module graph â€” then
assembled by the DocumentGenerationHTML baseline toolkit.

It is the **interactive counterpart** to `documentation-generation` and
`user-guide-generation`, and the successor to the Marp-based
`presentation-generation`:
- `documentation-generation` â†’ `docs/TECHNICAL_DOCUMENTATION.md` (technical reference)
- `user-guide-generation` â†’ `docs/USER_GUIDE.md` (user-facing guide)
- `html-docs-generation` â†’ `docs/site-src/*.html` + `site.json` â†’ `docs/site/` (interactive site)

**The site is always 4 pages:** Overview, Technical, Integrations, User Guide.
The per-page section skeletons are mandatory (hard rules â€” see Steps 5â€“8).

---

## TL;DR (if you read nothing else, read this)

1. Read `docs/TECHNICAL_DOCUMENTATION.md` + `docs/USER_GUIDE.md` in the current workspace.
2. Write content fragments + a manifest under `docs/site-src/`:
   - `docs/site-src/site.json`
   - `docs/site-src/index.html` (Overview / landing page content)
   - `docs/site-src/technical.html` (Technical page content)
   - `docs/site-src/integrations.html` (Integrations page content)
   - `docs/site-src/user-guide.html` (User guide page content)
3. Run **exactly** this one command â€” nothing else â€” to assemble:

```powershell
& 'C:\Users\Ricardo Figueiredo\Documents\Outsystems_Custom\toolkits\html-docs\scripts\Render-Site.ps1' -Project '<current workspace root>' -Clean
```

That is the entire skill. Everything below is detail and guardrails.

---

## THE ONLY BUILD COMMAND (do not improvise)

**There is exactly one valid way to assemble the site:** run `Render-Site.ps1`
by the absolute path above. It already handles everything:
- reads `docs/site-src/site.json` (manifest: site title + page list);
- wraps each content fragment in `templates/base.html` (sidebar, search, TOC,
  theme toggle, script tags);
- copies the shared assets (`site.css`, `site.js`, `module-graph.js`, the
  vendored `mermaid.min.js`) into `docs/site/assets/`;
- verifies every output HTML is non-empty and references the assets;
- prints a SUMMARY table and tells you the path to open in a browser.

You do NOT run any other build step. The site is **pure static HTML** â€” no
Node, no Python, no Docker, no npm, no bundler. You run the one command and
read its SUMMARY table.

If the command fails, it prints the offending file/line. **Read it, fix the
`site-src/` source (usually a missing fragment or malformed `site.json`), and
re-run the same command.** Do NOT switch to a different method.

### DO NOT (these are all wrong, even if they look plausible)

- **DO NOT install Node, npm, Astro, VitePress, Docusaurus, Eleventy, or any static-site generator.** The baseline deliberately uses vanilla HTML/CSS/JS so there is no build toolchain per consumer. Mermaid renders client-side from the vendored `mermaid.min.js`.
- **DO NOT run `npx`, `npm run build`, `vite build`, `astro build`, or any bundler.** There is nothing to bundle. `Render-Site.ps1` only does string-template wrapping + asset copying.
- **DO NOT use Docker.** There is no container, no server runtime. Output is static files.
- **DO NOT pre-render Mermaid to SVG.** Mermaid runs in the browser. Write `<div class="mermaid">flowchart LR ... </div>` directly in the fragment; `site.js` initializes and re-renders it on theme toggle.
- **DO NOT modify, move, or copy** the baseline templates (`base.html`, `site.css`, `site.js`, `module-graph.js`) or `assets/mermaid.min.js`. Reference them only by absolute path; the orchestrator copies them.
- **DO NOT write full HTML documents** as content fragments. A fragment is the **inner HTML of `<main>`** â€” headings, paragraphs, cards, diagrams â€” NOT a `<html>/<head>/<body>` shell. The orchestrator wraps every fragment in `base.html`. Writing a full document produces nested `<html>` and broken styles.
- **DO NOT "regenerate" or "build" by any other path** â€” no VS Code Live Server extension as a build step, no online converter, no `python -m http.server` (that command only *serves*, it does not *build*).
- **DO NOT drop or rename any of the 4 standard pages.** The site is always Overview + Technical + Integrations + User Guide. If a page's source has no data, generate the page with "(none found in extraction)" â€” never omit it.

If you are about to type `npm`, `npx`, `vite`, `astro`, or `docker`:
**STOP. You have not followed this skill. Re-read this section.**

---

## Prerequisites the baseline already handles (do NOT verify or install)

| Component | Status | Who handles it |
|-----------|--------|----------------|
| HTML/CSS/JS chrome | Vendored in `templates/` | Copied by the orchestrator |
| Mermaid v11 | Vendored at `assets/mermaid.min.js` | Copied by the orchestrator; renders client-side |
| Browser | Any modern browser (Edge/Chrome/Firefox) | The end user opens `index.html` |
| Node / Python / Docker | **Not required at all** | N/A |

There are **no prerequisites to install**. If something is missing, the
orchestrator will report a missing baseline file (a baseline install issue,
not a consumer issue).

---

## Consumer model (important)

This skill runs in a **consuming project** (e.g. ArkkiAutomation). The skill
file lives in the DocumentGenerationHTML baseline
(`C:\Users\Ricardo Figueiredo\Documents\Outsystems_Custom\toolkits\html-docs\.opencode\skills\`)
and is made available to the consumer via the consumer's `opencode.json` â†’
`skills.paths` (add the baseline path alongside any other skill paths).

When the skill runs, all paths are **relative to the current workspace root**
(the consumer project where opencode was launched):
- Reads `docs/TECHNICAL_DOCUMENTATION.md` and `docs/USER_GUIDE.md` from the consumer.
- Writes `docs/site-src/site.json` + `docs/site-src/*.html` (content fragments).
- Assembles by running `Render-Site.ps1` (absolute path above) with
  `-Project <workspace root>`. Output HTML + assets land in `docs/site/`.

## When to use

The user wants an **interactive HTML website** from the project's
documentation. Trigger examples:
- "generate an interactive site that explains the app"
- "create HTML documentation from the docs"
- "make a website for the application"
- "turn the technical docs into a browsable site"
- "interactive documentation"
- "build a docs site"

If the user wants **PDF/PowerPoint slides specifically**, use
`presentation-generation` (the Marp baseline) instead. This skill is for
**interactive HTML pages**.

---

## CRITICAL FAILURE MODES (read before authoring)

### FAILURE MODE #0 â€” Improvising a build command (the most common failure)

You write the `site-src/` files; **`Render-Site.ps1` assembles them.** That is
the only division of labor. A weaker model will be tempted to "help" by
installing Astro/VitePress, running `npm run build`, or hand-copying the
templates into the consumer. All of these are wrong. If you ever feel the urge
to type `npm`, `npx`, `vite`, `astro`, `docker`, or to "copy the baseline
templates into the consumer repo", **stop and re-read "THE ONLY BUILD COMMAND"
above.**

Specifically:
- There is **no build toolchain**. Vanilla HTML/CSS/JS renders client-side.
- Mermaid renders **in the browser** from the vendored bundle â€” no Python
  pre-render, no SVG files on disk.
- The orchestrator's only job is template-wrapping fragments + copying assets.
  Skip that and the pages have no sidebar, search, TOC, theme toggle, or
  mermaid init.

### FAILURE MODE #1 â€” Fabricating content not in the source docs

Every heading, bullet, fact, table row, diagram node, and module-graph entry
MUST trace to `docs/TECHNICAL_DOCUMENTATION.md` or `docs/USER_GUIDE.md`. Do
NOT invent modules, entities, screens, or steps. If the docs don't cover
something, omit it or mark "verify manually".

### FAILURE MODE #2 â€” Inventing Mermaid nodes/edges or module-graph data

Mermaid diagrams and the interactive module graph are the highest-risk
fabrication surfaces. Every node, edge, label, dependency, and "exposes"
entry must come from the source docs (e.g. the module interaction map in
TECHNICAL_DOCUMENTATION.md, the entity list, the 4-layer canvas). Do NOT add
edges or dependencies "because they look right". If the docs only describe
Aâ†’B, draw only Aâ†’B.

### FAILURE MODE #3 â€” Writing full HTML documents as fragments

A content fragment is the **inner HTML of `<main>`**. It must NOT contain
`<!DOCTYPE>`, `<html>`, `<head>`, or `<body>`. Start with an `<h1>` (or a
`.hero` block on the landing page) and write content. The orchestrator wraps
every fragment in `base.html`, which supplies the shell, sidebar, search, TOC,
theme toggle, and script tags. A fragment with its own `<head>` produces
nested documents and broken interactivity.

### FAILURE MODE #4 â€” Delegating to task agents

DO NOT use the Task tool to author the fragments or `site.json`. Task agents
summarize and drop detail to fit their context. Write each file YOURSELF with
the Write tool, reading the source docs directly.

### FAILURE MODE #5 â€” Skipping the build step

Authoring `site-src/` is only half the job. You MUST run `Render-Site.ps1`
afterward and verify `docs/site/index.html` (and the other pages) are
produced and non-empty. Fragments that exist only under `site-src/` are an
incomplete deliverable â€” the browsable site lives in `docs/site/`.

### FAILURE MODE #6 â€” Technical jargon in the User Guide page

The User Guide page is for non-technical audiences. BAN (same list as
`user-guide-generation`): client action, server action, service action,
WebDestination, widget, entity, eSpace, aggregate, REST, SOAP, site property,
structure, flow, node, JSON, PID, ClrMD. Translate to plain language. The
Technical page and the Integrations page MAY use technical terms.

### FAILURE MODE #7 â€” Building from stale docs

If the source `docs/*.md` are older than the module `.oml` files, the site
will be stale. Note this to the user and suggest re-running
`documentation-generation` first. (This skill does NOT re-extract; it is
docs-first.)

### FAILURE MODE #8 â€” Malformed site.json

`site.json` must be valid JSON with `title` (string) and `pages` (array of
`{file, label}` objects). A trailing comma, missing quote, or wrong key
fails the whole build. The orchestrator reports the parse error verbatim â€”
fix the JSON and re-run.

### FAILURE MODE #9 â€” Dropping or renaming a standard page

The site is **always 4 pages**: Overview (`index.html`), Technical
(`technical.html`), Integrations (`integrations.html`), User Guide
(`user-guide.html`). Do NOT omit a page "because the source doc is thin" or
"because there are no integrations". If a page has no data, generate it
anyway with its mandatory section headings and `(none found in extraction)`
beneath each empty section (mirrors `documentation-generation`'s empty-section
rule). The Integrations page is generated even when the tech doc has zero
integrations â€” in that case it states the gap explicitly.

### FAILURE MODE #10 â€” Inventing integrations or endpoint URLs

Every integration, endpoint name, and flow on the Integrations page MUST
trace to the technical doc. The extraction does NOT capture REST/SOAP
endpoint URLs, auth headers, or SOAP bodies â€” so do NOT fabricate them. Use
the endpoint *names* the doc provides (e.g. `batches`, `batchstatus`) and
state the URL/auth gap in a callout. A made-up URL or auth scheme is worse
than an honest "not extracted â€” verify manually".

### FAILURE MODE #11 â€” Skipping or reordering mandatory sections

Each page has a fixed `<h2>` skeleton (see Steps 5â€“8). Do NOT add, remove,
rename, merge, split, or reorder sections, and do NOT change heading levels.
If a section has no data, keep the heading and write `(none found in
extraction)`. The Step 10 verification greps `<h2>` counts per page â€” a
wrong count means the skeleton was violated. Rewrite before reporting "done".

---

## Output structure

```
docs/
â”œâ”€â”€ TECHNICAL_DOCUMENTATION.md        â† source (from documentation-generation)
â”œâ”€â”€ USER_GUIDE.md                     â† source (from user-guide-generation)
â”œâ”€â”€ site-src/                         â† this skill's SOURCE (git-track)
â”‚   â”œâ”€â”€ site.json                     â† manifest: site title + page list (4 pages)
â”‚   â”œâ”€â”€ index.html                    â† Overview / landing page content fragment
â”‚   â”œâ”€â”€ technical.html                â† Technical page content fragment
â”‚   â”œâ”€â”€ integrations.html             â† Integrations page content fragment
â”‚   â””â”€â”€ user-guide.html               â† User guide page content fragment
â””â”€â”€ site/                             â† this skill's OUTPUT (build artifact)
    â”œâ”€â”€ index.html                    â† assembled (base.html + fragment)
    â”œâ”€â”€ technical.html
    â”œâ”€â”€ integrations.html
    â”œâ”€â”€ user-guide.html
    â””â”€â”€ assets/
        â”œâ”€â”€ site.css
        â”œâ”€â”€ site.js
        â”œâ”€â”€ module-graph.js
        â””â”€â”€ mermaid.min.js
```

The `site-src/` files are the human-editable source (git-track them). The
`site/` folder is a build artifact (gitignore if desired â€” it is fully
rebuildable from `site-src/` + the baseline).

**The 4-page set is fixed.** Do not add a 5th page "because the doc has a big
section" â€” fold extra content into the appropriate standard page. Do not drop
a page "because it's thin" â€” generate it with `(none found in extraction)`.

---

## Workflow (do these in order)

### Step 1 â€” Prerequisite check (MANDATORY)

Confirm both source docs exist in the current workspace:
- `docs/TECHNICAL_DOCUMENTATION.md`
- `docs/USER_GUIDE.md`

If either is missing â†’ **HALT**. Tell the user:
> "Missing `<file>`. This skill turns already-written docs into an interactive
> site. Run `documentation-generation` (and `user-guide-generation`) first,
> then ask me to build the site."

### Step 2 â€” Read the source docs fully

Read both source `.md` files in full (use Read with offset/limit for large
files). Do NOT summarize from memory â€” read the actual content. Note:
- Technical: module table, 4-layer canvas, interaction map, data model
  entities, integrations, critical workflows, cross-cutting concerns.
- User guide: app overview, user types, getting started, each task's
  click-path, screen quick reference.

### Step 3 â€” Create the source folder

```
docs/site-src/
```
(Create it if it does not exist.)

### Step 4 â€” Write site.json (manifest)

Write `docs/site-src/site.json`. The 4-page set is fixed â€” use exactly these
4 entries in this order:

```json
{
  "title": "<App Name> â€” Documentation",
  "lang": "en",
  "pages": [
    { "file": "index.html",       "label": "Overview",     "badge": "" },
    { "file": "technical.html",   "label": "Technical",    "badge": "" },
    { "file": "integrations.html","label": "Integrations", "badge": "" },
    { "file": "user-guide.html",  "label": "User Guide",   "badge": "" }
  ]
}
```

Rules:
- `title` â€” site title shown in the sidebar header and `<title>`.
- `lang` â€” optional, defaults to `en`.
- `pages` â€” exactly the 4 standard pages, in this order. The first page is the
  landing page (linked from the site title). `file` is the fragment filename
  (must match a file in `site-src/`). `label` is the sidebar nav label. `badge`
  is optional small text shown beside the label (e.g. a count).
- Order in `pages` = order in the sidebar nav. The orchestrator auto-marks
  the current page `active`.
- Do NOT add a 5th page. Do NOT drop or reorder the 4.

### Step 5 â€” Author index.html (Overview / landing page â€” MANDATORY template)

Write `docs/site-src/index.html`. This is the **inner HTML of `<main>`** for
the landing page. Audience: mixed (both technical and non-technical). Aim for
a concise overview, not a dump of the whole doc.

**HARD RULES (non-negotiable):**
- Use EXACTLY these section headings, in EXACTLY this order.
- Do NOT add, remove, rename, merge, split, or reorder sections.
- Do NOT change heading levels â€” every listed section is an `<h2>` (the page
  `<h1>` lives inside `.hero`).
- If a section has no data for this app, KEEP the heading and write
  `(none found in extraction)` beneath it. Never drop a section.
- Only the content (facts, names, counts) varies per app â€” the skeleton is
  identical for every site.

**Mandatory structure:**

```html
<div class="hero">
  <h1>App Name</h1>
  <p class="lead">One-sentence description of what the app does, drawn from the
    source docs.</p>
  <div class="badges">
    <span class="badge"> key fact (e.g. module count) </span>
    <span class="badge"> another fact </span>
  </div>
</div>

<div class="cards">
  <a class="card" href="technical.html">  ... Technical overview ... </a>
  <a class="card" href="integrations.html"> ... Integrations ... </a>
  <a class="card" href="user-guide.html"> ... User guide ... </a>
</div>

<h2>What this app does</h2>
<p>2-3 plain paragraphs from the source docs.</p>

<h2>At a glance</h2>
<div class="table-wrap">
  <table>
    <thead><tr><th>Aspect</th><th>Detail</th></tr></thead>
    <tbody>
      <tr><td>User types</td><td>â€¦</td></tr>
      <tr><td>Key integrations</td><td>â€¦</td></tr>
      <tr><td>Modules</td><td>â€¦</td></tr>
      <tr><td>Core data</td><td>â€¦</td></tr>
    </tbody>
  </table>
</div>

<h2>About this documentation</h2>
<div class="callout info">
  <div class="callout-icon">â“˜</div>
  <div class="callout-body">
    Note the source (existing docs / live extraction) and carry over honest
    gaps (BPT, timers, endpoint URLs/auth, roles, login flow). Do not paper
    over gaps.
  </div>
</div>
```

Rules:
- The `.cards` grid MUST link to all three other pages (Technical, Integrations,
  User Guide).
- The `.badges` should surface 2â€“4 key facts (module count, entity count,
  integration count, screen count).
- The "About this documentation" callout states the source and lists carried
  gaps honestly â€” never fabricate.

### Step 6 â€” Author technical.html (Technical page â€” MANDATORY template)

Write `docs/site-src/technical.html`. Audience: technical (developers,
architects). Content drawn from `TECHNICAL_DOCUMENTATION.md`.

**HARD RULES (same as Step 5):** fixed `<h2>` headings, fixed order, no
adding/removing/renaming/reordering, no heading-level changes, empty sections
say `(none found in extraction)`.

**Mandatory `<h1>` + 8 `<h2>` sections (in this exact order):**

1. `<h1>` page title (e.g. "Technical overview").
2. `<h2>` Application overview â€” purpose paragraph + key integrations as a bulleted list.
3. `<h2>` Module architecture (4-layer canvas)
   - `<h3>` Interactive module graph â€” **MANDATORY** (see "Authoring the interactive module graph"). The graph reproduces the doc's 4-layer canvas.
   - `<h3>` 4-layer canvas â€” table (Module | Type | Layer | Role) from the doc.
4. `<h2>` Module interaction map â€” Mermaid `flowchart LR` reproducing the doc's full cross-module call-dependency graph. Use this for the full call graph; the interactive graph above is for the layered overview. Do not duplicate the same data in both.
5. `<h2>` Data model
   - `<h3>` Core transactional entities â€” Mermaid `erDiagram` (core entities only, â‰¤ ~8; note the full count and point to the technical doc) + a table.
   - `<h3>` Additional entity groups â€” one table per group the doc defines (e.g. "Order & exception", "Reference / static", "Audit & support"). Add as many `<h3>` group tables as the doc justifies; if the doc only has one group, keep one.
6. `<h2>` Logic & integration summary â€” module summary table (Module | Server Actions | Service Actions | Entities | Structures | Site Props). Plus a one-line pointer to the Integrations page for endpoint/flow detail. Do NOT inline the full integration flows here â€” they live on page 3.
7. `<h2>` Critical workflows & processes â€” one Mermaid `flowchart TD` (or `sequenceDiagram`) per workflow, each under its own `<h3>`. Include the exception-handling table if the doc has one.
8. `<h2>` Cross-cutting concerns â€” one `<h3>` per concern (audit logging, multi-tenancy, PDF generation, retry mechanisms, etc.).
9. `<h2>` Extraction notes & gaps â€” bulleted list of what was NOT auto-extracted (BPT, timers, REST/SOAP endpoint URLs + auth, roles, etc.). Carry over verbatim from the technical doc.

Rules:
- The interactive module graph (section 3) is **MANDATORY** â€” not optional.
  Every module in the doc's 4-layer canvas must appear in the graph JSON.
- Section 6 (Logic & integration summary) is a summary only. Full integration
  flows, endpoint tables, and sequence diagrams go on the Integrations page.
- If the doc has no entities, the Data model section keeps its `<h2>` +
  `<h3>` headings and writes `(none found in extraction)` â€” do not drop them.

### Step 7 â€” Author integrations.html (Integrations page â€” MANDATORY template)

Write `docs/site-src/integrations.html`. Audience: technical. Content drawn
from the integrations / cross-cutting-concerns / workflows sections of
`TECHNICAL_DOCUMENTATION.md`.

**HARD RULES (same as Steps 5â€“6):** the page is always generated; if the tech
doc has zero integrations, keep the page with a single `(none found in
extraction)` section plus the gap callout.

**Mandatory structure:**

```html
<h1>Integrations</h1>
<p>Intro paragraph: what this page covers, drawn from the tech doc.</p>

<div class="callout warn">
  <div class="callout-icon">âš </div>
  <div class="callout-body">
    <div class="callout-title">Endpoint URLs & auth not extracted</div>
    The source extraction captured integration flows but NOT REST/SOAP endpoint
    URLs, auth headers, or SOAP bodies. Use endpoint names from the doc; state
    the URL/auth gap. Do not fabricate.
  </div>
</div>

<!-- If the tech doc has â‰¥1 integration: one <h2> per integration, numbered. -->
<h2>1. <Integration Name> (<Module>)</h2>
<p>What this integration does, which module owns it, direction (inbound/outbound).</p>
<h3>Endpoints / actions</h3>
<div class="table-wrap"><table>â€¦</table></div>
<h3>Flow</h3>
<div class="mermaid">
sequenceDiagram
    â€¦
</div>
<p>Retry / failure behaviour, config notes, etc. (only what the doc states.)</p>

<h2>2. <Next Integration> (<Module>)</h2>
â€¦

<!-- Internal processes (timers, retries) each get their own <h2> too. -->
<h2>N. <Internal process name> (<Module>)</h2>
â€¦

<h2>Integration summary</h2>
<div class="table-wrap">
  <table>
    <thead><tr><th>Integration</th><th>Module</th><th>Direction</th><th>Key actions</th></tr></thead>
    <tbody>â€¦</tbody>
  </table>
</div>
```

**If the tech doc has zero integrations** (no integrations section, no external
systems mentioned), still generate the page:

```html
<h1>Integrations</h1>
<p>This application has no external integrations described in the source
  documentation.</p>
<div class="callout warn">â€¦ endpoint URLs & auth not extracted â€¦</div>
<h2>(none found in extraction)</h2>
<p>The technical documentation does not describe any external integrations,
  REST/SOAP consumers, or timer-driven processes. Verify in Service Studio if
  integrations are expected.</p>
```

Rules:
- Use a Mermaid `sequenceDiagram` for request/response flows between the app
  and an external system. Use a `flowchart` for internal processes (timers,
  retry loops). One diagram per integration/process under its own `<h3>`.
- Every integration, endpoint name, and flow MUST trace to the tech doc. Do
  not invent endpoints, URLs, or auth schemes (FAILURE MODE #10).
- Number the integration `<h2>` headings (1., 2., 3., â€¦). The final
  `<h2> Integration summary` is always present (even with zero integrations â€”
  in that case the table is empty with a note).

### Step 8 â€” Author user-guide.html (User Guide page â€” MANDATORY template)

Write `docs/site-src/user-guide.html`. Audience: non-technical end users and
business stakeholders. Content drawn from `USER_GUIDE.md`. Plain language
(FAILURE MODE #6 â€” no technical jargon).

**HARD RULES (same as Steps 5â€“7):** the 4 leading `<h2>` and 2 trailing
`<h2>` are fixed; app-group `<h2>` sit between them. Empty sections say
`(none found in extraction)`.

**Mandatory structure:**

1. `<h1>` page title (e.g. "User guide"). Plus an optional top callout for the
   login-flow gap (if the user guide notes it).
2. `<h2>` About this app â€” 2â€“3 plain sentences.
3. `<h2>` Who uses this app â€” table (user type â†’ what they do).
4. `<h2>` Getting started â€” how to reach / log in + main areas and navigation.
5. `<h2>` Tasks you can do â€” short intro paragraph.
6. One `<h2>` per app group (e.g. "Arkki mobile app tasks", "AdminTool web app
   tasks"). Each group contains one `<h3>` per task, with:
   - a one-line **Goal**,
   - a one-line **Who can do this**,
   - a Mermaid `flowchart LR` of the click-path (plain labels, not internal
     widget names),
   - a numbered `<ol>` of the steps.
7. `<h2>` Screen quick reference â€” one table per app (Screen | What it's for | Main actions).
8. `<h2>` Where to get help â€” closing notes, carried-over gaps (login flow,
   exact visible labels, permissions).

Rules:
- BANNED jargon (FAILURE MODE #6): client action, server action, service
  action, WebDestination, widget, entity, eSpace, aggregate, REST, SOAP, site
  property, structure, flow, node, JSON, PID, ClrMD. Translate to plain
  language. The technical/integration terms belong only on pages 2 and 3.
- Each task's Mermaid `flowchart LR` reproduces the user guide's "Navigation
  path" / "Steps" as a left-to-right flow of screens/actions. Use plain labels
  the user would see, not internal widget names.
- If the user guide has no tasks for an app group, keep the group `<h2>` and
  write `(none found in extraction)`.

### Step 9 â€” Assemble the site (MANDATORY)

Run the baseline orchestrator by absolute path. **This is the only command
you run** â€” see "THE ONLY BUILD COMMAND" and "DO NOT" above.

```powershell
& 'C:\Users\Ricardo Figueiredo\Documents\Outsystems_Custom\toolkits\html-docs\scripts\Render-Site.ps1' -Project '<workspace root>' -Clean
```

- `-Project` = the current workspace root (the consumer project, e.g.
  `C:\Users\Ricardo Figueiredo\Documents\ArkkiAutomation`).
- `-Clean` deletes `docs/site/` before rebuilding (clean rebuild).

Interpret the output:
- The script prints a SUMMARY table with bytes per page.
- If any row has `Bytes=0` or a `Status` other than `ok`, read the error lines
  it prints and fix the `site-src/` source, then re-run **the same command**.
  Common fixes: a missing fragment (filename mismatch with `site.json`), a
  malformed `site.json` (trailing comma / wrong key), an empty fragment.

### Step 10 â€” Verify (MANDATORY)

```powershell
# Check 1: exactly 4 HTML pages + 4 assets, all non-empty
Get-ChildItem 'docs\site\*.html','docs\site\assets\*' | Select-Object Name,Length
# Must show: index.html, technical.html, integrations.html, user-guide.html
#            + site.css, site.js, module-graph.js, mermaid.min.js â€” all Length -gt 0

# Check 2: mandatory <h2> skeleton per page (counts must match)
(Select-String -Path 'docs\site\index.html'      -Pattern '<h2' -AllMatches).Matches.Count   # == 3
(Select-String -Path 'docs\site\technical.html'  -Pattern '<h2' -AllMatches).Matches.Count   # == 8
(Select-String -Path 'docs\site\integrations.html'-Pattern '<h2' -AllMatches).Matches.Count  # >= 1
(Select-String -Path 'docs\site\user-guide.html' -Pattern '<h2' -AllMatches).Matches.Count   # >= 6 (4 leading + 2 trailing + N app groups)

# Check 3: page set is exactly the 4 standard pages (no 5th page, none missing)
(Get-ChildItem 'docs\site\*.html' | Select-Object -ExpandProperty Name | Sort-Object) -join ','
# Must be: integrations.html, index.html, technical.html, user-guide.html
```

Interpret results:
- **Check 1:** any missing/zero-length HTML or asset â†’ rebuild failed. Re-run Step 9.
- **Check 2:** `index.html` must have exactly 3 `<h2>`; `technical.html` exactly 8; `integrations.html` at least 1; `user-guide.html` at least 6. A wrong count means the mandatory skeleton was violated (FAILURE MODE #11) â€” rewrite the fragment and re-run.
- **Check 3:** any page other than the 4 standard ones (or any standard page missing) â†’ FAILURE MODE #9. Fix `site.json` to the 4 standard pages and re-run.
- Open `docs\site\index.html` in a browser and spot-check: sidebar nav works, theme toggle works, search returns results, Mermaid diagrams render, the module graph is clickable, the TOC highlights on scroll.

If anything is missing or broken, re-run Step 9 after fixing the source.

### Step 11 â€” Report

Tell the user the absolute path to open in a browser
(`docs\site\index.html`), the assembled page paths, and the `site-src/`
source paths. Note any honest gaps carried over from the source docs (e.g.
"login flow not extracted â€” verify manually", "endpoint URLs not extracted â€”
verify in Service Studio").

---

## Authoring the interactive module graph

The module graph is a clickable, explorable rendering of the app's 4-layer
canvas. It is **MANDATORY on the Technical page** (Step 6, section 3).

It is authored as **two adjacent elements** in the `technical.html` fragment:

```html
<div class="module-graph" data-module-graph></div>
<script type="application/json" class="module-graph-data">
{
  "layers": [
    { "name": "UI",           "modules": ["UI_Module"] },
    { "name": "Logic",        "modules": ["Process_Module"] },
    { "name": "Data",         "modules": ["Data_Module"] },
    { "name": "Integration",  "modules": ["REST_Module"] }
  ],
  "modules": {
    "UI_Module": {
      "label": "UI Module",
      "description": "Screens and web flows",
      "depends_on": ["Process_Module"],
      "dependents": [],
      "exposes": ["EntryPoint1"],
      "details": ["5 screens", "12 web blocks"]
    },
    "Process_Module": {
      "label": "Process Module",
      "description": "Business logic",
      "depends_on": ["Data_Module", "REST_Module"],
      "dependents": ["UI_Module"],
      "exposes": ["SubmitOrder", "CancelOrder"],
      "details": ["Order state machine", "Exception handling"]
    }
  }
}
</script>
```

Data rules:
- `layers` â€” ordered top-to-bottom (UI â†’ Logic â†’ Data â†’ Integration is the
  OutSystems convention; adapt if the doc shows a different order). Each
  layer's `modules` is a list of keys into `modules`.
- `modules` â€” map of key â†’ module object:
  - `label` (display name), `description` (one line).
  - `depends_on` â€” modules this one calls (outbound). MUST match the doc's
    interaction map. Empty array if none.
  - `dependents` â€” modules that call this one (inbound). Computed from the
    doc's interaction map; include for the "Dependents" detail panel.
  - `exposes` â€” public actions/entry points (from the doc's module table).
    Omit if the doc doesn't list them.
  - `details` â€” short highlight bullets (screen count, block count, etc.).
    Omit if not in the doc.
- Every key referenced in `layers[].modules` and in every `depends_on` /
  `dependents` MUST exist in `modules`. A dangling key produces a missing
  card with no error, which is a silent fabrication â€” avoid it.

`module-graph.js` renders the layers as a grid of cards. Clicking a card
opens a detail panel and highlights its dependents/dependencies; clicking a
dependency chip jumps to that module. The Reset button clears the selection.

Use the module graph for the **layered architecture overview** (Technical
page, section 3). Use a regular Mermaid `flowchart LR` for the **full
cross-module call graph** (Technical page, section 4 â€” when the doc has more
detail than fits the 4-layer canvas). Do not duplicate the same data in both
â€” pick the right tool per section.

---

## Authoring conventions (HTML components available)

Content fragments use plain HTML. The baseline CSS (`site.css`) provides these
component classes â€” use them so the site looks consistent:

| Component | HTML | Notes |
|-----------|------|-------|
| Hero (landing) | `<div class="hero"><h1>â€¦</h1><p class="lead">â€¦</p><div class="badges">â€¦</div></div>` | Landing page top block. |
| Badge | `<span class="badge">â€¦</span>` | Small pill, used in `.badges` or inline. |
| Card grid | `<div class="cards"><a class="card" href="â€¦">â€¦</a></div>` | Landing page link tiles. Card has `.card-icon`, `<h3>`, `<p>`, `.card-more`. |
| Callout | `<div class="callout info|tip|warn|danger"><div class="callout-icon">âš </div><div class="callout-body"><div class="callout-title">â€¦</div>â€¦</div></div>` | Notes/tips/warnings. |
| Table | `<div class="table-wrap"><table>â€¦</table></div>` | Wrap tables for horizontal scroll. |
| Code block | `<pre><code>â€¦</code></pre>` | Copy button auto-added. Inline: `<code>â€¦</code>`. |
| Collapsible | `<details><summary>â€¦</summary>â€¦</details>` | Native disclosure; styled. |
| Mermaid | `<div class="mermaid">flowchart LR\n  A --> B\n</div>` | Renders client-side. One diagram per block. |
| Module graph | see "Authoring the interactive module graph" | Two adjacent elements. MANDATORY on Technical page. |

### Mermaid authoring rules

- Wrap diagram source in `<div class="mermaid">â€¦</div>` (NOT a fenced
  ``` ```mermaid ``` code block â€” that would render as static code text).
  The `<pre><code>` syntax is for display-only code; mermaid needs the div.
- Keep diagrams focused: â‰¤ ~10 nodes per diagram. If the data model has 47
  entities, split into 2-3 grouped ER diagrams (e.g. "Core transactional",
  "Order & exception", "Audit/support"), or show only the core entities and
  note the full list is in the technical doc.
- Mermaid syntax must be valid; a syntax error renders a blank/error block for
  that diagram but does NOT fail the build (unlike the old Marp pipeline).
  Still, test mentally: balanced brackets, valid arrow types (`-->`, `-.->`,
  `==>`), quoted labels with special characters.
- Diagrams auto-adapt to dark mode (the theme re-initializes mermaid on toggle).

### Per-page Mermaid defaults (MANDATORY)

Each page has default diagram types â€” use them unless the source data forces otherwise:

| Page | Default diagrams |
|------|------------------|
| **Overview** (`index.html`) | None (uses `.hero` + `.cards` + table). |
| **Technical** (`technical.html`) | `flowchart LR` (module interaction map, section 4) + `erDiagram` (core entities, section 5) + one `flowchart TD` or `sequenceDiagram` per workflow (section 7). Plus the interactive module graph (section 3, not Mermaid). |
| **Integrations** (`integrations.html`) | `sequenceDiagram` for request/response between app and external system; `flowchart TD` for internal processes (timers, retry loops). One diagram per integration under its own `<h3>`. |
| **User Guide** (`user-guide.html`) | `flowchart LR` per task (the click-path). Plain labels, not internal widget names. |

### HTML authoring rules

- A fragment is the **inner HTML of `<main>`**. No `<!DOCTYPE>`, `<html>`,
  `<head>`, or `<body>`. No `<script src>` or `<link>` tags â€” the orchestrator
  injects all assets via `base.html`.
- Start each page with an `<h1>` (the orchestrator also uses it for the
  `<title>` tag). On the landing page, the `<h1>` may live inside `.hero`.
- Use `<h2>` for top-level sections, `<h3>` for subsections. The right-side
  TOC is auto-built from `<h2>`/`<h3>` and scroll-spies. Deeper headings
  (`<h4>`+) do not appear in the TOC.
- Give complex diagrams/tables their own `<h3>` so they surface in the TOC.
- Escape `<`, `>`, `&` in code samples (use `&lt;`, `&gt;`, `&amp;`).
- Inline `<style>` or `<script>` for page-specific behavior is allowed but
  discouraged â€” prefer the shared components. If you must, keep it minimal.

---

## Honesty about gaps

This skill inherits whatever the source docs captured, plus site-format
considerations:
- If `TECHNICAL_DOCUMENTATION.md` notes "Flow traces not captured" or
  "verify manually", the site carries the same gap â€” do NOT paper over it.
- ER diagrams show a **subset** of entities for readability; always note the
  full count and point to the technical doc.
- The module graph shows only the modules/dependencies the doc describes. If
  the doc's interaction map is incomplete, the graph is incomplete â€” note it.
- The Integrations page shows only the integrations the doc describes. Endpoint
  URLs and auth are NOT extracted â€” state this in a callout, do not fabricate.
- The User Guide page inherits `user-guide-generation`'s gaps (login flow,
  exact visible labels, cross-module navigation, mobile gestures).
- Do NOT fabricate steps, screens, integrations, or diagram nodes to fill a
  section. Empty mandatory sections say `(none found in extraction)`.

## Reference
- Build pipeline: `scripts/Render-Site.ps1` (in the DocumentGenerationHTML
  baseline). It only wraps fragments + copies assets â€” no other build step.
- Shared chrome: `templates/base.html`, `templates/site.css`,
  `templates/site.js`, `templates/module-graph.js`, `assets/mermaid.min.js`.
- Sibling skills: `documentation-generation`, `user-guide-generation` (in the
  OutsystemsAIAutomation baseline), `presentation-generation` (in the
  DocumentGeneration baseline, for PDF/PPTX slides).
- Consumer model: see `AGENTS.md` in this baseline.
