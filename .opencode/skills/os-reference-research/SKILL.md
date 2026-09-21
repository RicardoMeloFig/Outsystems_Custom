---
name: os-reference-research
description: Use when answering OutSystems product questions from the local reference library — how to find official docs, UI framework APIs, CSS, plugin or example code in references/repos/ via the task-first routing catalog.
---

# Reference research (task-first)

The workspace ships 228 upstream repository snapshots under
`references/repos/` with provenance in `catalog/repositories.json`, a
machine-readable classification in `catalog/routing.json`, and a task router
in `catalog/topics.md` pointing to per-domain pages in `catalog/domains/`.
Never bulk-read the library.

## Workflow

1. **Classify the task** into a domain (ui, logic, data, security,
   integration, mobile, architecture, devops, testing, troubleshooting)
   using `catalog/topics.md` — it is a task-first router.
2. **Open the domain page** `catalog/domains/<domain>.md`: primary repos
   (consult first) → secondary (specific need) → marginal (only on demand).
   Each entry names the repo and the exact `where to look` path.
   - No domain fits (toolkit deps, misc libs) → search `catalog/routing.json`
     for the repo's category/purpose and target it directly.
3. **Search narrowly** inside the chosen repo only: Grep/Glob under its
   `src/` (docs repos: grep root `toc.yml` first — the fastest structural
   index; `related.yml` for title lookups), page frontmatter
   (`summary`, `tags`, `audience`, `app_type`, `platform-version`), and
   headings.
4. **Cite** the path under `references/repos/<repo>/...` and note the
   snapshot commit from `catalog/repositories.json`.
5. **Distinguish O11 vs ODC** — they differ significantly; the workspace
   targets O11 unless the user says otherwise (`docs-product` = O11,
   `docs-odc` = ODC; its content lives partly under a historical `eap/`
   directory — do not skip it).

## Exclusions

Binaries, images, `node_modules/`, `dist/`, vendored minified JS are
off-limits for routine search. Removed repos are listed in
`catalog/excluded-repos.txt` (e.g. `vscode`, `kud`, `google-bert`, `CUBES`).
Licenses: docs repos are CC BY-NC-ND 4.0 (attribution, non-commercial, no
derivatives); UI libs are BSD-3-Clause.
