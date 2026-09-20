# Workspace architecture

Outsystems_Custom consolidates four sources into one OutSystems 11 AI
automation workspace. Design goals: the AI model gets exactly the context a
task needs; nothing is duplicated; everything is verifiable.

## Layers

```
              ┌─────────────────────────────────┐
              │ opencode.json + AGENTS.md       │
              │ primary agent (any request)     │  routes OutSystems work down
              └───────────────┬─────────────────┘
          Task tool call       │
              ┌───────────────┴─────────────────┐
              │ os-coordinator (subagent)       │  intake, routing, verification
              └───────────────┬─────────────────┘
          Task tool delegation │
    ┌───────────┬─────────────┼──────────────┬─────────────┬─────────┐
    │os-refer   │os-extract   │os-architect  │os-edit-*    │os-docs  │os-toolkit
    │ence       │             │(design gate) │headless/live│         │
    └────┬──────┴──────┬──────┴──────┬───────┴──────┬──────┴────┬────┴────┬─────┐
         │             │             │              │           │         │
   catalog+      3 ClrMD      extraction        omleditor   skills +   scripts/
   grep/read     MCP exes      evidence +        + liveeditor files     config
                 reads SS      reference docs    MCP exes +
         │       process                 bridge    edits .oml  edits open
   references/                            files    module in SS
   repos/
```

- **Primary agent** (normal model, no `default_agent` override): routes any
  OutSystems task to the `os-coordinator` subagent, which classifies,
  gathers evidence, dispatches specialists, and returns one consolidated
  report. All five MCP servers are disabled globally
  (`permission: {"outsystems-*": "deny"}`) for the primary agent AND the
  coordinator; specialists re-enable their own server's tools via
  agent-file frontmatter, so tool schemas (196 for the live editor alone)
  only enter context when relevant. `os-architect` (read-only: no MCP, no
  edits) gates every substantial build BEFORE an editor runs.
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

1. The primary agent (coordinator) and the architect never load MCP tool
   schemas; the architect additionally cannot edit files or run commands
   (design stays evidence-based and mutation-free).
2. One editing specialist at a time per module; destructive actions require
   explicit confirmation (bridge install, deletions, source `.oml`
   overwrite, publish).
3. Every mutation is verified by read-back; extraction prerequisites are
   checked (`get_open_module`, explicit `pid`).
4. `scripts/Verify-Project.ps1` is the gate: OK + exit 0 or stop.
