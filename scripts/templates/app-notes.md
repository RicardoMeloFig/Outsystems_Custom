# __APPNAME__ Automation

This project contains **__APPNAME__-specific content only**: OMLs,
extraction outputs, hand-written docs, generated sites, and notes. All
tooling (5 MCP servers, 36 skills, 6 specialist agents, workflow commands,
scripts, reference library) is consumed in place from the shared baseline:

**Baseline:** `__BASELINE__`
**This project:** `__TARGET__`

Nothing is copied from the baseline; when it is updated or its exes are
rebuilt there, this project picks up the changes immediately. If the
baseline folder moves to a new PC or path, re-run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "<new baseline>\scripts\New-LinkedProject.ps1" -Target "__TARGET__" -AppName "__APPNAME__" -Force
```

## __APPNAME__ modules

The __APPNAME__ application (OMLs in `./OMLs/`, kept locally — not git-tracked):

| Module | Type |
|--------|------|
| <!-- Add modules as you work with them. --> | |

## Conventions

- Extraction output goes under `./docs/<ModuleName>/` (gitignored; regenerated).
- Hand-written docs (`./docs/*.md`) and site fragments (`./docs/site-src/`) are tracked.
- The rendered site lands in `./docs/site/` (gitignored).
- Open-module drop folder: `./open/`.
- After baseline agent/command/config changes, refresh the generated links
  and restart OpenCode:

  ```powershell
  powershell -NoProfile -ExecutionPolicy Bypass -File "__BASELINE__\scripts\New-LinkedProject.ps1" -Target "__TARGET__" -AppName "__APPNAME__" -Refresh
  ```

## Building / verifying the tooling

The MCP exes live in the baseline (not copied here). After a baseline
update or fresh setup, run from the baseline folder:

```powershell
__BASELINE__\scripts\Ensure-Built.ps1
__BASELINE__\scripts\Verify-Project.ps1
```

Verify THIS project (must print READY and exit 0):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "__BASELINE__\scripts\Verify-LinkedProject.ps1" -Target "__TARGET__"
```
