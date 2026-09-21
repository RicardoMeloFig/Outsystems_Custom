# UI — reference routing

**Consult when**: building/designing screens, widgets (Button, Table Records,
Input, List), web blocks, events (OnClick, screen/block lifecycle), patterns
(accordion, dropdown, gallery...), themes/CSS/SCSS, layouts, responsive,
accessibility, or embedding JS/CSS libraries in screens.

## Primary sources

| Repository | Where to look | What you get |
|---|---|---|
| `docs-product` (O11) | `src/building-apps/ui/` — `screens/`, `look-feel/`, `forms/`, `inputs/`, `patterns/web` + `patterns/mobile`, `navigation/`, `reuse/`, `screen-templates/`, `accessibility/`, `table/`, `best-practices-ui/` | Official O11 behavior. Key files: `screens/screen-block-lifecycle-events.md` (events), `reuse/block-create-reuse.md` (web blocks), `look-feel/css.md` / `look-feel/themes.md`, `forms/form-validate.md` |
| `outsystems-ui` | `CLAUDE.md`, `ARCHITECTURE.md`, `CSS-ARCHITECTURE.md`; pattern APIs at `src/scripts/OutSystems/OSUI/Patterns/*API.ts` (33 files: `AccordionAPI.ts`, `DropdownAPI.ts`, `CarouselAPI.ts`...); style layers in `src/scss/` (`00-abstract` → `09-excluders`, widgets in `03-widgets`, patterns in `04-patterns`) | The real pattern API surface and SCSS token/class architecture |
| `docs-howtos` | `src/front-end/` | Step-by-step UI recipes (popups, autocomplete, pagination pattern, validation messages, editable tables) |

## Secondary sources

| Repository | When |
|---|---|
| `outsystems-datagrid` | DataGrid work: `CLAUDE.md`, public API vs Wijmo provider vs generated Integration Studio templates |
| `outsystems-maps` | Maps work: `CLAUDE.md`, runtime assets + third-party libs |
| `outsystems-ui-kit` | Sketch design file only (binary, 2020 snapshot) — design reference, not code |
| `datavisualization-images` | Data Viz product images (DataGrid/Charts/Maps) — assets only |

## JS/CSS libraries embeddable in screens (ui-frontend-lib)

`flatpickr` (datetime picker), `virtual-select` (100k+ option dropdown),
`floating-ui`/`popper`/`popover`/`tooltip` (positioning/popovers/tooltips),
`monaco-editor` + `ace` (code editors), `react-select` (OS fork),
`html-to-image` (DOM→image, OS fork), `prism` (highlighting),
`WebBarcodePlugin` (browser barcode), `long.js` (64-bit ints for entity IDs in
JS), `jquery.outsystems.idfilter` (ID escaping), `AutoAnimations`,
`immutable-records`, `requirejs` (AMD loader, OS build fork). All
`secondary`/`marginal` — search only on specific need.

## Search tips

- `docs-product/toc.yml` section "Building apps → Design UI" is the fastest
  structural index; `related.yml` maps page titles (e.g. "Button Widget") to
  files.
- Filter docs pages by frontmatter `app_type:` — `reactive web apps`,
  `traditional web apps`, `mobile apps, reactive web apps` behave
  differently.
- In `outsystems-ui`, read the pattern's `*API.ts` for the typed API, then
  the matching `src/scss/04-patterns` for classes.

## Related workspace skills

`building-screens-and-buttons`, `building-forms`, `building-tables`,
`building-lists`, `screen-templates`, `styling-and-css-live`,
`pagination-sorting-search`, `expression-reference`.

## Gotchas

- Reactive vs Traditional web differ (events, client actions, data fetch) —
  check `app_type` before citing.
- ODC screens: use `docs-odc` (see its historical `eap/` directory).
- `outsystems-ui` pattern APIs are TypeScript definitions of the shipped
  patterns; runtime behavior varies by version — verify against your SS
  version.
