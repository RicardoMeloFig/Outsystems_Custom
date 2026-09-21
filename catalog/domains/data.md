# Data — reference routing

**Consult when**: entities, attributes, relationships, delete rules, static
entities, indexes, structures, aggregates, SQL/dynamic SQL, CRUD wrappers,
site properties, offline data stores, or data access performance.

## Primary sources

| Repository | Where to look | What you get |
|---|---|---|
| `docs-product` (O11) | `src/building-apps/data/modeling/` (`entity.md`, `entity-create.md`, `entity-static.md`, `index-create.md`, `relationship/` incl. `delete-rules.md`), `src/building-apps/data/operations/` (`fetch-display.md`, `filter-results.md`, `sort-aggregate.md`, `sql.md`, `best-practices-fetch-data/`), `structure-create-use.md`, `crud-wrappers.md`, `data/site/site.md` (site properties) | Modeling rules, how aggregates/SQL work, best fetch practices |
| `docs-howtos` | `src/data/` (Excel bootstrap/export, data migration, XML parsing, concurrent updates) | Practical data recipes |

## Secondary sources

| Repository | When |
|---|---|
| `npgsql` | PostgreSQL-specific .NET provider behaviors (external DB / extension work) |
| `sqlcipher`, `Cordova-sqlite-storage`, `Cordova-sqlcipher-adapter`, `Android-sqlite-connector`, `Android-sqlite-native-driver` | Mobile/offline data stores — see also `mobile.md` |
| `Tenant-Pool-Size-Prediction` | ML.NET forecasting example extension (data science, marginal) |

## Search tips

- `docs-product/toc.yml` "Building apps → Data" sections (model, operations,
  site props); `related.yml` maps titles to files.
- Filter by `app_type:` — Traditional and Reactive fetch semantics differ;
  `data/offline/` and `mobile-best-practices/` are mobile-only.
- SQL vs aggregate: aggregates are compiled into SQL — for dynamic results
  use `sql.md` + `build-dynamic-sql-statements.md`.

## Related workspace skills

`creating-entities`, `adding-attributes`, `building-tables` (aggregates),
`creating-crud-wrappers`, `expression-reference` (functions/filters).

## Gotchas

- Never guess attribute types/keys — extract the module first
  (`os-extract`) and probe.
- Delete rules are defined at relationship level, not entity level.
- Site properties are platform-level config; avoid storing business data in
  them.
