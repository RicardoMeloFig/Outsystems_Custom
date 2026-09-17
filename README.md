# Outsystems_Custom

Consolidated OutSystems 11 AI automation workspace. Merges four sources:

| Source | Now lives in | Role |
|--------|--------------|------|
| AI Outsystem Automation | `toolkits/extraction/` | Read-only extraction (3 ClrMD MCP servers, 7 skills, schemas) |
| AI Outsystems Automation Editor | `toolkits/editor/` | OML editing (headless + live, 2 MCP servers, 26 skills, SS bridge) |
| DocumentGenerationHTML | `toolkits/html-docs/` | Interactive HTML docs site generator (1 skill) |
| Outsystems REPO | `references/repos/` | Local snapshots of ~236 upstream repos + provenance catalog |

See **`AGENTS.md`** (AI agent routing) and **`SETUP.md`** (setup runbook).

## Quick start

```powershell
.\scripts\Build-All.ps1        # build the 5 MCP server exes
.\scripts\Verify-Project.ps1   # must print OK: and exit 0
.\scripts\Start-OpenCode.ps1   # launch OpenCode from this folder
```

## Linked consumer projects (per-application workspaces)

This workspace is the **shared baseline**. Application projects (e.g. one per
OutSystems app) are thin consumers that keep their own OMLs/docs and use all
tooling from here in place:

```powershell
.\scripts\New-LinkedProject.ps1 -Target "C:\Users\<you>\Documents\NewAppAutomation" -AppName "NewApp"
.\scripts\Verify-LinkedProject.ps1 -Target "C:\Users\<you>\Documents\NewAppAutomation"   # must print READY
```

Refresh after baseline changes with `-Refresh` (user edits are preserved via a
hash manifest). See `AGENTS.md` → "Linked consumer projects".

## Notes

- Reference snapshots are **git-free** working trees; upstream Git history
  stays in the original `Outsystems REPO` folders. `catalog/repositories.json`
  records each snapshot's origin, commit, branch, license, and size.
- The official remote OutSystems tenant MCP (ODC cloud) is **not** configured;
  see `SETUP.md` for how to enable it later if needed.
