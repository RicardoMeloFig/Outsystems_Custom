# projects/ — application workspaces

Each subfolder is a **consumer project**: its own root for extraction
outputs, synthesized Markdown documentation, and generated HTML sites.

Expected structure per project (created on demand):

```
projects/<AppName>/
├── docs/
│   ├── TECHNICAL_DOCUMENTATION.md   ← from documentation-generation skill
│   ├── USER_GUIDE.md                ← from user-guide-generation skill
│   ├── site-src/                    ← authored HTML fragments + site.json
│   └── site/                        ← built site (Render-Site.ps1 output)
```

Agents default to the active project here; when ambiguous, they ask which
project root to use.
