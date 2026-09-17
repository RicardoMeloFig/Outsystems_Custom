# Workspace architecture

Outsystems_Custom consolidates four sources into one OutSystems 11 AI
automation workspace. Design goals: the AI model gets exactly the context a
task needs; nothing is duplicated; everything is verifiable.

## Layers

```
                    ┌────────────────────────────┐
                    │  opencode.json + AGENTS.md │  lean primary agent (router)
                    └────────────┬───────────────┘
          Task tool delegation   │
   ┌──────────┬──────────┬──────┴─────┬──────────┬──────────┐
   │os-refer  │os-extract│os-edit-    │os-edit-  │os-docs   │os-toolkit
   │ence      │          │headless    │live      │          │
   └────┬─────┴────┬─────┴─────┬──────┴────┬─────┴────┬─────┴────┐
        │          │           │           │          │          │
  catalog+    3 ClrMD     omleditor   liveeditor  skills +   scripts/
  grep/read   MCP exes    MCP exe     MCP exe +   files      config
        │          │           │           bridge
  references/  reads SS    edits .oml  edits open
  repos/       process     files       module in SS
```

- **Primary agent**: only `AGENTS.md` + workspace files. All five MCP servers
  are disabled globally (`"tools": {"outsystems-*": false}`); specialists
  re-enable their own server's tools via agent-file frontmatter, so tool
  schemas (196 for the live editor alone) only enter context when relevant.
- **Skills**: 36 total, in four discovery paths (root 2 + extraction 7 +
  editor 26 + html-docs 1). Root skills describe workspace-wide workflows
  (catalog research, toolkit maintenance); toolkit skills stay scoped with
  their components.

## Toolkits

| Toolkit | Backend | Writes? |
|---------|---------|---------|
| `toolkits/extraction` | ClrMD heap reading from `ServiceStudio.exe` | docs outputs only |
| `toolkits/editor` | SS model DLLs (headless) / OsLiveBridge named pipe (live) | `.oml` files / live module |
| `toolkits/html-docs` | PowerShell template wrapper + vendored Mermaid | consumer `docs/site/` |

## Reference library

`references/repos/` holds git-free snapshots (175k files). Provenance in
`catalog/repositories.json` (branch/commit/shallow/dirty/license per repo;
regenerate with `scripts/New-Catalog.ps1` from the original `Outsystems REPO`
folder). Retrieval is catalog-first: `catalog/topics.md` → repo → targeted
grep. Git history for every snapshot remains in the original folders.

## Invariants

1. The primary agent never loads MCP tool schemas.
2. One editing specialist at a time per module; destructive actions require
   explicit confirmation (bridge install, deletions, source `.oml`
   overwrite, publish).
3. Every mutation is verified by read-back; extraction prerequisites are
   checked (`get_open_module`, explicit `pid`).
4. `scripts/Verify-Project.ps1` is the gate: OK + exit 0 or stop.
