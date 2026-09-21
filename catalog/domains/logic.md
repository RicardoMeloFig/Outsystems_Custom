# Logic — reference routing

**Consult when**: writing business logic — service actions, server vs client
actions, action parameters, multi-step flows, exception handling, timers,
processes/BPM, case management, expressions, business rules, CRUD wrappers.

## Primary sources

| Repository | Where to look | What you get |
|---|---|---|
| `docs-product` (O11) | `src/building-apps/logic/` (`actions.md`, `action-web.md`, `expression-editor.md`), `src/building-apps/handling-exceptions/`, `src/building-apps/timers/`, `src/building-apps/processes/` (BPM + `reference-bpmn/` + `process-flow/`), `src/building-apps/case-management-workflow/`; reference: `src/ref/lang/auto/class-service-action.md`, `src/ref/logic/implementing-logic/` | Official semantics: when logic runs, exceptions, timer rules, process definition |
| `docs-howtos` | `src/logic/` (regex/strings, email sending, file-upload validation, login extras), `src/processes/` (BPT archive, taskbox configuration) | Practical recipes |

## Secondary sources

| Repository | When |
|---|---|
| `docs-odc` | ODC logic differences (external logic, different action model) — `src/.../eap/` included |
| `outsystems-datagrid` | Only when the "logic" is DataGrid server-side callbacks/events |

## Search tips

- Grep `docs-product/toc.yml` for the logic section, then read the `src/`
  file; `related.yml` maps titles like "Service Action" to files.
- Expression semantics (operators, nulls, function catalog):
  `src/ref/lang/auto/builtinfunction-*.md` — verify there before writing any
  expression.
- Frontmatter `app_type:` distinguishes Reactive client actions from
  Traditional server actions.

## Related workspace skills

`creating-service-actions-live` (default) / `creating-service-actions`
(headless, only on request), `building-service-action-flows`,
`exception-handlers-in-service-actions`, `creating-crud-wrappers`,
`app-configuration` (site properties, timers), `expression-reference`.

## Gotchas

- Client-side vs server-side execution is a Reactive app concern — pick the
  right action type early.
- Exception handling in OutSystems uses exception handlers per node, not
  try/catch blocks — check `handling-mechanism.md` before building.
- Timers have no guaranteed exact run time (queue-based); processes are
  persisted and resumable.
