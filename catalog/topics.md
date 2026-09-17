# Reference library — curated topics

Entry points into `references/repos/` (~236 upstream snapshots). Full
provenance (commit, branch, dirty, license, size): `catalog/repositories.json`.

Pick the repository first, then search narrowly inside it. O11 vs ODC
matters everywhere — this workspace primarily targets **O11**.

## Official documentation (CC BY-NC-ND 4.0)

| Topic | Repository | Where to look |
|-------|------------|---------------|
| O11 platform/product docs | `docs-product` | `src/` content pages; `toc.yml` = navigation tree; frontmatter `summary`/`tags` |
| ODC docs | `docs-odc` | `src/` (much content under a historically named `eap` directory — do not skip it) |
| How-to guides | `docs-howtos` | `src/` |
| Support/troubleshooting | `docs-support` | `src/` |

## UI framework source (BSD-3-Clause)

| Topic | Repository | Where to look |
|-------|------------|---------------|
| OutSystems UI (O11 + ODC) | `outsystems-ui` | `CLAUDE.md`, `ARCHITECTURE.md`, `CSS-ARCHITECTURE.md`; patterns in `src/scripts/OutSystems/OSUI/Patterns`; SCSS in `src/scss` |
| DataGrid (Wijmo wrapper + .NET FW 4.7.2 ext) | `outsystems-datagrid` | `CLAUDE.md`; public API vs Wijmo provider vs generated Integration Studio templates |
| Maps (Google Maps/Leaflet) | `outsystems-maps` | `CLAUDE.md`; runtime assets + third-party libs |
| UI Kit (Sketch, 2020 snapshot) | `outsystems-ui-kit` | binary sketch file only |

## Tooling / integration

| Topic | Repository | Where to look |
|-------|------------|---------------|
| Official tenant MCP (ODC cloud, remote) | `outsystems-mcp` | `README.md`, `CLAUDE.md`, `SKILL.md` — OAuth/Dynamic Client Registration conventions; NOT a local O11 server |
| External libraries SDK templates | `OutSystems.ExternalLibraries.SDK-templates` | IBAN/SOAP/mTLS examples; targets net8.0/net10.0, SDK 1.5.0 |
| Pipeline/CI examples | `outsystems-pipeline`, `odc-jenkins-pipeline`, `jenkins` | sample workflows |

## Searching tips

- Docs repos have identical contributor guidance (`CLAUDE.md`) and
  classification vocabularies (`metadata.yaml`, `knowledge-needs.yaml`).
- Generated API/CSS docs inside these repos are legitimate content.
- Off-limits for routine search: binaries, images, `node_modules/`,
  `dist/`, vendored minified bundles. Some repos (TypeScript,
  DefinitelyTyped, CUBES) are huge forks — search only on demand.
