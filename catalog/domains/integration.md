# Integration — reference routing

**Consult when**: consuming/exposing REST APIs, SOAP (consume/expose),
Integration Studio extensions (.NET), External Libraries, external database
connections (SQL Server/PostgreSQL/MongoDB), SAP, OData, webhooks, swagger
clients, or building custom components that talk to external systems.

## Primary sources

| Repository | Where to look | What you get |
|---|---|---|
| `docs-product` (O11) | `src/integration-with-systems/rest/` (`consume-rest-apis/`, `expose-rest-apis/`, `troubleshoot-a-rest-api.md`), `soap/` (`consume/`, `expose/`), `integration-studio/`, `external-database/` (`connect-external-db.md`, `mongo-db.md`), `sap/`, `integration-builder/`, `artificial-intelligence/`; IS errors in `ref/errors-and-warnings/errors-is/` | Official integration mechanics and error catalog |
| `OutSystems.ExternalLibraries.SDK-templates` | `README.md` + templates (basic/advanced/SOAP; .NET and .NET 8) | How to structure an External Library project (ODC; targets SDK 1.5.0) |

## Secondary sources

| Repository | When |
|---|---|
| `cloud-connector` | Secure ODC/cloud → on-premises network connectivity |
| `swagger-js`, `swagger-ui` | Swagger client generation / interactive API UI |
| `UltimatePDF-ExternalLogic`, `vanguard-xml-to-json` | ODC external-logic ports (PDF generation, XML→JSON) — reference implementations |
| `npgsql` | PostgreSQL .NET provider specifics |
| `HeadlessChromium.Puppeteer.Lambda.Dotnet` | Server-side PDF/screenshot generation outside the platform |

## Search tips

- `docs-product/toc.yml` "Integration with external systems" is the whole
  domain; `related.yml` maps action titles to pages.
- Filter by `app_type:` — REST/SOAP consumption works in all app types but
  credentials/headers handling differs.
- O11 Integration Studio extensions are .NET Framework 4.7.2; ODC External
  Libraries are .NET 8/10. Never mix guidance.

## Related workspace skills

`managing-dependencies` (consume producer elements), `creating-crud-wrappers`
(if integrating via generated wrappers), `expression-reference`.

## Gotchas

- REST input from external systems must be treated as untrusted — validate
  and sanitize server-side.
- Consuming a producer module is different from an external integration;
  dependencies live in the module dependency graph, endpoints live in the
  deployment zone.
