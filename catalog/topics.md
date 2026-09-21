# Reference library — task router

Entry points into `references/repos/` (228 upstream snapshots; full
provenance in `catalog/repositories.json`; per-repo classification in
`catalog/routing.json`; deliberately removed repos listed in
`catalog/excluded-repos.txt`).

**Route by task, not by folder name.** Read the domain page for the task at
hand — it lists the exact repos, paths, and search tips for that domain.
Inside a domain page, consult `primary` repos first; `secondary` on specific
need; `marginal` only if nothing else has it.

| When the task is… | Read first | Always useful |
|---|---|---|
| Build/design UI (screens, widgets, web blocks, events, patterns, themes/CSS) | `catalog/domains/ui.md` | `docs-product`, `outsystems-ui` |
| Business logic (actions, service actions, exceptions, timers, processes) | `catalog/domains/logic.md` | `docs-product`, `outsystems-ui` |
| Data (entities, relationships, aggregates, SQL, CRUD wrappers) | `catalog/domains/data.md` | `docs-product` |
| Security (roles, permissions, auth/SSO, web security) | `catalog/domains/security.md` | `docs-product`, `docs-odc` |
| Integration (REST/SOAP, extensions, external libraries, external DBs) | `catalog/domains/integration.md` | `docs-product`, `OutSystems.ExternalLibraries.SDK-templates` |
| Mobile (offline, plugins, push, MABS, native SDKs) | `catalog/domains/mobile.md` | `docs-product`, `outsystems-phonegap-plugin-push` |
| Architecture/lifecycle (4-layer canvas, DDD, LifeTime, deployment) | `catalog/domains/architecture.md` | `docs-product` |
| DevOps/infra (CI/CD pipelines, platform install, observability) | `catalog/domains/devops.md` | `docs-product`, `outsystems-pipeline` |
| Testing/QA (mocks, contract, visual regression) | `catalog/domains/testing.md` | `docs-product` |
| Troubleshooting/performance/monitoring | `catalog/domains/troubleshooting.md` | `docs-support`, `docs-product` |
| Anything else (toolkit deps, misc libs) | `catalog/routing.json` (search the repo directly) | — |

## Versioning: O11 vs ODC

**This workspace targets O11 as the default** (proven on Service Studio
11.55.81). `docs-product` = O11, `docs-odc` = ODC (much content under a
historically named `src/.../eap` directory — do not skip it). Docs pages
carry frontmatter `platform-version:` and `app_type:` — filter on those
before citing a page (e.g. `reactive web apps` vs `traditional web apps`
vs `mobile apps, reactive web apps`).

## Search tips

- The docs repos (`docs-product`, `docs-odc`, `docs-howtos`) share the same
  tooling: content in `src/`, navigation in root `toc.yml`, page-title index
  in root `related.yml`. Grep `toc.yml` for a topic, then open the `src/`
  file.
- The UI framework repo `outsystems-ui` has its own `CLAUDE.md`,
  `ARCHITECTURE.md`, `CSS-ARCHITECTURE.md` — read those first.
- Generated API/CSS docs inside these repos are legitimate content.
- Cite paths as `references/repos/<repo>/...` and note the snapshot commit
  from `catalog/repositories.json`.

## Exclusions

- Off-limits for routine search: binaries, images, `node_modules/`, `dist/`,
  vendored minified bundles.
- Removed from the library (see `catalog/excluded-repos.txt`): `vscode`,
  `kud`, `dotnet-project-for-experiments`, `blogpost_regression_nunit`,
  `google-bert`, `graph_nets`, `CUBES`, `pandora-no-op`.
- Licenses: docs repos are CC BY-NC-ND 4.0 (attribution, non-commercial, no
  derivatives); UI libs are BSD-3-Clause.
