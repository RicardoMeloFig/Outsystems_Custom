# AGENTS.md

Canonical entry point AND runbook for any AI agent working in this repo. Read
this **first**, before touching scripts or skill files.

## Project overview

`DocumentGenerationHTML` is a **baseline toolkit** that turns a project's
existing Markdown documentation into an **interactive, multi-page HTML
documentation site** â€” sidebar navigation, client-side search, dark/light
theme, live Mermaid diagrams, and a clickable module graph. Pure vanilla
HTML/CSS/JS; no Node, no Python, no Docker, no bundler.

It follows the same **baseline / consumer** model as the OutsystemsAIAutomation
and DocumentGeneration (Marp) toolkits: this repo holds the templates + skill +
orchestrator; consumer projects (e.g. ArkkiAutomation) reference it via their
`opencode.json` â†’ `skills.paths` and invoke the orchestrator by absolute path.

This is the **HTML successor** to the Marp-based `DocumentGeneration` baseline.
Where the Marp baseline produced PDF + PPTX slide decks, this baseline produces
a browsable static website that explains the project interactively.

| Artifact | Role |
|----------|------|
| `.opencode/skills/html-docs-generation/SKILL.md` | The skill: reads consumer's `docs/*.md`, authors HTML content fragments, runs the build |
| `scripts/Render-Site.ps1` | Orchestrator: wraps fragments in `base.html` + copies shared assets â†’ static site |
| `templates/base.html` | Page shell: sidebar, search, TOC, theme toggle, script tags |
| `templates/site.css` | Global styles (layout, components, light/dark, responsive, print) |
| `templates/site.js` | Interactivity: theme toggle, sidebar, scroll progress, scrollspy TOC, copy buttons, client-side search, mermaid init |
| `templates/module-graph.js` | Interactive 4-layer module canvas (clickable cards + detail panel) |
| `assets/mermaid.min.js` | Vendored Mermaid v11 UMD bundle (offline-safe, renders client-side) |

## The pipeline

```
consumer docs/*.md
   â”‚  (AI skill: read + synthesize, faithful to source)
   â–¼
docs/site-src/site.json               â† manifest: site title + page list
docs/site-src/<Page>.html             â† content fragments (inner HTML of <main>)
   â”‚  (Render-Site.ps1)
   â”œâ”€â”€â–¶ for each page: wrap fragment in templates/base.html
   â”‚        â””â”€â–¶ docs/site/<Page>.html   (final static HTML, sidebar/search/TOC/scripts injected)
   â””â”€â”€â–¶ copy site.css, site.js, module-graph.js, mermaid.min.js â†’ docs/site/assets/
```

Output lands in the **consumer's** `docs/site/` folder. Open `index.html` in
any browser, or host the folder on any static server.

The standard site is **4 pages**: Overview (`index.html`), Technical
(`technical.html`), Integrations (`integrations.html`), User Guide
(`user-guide.html`). The per-page section skeletons are mandatory (fixed
`<h2>` headings, fixed order â€” see the skill for the hard rules). The
orchestrator itself stays generic: it reads whatever pages `site.json` lists,
so the 4-page rule lives in the skill, not in `Render-Site.ps1`.

## Why vanilla HTML/CSS/JS (not Marp, not Astro, not Docker)

- **No build toolchain per consumer.** Vanilla HTML/CSS/JS renders in the
  browser. There is nothing to install, bundle, or compile. The orchestrator
  only does string-template wrapping + asset copying.
- **Mermaid renders client-side** from the vendored `mermaid.min.js` (the same
  UMD bundle the Marp baseline used, but now loaded via `<script src>` in the
  browser). This eliminates the Python + Playwright + Chromium pre-render step
  that the Marp baseline needed.
- **No Node, no npm, no npx.** Unlike the Marp baseline (which auto-installed
  Node for `npx @marp-team/marp-cli`), this baseline has zero runtime
  dependencies. The orchestrator is pure PowerShell.
- **No Docker.** Static files need no container. (The Marp baseline rejected
  Docker too, for the same Windows path reasons.)
- **Interactive where it matters.** Slides are passive; an HTML site can
  offer client-side search, dark mode, a clickable module graph, scrollspy
  TOC, and copy-to-clipboard â€” all with a few KB of vanilla JS.
- **PPTX is intentionally dropped.** HTML doesn't map cleanly to PowerPoint;
  the Marp baseline's rasterized PPTX was already a known low-fidelity limit.
  PDF is also dropped (the user explicitly chose HTML-only). If a consumer
  later wants PDF, the browser's Print â†’ Save as PDF produces clean output
  via the included print stylesheet.

## Prerequisites (on the machine)

- **Windows x64** (PowerShell 5.1).
- **Any modern browser** (Edge/Chrome/Firefox) to view the site â€” preinstalled
  on Windows.
- **Nothing else.** No Node, no Python, no Playwright, no Docker. This is the
  key simplification over the Marp baseline.

The vendored `assets/mermaid.min.js` is the only third-party bundle, and it is
offline-safe (no CDN, no internet needed at view time).

## How a consumer wires this up (one-time per consumer project)

Edit the consumer's `opencode.json` â†’ `skills.paths` and append this
baseline's skills folder:

```jsonc
"skills": {
  "paths": [
    "C:/Users/Ricardo Figueiredo/Documents/Outsystems_Custom/toolkits/extraction/.opencode/skills",
    "C:/Users/Ricardo Figueiredo/Documents/Outsystems_Custom/toolkits/html-docs/.opencode/skills"
  ]
}
```

Then, from the consumer project, asking "generate an interactive site that
explains the app" triggers the `html-docs-generation` skill, which reads the
consumer's `docs/*.md` and assembles the site into the consumer's
`docs/site/`.

## Running the builder manually (without the skill)

If you already have content fragments + `site.json` in
`<project>/docs/site-src/`:

```powershell
& 'C:\Users\Ricardo Figueiredo\Documents\Outsystems_Custom\toolkits\html-docs\scripts\Render-Site.ps1' -Project '<project root>' -Clean
```

Options:
- `-Project <path>` â€” consumer project root containing `docs/site-src/`.
  Defaults to the current directory.
- `-Inputs <file...>` â€” assemble specific fragment files (by filename) instead
  of every page in `site.json`.
- `-Clean` â€” delete `docs/site/` before rebuilding (clean rebuild).

## Verifying a build

```powershell
Get-ChildItem '<project>\docs\site\*.html','<project>\docs\site\assets\*' |
  Select-Object Name,Length
```
All HTML pages + all 4 assets (`site.css`, `site.js`, `module-graph.js`,
`mermaid.min.js`) must be non-zero. Then open `docs\site\index.html` in a
browser and spot-check: sidebar nav, theme toggle, search, Mermaid diagrams,
module graph, scrollspy TOC.

## Updating the vendored Mermaid

```powershell
python -c "import urllib.request; urllib.request.urlretrieve('https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.min.js', r'C:\Users\Ricardo Figueiredo\Documents\Outsystems_Custom\toolkits\html-docs\assets\mermaid.min.js')"
```
The bundle must be the **UMD** build (`dist/mermaid.min.js`), which sets
`globalThis.mermaid`. The ESM build (`mermaid.esm.min.mjs`) will NOT work with
`base.html`'s classic `<script src>` loader.

## Honesty about limits

- The skill is **docs-first**: it does not re-extract from Service Studio. If
  the consumer's `docs/*.md` are stale, the site is stale.
- Mermaid ER diagrams show a **subset** of entities for readability; the full
  list stays in the technical doc.
- The interactive module graph shows only the modules/dependencies the source
  doc describes. An incomplete interaction map yields an incomplete graph.
- Client-side search fetches each page on first query (works over `file://`
  only in some browsers; serve via `python -m http.server` if `file://`
  fetch is blocked).
- A malformed Mermaid block renders an error box for that diagram only â€” it
  does NOT fail the whole build (unlike the Marp pipeline). Fix the diagram
  source and re-run for a clean render.
- PDF/PPTX are not produced. Use the browser's Print â†’ Save as PDF (the
  included print stylesheet hides the sidebar/TOC and lays out content
  cleanly) if a printable handout is needed.

## Relationship to the Marp baseline

This baseline (`DocumentGenerationHTML`) and the Marp baseline
(`DocumentGeneration`) can coexist. They share the same consumer model and
the same vendored Mermaid bundle, but produce different outputs:
- `DocumentGeneration` â†’ PDF + PPTX slide decks (passive, presentation).
- `DocumentGenerationHTML` â†’ interactive HTML site (browsable, explorable).

A consumer can wire up **both** skill paths and pick per request: "make a
deck" â†’ Marp; "make an interactive site" â†’ HTML.
