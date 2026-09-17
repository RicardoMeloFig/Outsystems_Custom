---
name: os-reference-research
description: Use when answering OutSystems product questions from the local reference library — how to find official docs, UI framework APIs, CSS, plugin or example code in references/repos/ via the catalog-first workflow.
---

# Reference research (catalog-first)

The workspace ships ~236 upstream repository snapshots under
`references/repos/` with provenance in `catalog/repositories.json` and a
curated map in `catalog/topics.md`. Never bulk-read the library.

## Workflow

1. **Pick the repository** from `catalog/topics.md`:
   - O11 product docs → `docs-product` (content under `src/`)
   - ODC docs → `docs-odc` (much content under `src/.../eap` — the name is
     historical, do not skip it)
   - How-tos → `docs-howtos`; support/troubleshooting → `docs-support`
   - UI framework source → `outsystems-ui` (see its `CLAUDE.md`,
     `ARCHITECTURE.md`, `CSS-ARCHITECTURE.md`)
   - DataGrid → `outsystems-datagrid`; Maps → `outsystems-maps`
   - Official tenant MCP → `outsystems-mcp`
   - External-libraries SDK templates → `OutSystems.ExternalLibraries.SDK-templates`
2. **Search narrowly** within that repo only: Grep/Glob under its `src/`
   content, page frontmatter (`summary`, `tags`, `audience`, topic ids), and
   headings. The docs repos' `toc.yml` is the fastest structural index.
3. **Cite** the path under `references/repos/<repo>/...` and note the
   snapshot commit from `catalog/repositories.json`.
4. **Distinguish O11 vs ODC** — they differ significantly; the workspace
   targets O11 unless the user says otherwise.

## Exclusions

Binaries, images, `node_modules/`, `dist/`, vendored minified JS are
off-limits for routine search. Licenses: docs repos are CC BY-NC-ND 4.0
(attribution, non-commercial, no derivatives); UI libs are BSD-3-Clause.
