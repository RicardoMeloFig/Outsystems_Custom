# references/ â€” OutSystems ground-of-truth integration

This folder links the toolkit to the local **OutSystems ground-of-truth repo**
(a collection of OutSystems' public repositories, e.g.
`C:\Users\Ricardo Figueiredo\Documents\Outsystems_Custom\references\repos`). It provides the
reference material that makes extraction output *interpretable*, not just raw.

## Contents

| File | Tracked? | Purpose |
|------|----------|---------|
| `source-path.txt` | **No** (gitignored, machine-specific) | Absolute path to the ground-of-truth repo. Read by `scripts/Build-UiReference.ps1` and by the docs skills. |
| `outsystems-ui/patterns.md` | Yes (generated, committed) | Pattern catalog for the OutSystems UI framework: pattern name, category, provider, public JS API, CSS classes, CSS custom properties. |
| `outsystems-ui/classic-theme-o11.css` | Yes (generated, committed) | Verbatim copy of the OutSystems UI **O11 classic theme** CSS â€” the base stylesheet every O11 Reactive theme starts from. Use it to diff/explain extracted theme CSS. |

## Regenerating

The generated files are committed so clones and consumer projects get them for
free. Regenerate after the ground-of-truth repo is updated:

```powershell
.\scripts\Build-UiReference.ps1            # reads references/source-path.txt
.\scripts\Build-UiReference.ps1 -SourceRepo "C:\path\to\Outsystems REPO"   # or pass explicitly
```

Do **not** edit `patterns.md` / `classic-theme-o11.css` by hand â€” they are
build output.

## Official docs corpus (used by docs skills)

The ground-of-truth repo also carries OutSystems' official documentation
sources (markdown):

- `<source-path>\docs-product\src` â€” product docs (`building-apps/`, `ref/`, `security/`, ...)
- `<source-path>\docs-howtos\src` â€” how-to docs
- `<source-path>\docs-odc\src` â€” ODC-only docs (not relevant to OS11 work)

The `documentation-generation` and `user-guide-generation` skills consult these
(optional) to verify terminology against official language. Prefer the
O11-relevant pages; ignore `migration-to-odc` / `extending-with-odc` content
when documenting OS11 modules.

## Consumer projects

Linked consumers (`scripts/New-LinkedProject.ps1`) inherit the generated files
via the baseline's absolute path â€” no copy needed. If a consumer is a full
clone/copy, re-run `Build-UiReference.ps1` there (it needs `source-path.txt`).
