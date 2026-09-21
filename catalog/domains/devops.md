# DevOps & infrastructure — reference routing

**Consult when**: CI/CD pipelines (Jenkins, Azure DevOps, GitHub Actions),
deployment automation, platform install/upgrade, containers/Kubernetes,
cloud templates (ARM/Terraform), observability infra (Elastic,
OpenTelemetry, Grafana), DNS/networking, or app build verification.

## Primary sources

| Repository | Where to look | What you get |
|---|---|---|
| `docs-product` (O11) | `src/deploying-apps/cicd/`, `src/deploying-apps/devops/`, `src/deploying-apps/zones/`; platform install: `src/setup-infra-platform/` | Official CI/CD and platform setup guidance |
| `outsystems-pipeline` | `README.md` | Official Python CI/CD accelerators (Jenkins / Azure DevOps) |
| `jenkins` | Deployment scripts (`DeployLatestTagsToTargetEnv`, `FetchLifeTimeData`, `SetApplicationDeploymentZone`) | LifeTime automation examples |

## Secondary sources

| Repository | When |
|---|---|
| `odc-jenkins-pipeline` | ODC REST API pipelines (select revision → QA → approval → staging → prod) |
| `OutSystems.SetupTools`, `outsystems-chef`, `linux-outsystemsplaform-scripts`, `ContainerAutomation`, `AzureARMTemplates`, `opscloud-opscoaching`, `outsystems-hybrid-provision` | Platform provisioning (Windows/Linux/Azure/K8s) |
| `outsystems-elastic-integration`, `sqs_beats`, `opentelemetry-collector-contrib`, `terraform-provider-grafanaal`, `MonitorProbe` | Monitoring data pipelines |
| `super-linter`, `action-pr-title` | CI quality gates |
| `chisel`, `external-dns`, `fission`, `hwc` | Env access / K8s plumbing (marginal) |

## Search tips

- O11 automation is LifeTime-based (PowerShell/scripts); ODC automation is
  REST-API based — pick the right set of repos before searching.
- `docs-product/toc.yml` "Deploying apps" + "Setup OutSystems
  infrastructure and platform" sections.

## Related workspace skills

`publishing` (Save/verify/1-Click Publish + error triage),
`os-toolkit-maintenance` (workspace tooling CI itself).

## Gotchas

- Publishing to a real environment is a destructive/irreversible action —
  always confirm with the user (ground rule 3).
- Container/provisioning repos are snapshots of evolving upstream —
  validate versions against `catalog/repositories.json`.
