# DocumentGenerationHTML

A **baseline toolkit** that turns a project's existing Markdown documentation
into an **interactive, multi-page HTML documentation site** â€” sidebar
navigation, client-side search, dark/light theme, live Mermaid diagrams, and a
clickable module graph. Pure vanilla HTML/CSS/JS; **no Node, no Python, no
Docker, no bundler**.

It is the HTML successor to the Marp-based `DocumentGeneration` baseline, and a
sibling to `OutsystemsAIAutomation` (which extracts OutSystems modules into
`docs/*.md`). Consumer projects (e.g. `ArkkiAutomation`) reference all three
via `opencode.json` â†’ `skills.paths`.

## Pipeline

```
docs/*.md  â”€â”€(html-docs-generation skill)â”€â”€â–¶  docs/site-src/site.json
                                                  docs/site-src/<Page>.html  (content fragments)
                                                      â”‚  inner HTML of <main>
                                                      â–¼
                                Render-Site.ps1
                                  â”œâ”€ wrap each fragment in templates/base.html  â†’  docs/site/<Page>.html
                                  â””â”€ copy site.css, site.js, module-graph.js, mermaid.min.js â†’ docs/site/assets/
```

Output: `docs/site/index.html` + `technical.html` + `user-guide.html` + `assets/`
in the **consumer** project. Open `index.html` in any browser, or host the
folder on any static server.

## Layout

```
DocumentGenerationHTML/
â”œâ”€â”€ AGENTS.md                                       # runbook (read first)
â”œâ”€â”€ README.md                                       # this file
â”œâ”€â”€ .opencode/skills/html-docs-generation/SKILL.md  # the skill
â”œâ”€â”€ scripts/
â”‚   â””â”€â”€ Render-Site.ps1                             # orchestrator (PowerShell only)
â”œâ”€â”€ templates/
â”‚   â”œâ”€â”€ base.html                                   # page shell (sidebar/search/TOC/theme toggle)
â”‚   â”œâ”€â”€ site.css                                    # global styles + light/dark + responsive + print
â”‚   â”œâ”€â”€ site.js                                     # theme toggle, scrollspy, search, mermaid init
â”‚   â””â”€â”€ module-graph.js                             # interactive 4-layer module canvas
â””â”€â”€ assets/
    â””â”€â”€ mermaid.min.js                              # vendored Mermaid v11 UMD (renders client-side)
```

## Prerequisites

- Windows x64, PowerShell 5.1.
- Any modern browser (Edge/Chrome/Firefox) to view the site.
- **Nothing else.** No Node, no Python, no Playwright, no Docker.

## Wire up a consumer project (one-time)

Add this baseline's skills folder to the consumer's `opencode.json`:

```jsonc
"skills": {
  "paths": [
    "C:/Users/Ricardo Figueiredo/Documents/Outsystems_Custom/toolkits/extraction/.opencode/skills",
    "C:/Users/Ricardo Figueiredo/Documents/Outsystems_Custom/toolkits/html-docs/.opencode/skills"
  ]
}
```

Then from the consumer project, ask: *"generate an interactive site that
explains the app"*. The `html-docs-generation` skill reads the consumer's
`docs/TECHNICAL_DOCUMENTATION.md` + `docs/USER_GUIDE.md`, authors content
fragments + a manifest under `docs/site-src/`, and assembles the site into
`docs/site/`.

## Build manually (without the skill)

If `docs/site-src/site.json` + `*.html` fragments already exist:

```powershell
& 'C:\Users\Ricardo Figueiredo\Documents\Outsystems_Custom\toolkits\html-docs\scripts\Render-Site.ps1' -Project '<project root>' -Clean
```

## See also

- `AGENTS.md` â€” full runbook, design rationale, and limits.
- `.opencode/skills/html-docs-generation/SKILL.md` â€” the skill workflow and
  authoring conventions.
