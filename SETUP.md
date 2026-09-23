# SETUP.md — one-time setup on a new PC

## Prerequisites

- **Windows x64**
- **Git** — https://git-scm.com
- **.NET 8+ SDK** — `dotnet --list-sdks` must show an `8.0.x` (or newer)
  entry. Needed only to **build** the MCP exes; **running** them needs no
  .NET install at all — `Build-All.ps1` publishes self-contained win-x64 by
  default (each exe bundles the runtime).
- **OutSystems Service Studio 11** — installed at
  `C:\Program Files\OutSystems\Service Studio 11\Service Studio\`
  (override with the `OSSS_DIR` env var if elsewhere).
- **OpenCode Desktop** — `%LOCALAPPDATA%\Programs\@opencode-aidesktop\OpenCode.exe`
  (https://opencode.ai). The `opencode` CLI is optional; the launcher script
  uses the Desktop app.

> **Zip-copy works with zero .NET installs.** A zip copy of a healthy,
> already-built baseline runs anywhere (Windows x64): the exes are
> self-contained and gitignored artifacts are the only things you lose by
> not cloning. A `git clone` needs the one-time SDK install above, then
> `Build-All.ps1` produces portable, always-current exes.

## Steps (fresh-PC runbook)

1. **Build the MCP servers** (outputs are gitignored):
   ```powershell
   .\scripts\Build-All.ps1          # self-contained win-x64 (default, portable)
   ```
   `-FrameworkDependent` is a dev-only opt-out (smaller output, but the
   machine running the servers then needs a .NET 8 runtime — not portable).
2. **Build the Service Studio bridge** (compile only; does NOT install):
   ```powershell
   .\scripts\Build-Bridge.ps1
   ```
   Installing the bridge DLL into Service Studio is a separate, explicit step
   (`.\scripts\Build-Bridge.ps1 -Install`) — it changes Service Studio's
   `Plugins\ServiceStudio\` folder and requires confirmation.
3. **Verify**:
   ```powershell
   .\scripts\Verify-Project.ps1     # must print OK: and exit 0
   ```
4. **Launch**:
   ```powershell
   .\scripts\Start-OpenCode.ps1
   ```
   Inside opencode, run `/mcp` — all five servers must show connected. If a
   server is inactive, re-run `Build-All.ps1`, then restart opencode.

## Reference library

- `references/repos/` holds git-free snapshots. Provenance (origin, commit,
  branch, dirty state, license) is in `catalog/repositories.json`.
- Regenerate the catalog from the original `Outsystems REPO` folder:
  ```powershell
  .\scripts\New-Catalog.ps1 -Source "C:\Users\Ricardo Figueiredo\Documents\Outsystems REPO"
  ```
- Re-sync changed files from the original folder. **Warning:** the default
  mode mirrors (`robocopy /MIR`) — it deletes snapshot files that no longer
  exist in the source and overwrites local snapshot edits. Use `-NoMirror`
  to only add/update:
  ```powershell
  .\scripts\Sync-References.ps1 -Source "C:\Users\Ricardo Figueiredo\Documents\Outsystems REPO" -NoMirror
  ```

## Linked consumer projects (per-application workspaces)

Each OutSystems application gets its own thin consumer project that uses this
baseline in place (nothing copied except generated agent/command markdown):

```powershell
# from this baseline root — creates opencode.json, the 8 agents
# (coordinator + architect + 6 specialists), the 5 commands, AGENTS.md,
# .gitignore, <AppName>.md, OMLs/, open/, docs/:
.\scripts\New-LinkedProject.ps1 -Target "C:\Users\<you>\Documents\NewAppAutomation" -AppName "NewApp"

# integrity gate for the consumer (must print READY and exit 0):
.\scripts\Verify-LinkedProject.ps1 -Target "C:\Users\<you>\Documents\NewAppAutomation"

# optional: prove the baseline exes speak MCP (initialize + tools/list):
.\scripts\Test-McpHandshake.ps1
```

Then launch OpenCode Desktop **with the consumer folder as working directory**:

```powershell
Start-Process "$env:LOCALAPPDATA\Programs\@opencode-aidesktop\OpenCode.exe" -WorkingDirectory "C:\Users\<you>\Documents\NewAppAutomation"
```

Inside opencode: `/mcp` must show all five `outsystems-*` servers connected.

Key behaviors:

- **Outputs stay in the consumer**: extraction docs (`docs/<Module>/`),
  rendered sites (`docs/site/`), and edited `.oml` artifacts (`OMLs/`). The
  baseline is never written to from a consumer.
- **Refresh after baseline changes** (new agents, commands, templates, or a
  moved baseline folder): re-run `New-LinkedProject.ps1 ... -Refresh`. Only
  unmodified generated files are regenerated — files you edited are skipped
  (tracked via `.opencode/link-manifest.json` hashes); `-Force` overrides.
- **Restart OpenCode** after scaffolding/refreshing — config is loaded once
  at startup.

## Optional: official remote OutSystems MCP (ODC cloud)

Not configured by default. To enable later, add to `opencode.json` → `mcp`:

```jsonc
"outsystems": {
  "type": "remote",
  "url": "https://<your-tenant>.outsystemscloud.com/mcp",
  "enabled": true
}
```

OpenCode handles OAuth (dynamic client registration) automatically on first
use. Setup guidance: `references/repos/outsystems-mcp/README.md`.

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| MCP server inactive in `/mcp` | exe not built → `.\scripts\Build-All.ps1`; restart opencode. |
| `Ensure-Built.ps1` exits 1 (`STALE-LOCKED`) | OpenCode holds DLLs → close it, re-run, relaunch. |
| Editor tools error "type not found" | SS 11 not installed / `OSSS_DIR` wrong. |
| `Verify-Project.ps1` prints `MISSING:` | toolkit incomplete — restore from the original source folders. |
| Build fails on `net8.0` targeting pack | let NuGet restore it (network), or install the .NET 8 SDK. |
| `Build-All.ps1` exits 2 (stale) | locked files or failed publish; old exe kept → close opencode, re-run. |
| Server exe crashes with "You must install or update .NET" | **poisoned publish dir** — mixed self-contained/framework-dependent leftovers from interrupted builds. Delete that server's `publish/` folder, close opencode, re-run `.\scripts\Build-All.ps1`. |
| `Verify-Project.ps1` warns "FRAMEWORK-DEPENDENT (no coreclr.dll)" | the publish was built with `-FrameworkDependent` or by an old script version → re-run `.\scripts\Build-All.ps1` (self-contained is the default) with opencode closed. |
| Consumer `Verify-LinkedProject.ps1` prints `MISSING:` | re-run `New-LinkedProject.ps1 -Target <target> -AppName <app> -Refresh`; rebuild baseline exes if the gate names a missing exe. |
| Consumer refresh skips a file you edited | by design (manifest hash differs). Rename your version, refresh, re-apply your edit — or use `-Force` to discard it. |
| `Test-McpHandshake.ps1` FAILs a server | the exe is not protocol-usable — rebuild it (`Build-All.ps1 -Name <server>`), then re-probe. |

## Committing

The reference library (~4 GB, 175k files) is plain files and *can* be tracked,
but committing it makes the repository heavy. Decide deliberately; the
catalog + `Sync-References.ps1` allow restoring snapshots without committing
them.
