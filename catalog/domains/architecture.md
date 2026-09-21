# Architecture & lifecycle — reference routing

**Consult when**: module/application design (4-layer canvas), module
dependencies, domain-driven design, reference architectures, microservices,
LifeTime, deployment plans, versioning (tags/hotfixes/rollback), migration
to ODC. This page feeds the `os-architect` design gate.

## Primary sources

| Repository | Where to look | What you get |
|---|---|---|
| `docs-product` (O11) | `src/app-architecture/design-architecture/` (`01-4-layer-canvas.md`, `02-translating-business-app-modules.md`, `03-validating-app-architecture.md`, `05-integration-patterns.md`, `08-microservices-arch-outsystems.md`), `src/app-architecture/domain-driven-design/`, `src/app-architecture/performance-top-10-rules.md` | The canonical architecture rules |
| `docs-product` (lifecycle) | `src/deploying-apps/` (`deploy-an-application.md`, `deployment-plans.md`, `tag-a-version.md`, `rollback-to-a-previous-version.md`, `apply-a-hotfix.md`, `cicd/`, `devops/`, `zones/`), `src/manage-platform-app-lifecycle/` (LifeTime, teams) | Deploy/version/lifecycle mechanics |
| `LifetimeServicesExample` | `README.md` + samples (C#/Java) | Calling the LifeTime Services API programmatically |

## Secondary sources

| Repository | When |
|---|---|
| `docs-odc` + `docs-product/src/migration-to-odc/` | ODC migration planning (see also `docs-product/src/extending-with-odc/`) |
| `OutBuilding` | Real multi-module app sample (mobile + maps) — small enough to read |
| `jenkins`, `outsystems-pipeline` | CI/CD wiring for architecture decisions — see `devops.md` |

## Search tips

- `docs-product/toc.yml` "App architecture", "Deploying apps",
  "Manage platform and app lifecycle" sections; `related.yml` maps titles.
- Architect contracts must sequence bottom-up by module dependency: CS →
  BL → UI (producers before consumers).

## Related workspace skills / agents

`os-architect` (design gate — must run before substantial builds),
`docs/architecture.md` (workspace architecture), `managing-dependencies`.

## Gotchas

- 4-layer canvas is a guideline with exceptions; validate per module against
  `03-validating-app-architecture.md`.
- ODC changes module model (web libraries, different lifecycle) — don't
  apply O11 lifecycle docs to ODC projects.
