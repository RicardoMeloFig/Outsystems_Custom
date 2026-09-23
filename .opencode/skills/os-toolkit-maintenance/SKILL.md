---
name: os-toolkit-maintenance
description: Use when building, rebuilding, verifying, or troubleshooting the workspace tooling — MCP exes, the Service Studio bridge, build/verify/launch scripts, opencode.json, agents, and skills. Covers staleness gates and confirmation boundaries.
---

# Toolkit maintenance

## Build/staleness model

- Five MCP exes are gitignored and live in `toolkits/*/mcp/<server>/publish/`.
- `scripts\Build-All.ps1` publishes SELF-CONTAINED win-x64 by default
  (portable exes; no .NET runtime needed on the machine running them).
  `-FrameworkDependent` is a dev-only opt-out. The publish dir is cleaned
  before each publish, and a server whose files are locked by a running
  opencode is skipped (never half-overwritten — that poisons the dir).
- Any MCP source edit makes its exe stale (source newer than exe). Stale exes
  silently produce wrong output (e.g. truncated CSS, missing per-module
  attribution). A framework-dependent publish (no `coreclr.dll` in the
  publish folder) is also treated as stale by the gates.
- `scripts\Ensure-Built.ps1` compares timestamps, detects framework-dependent
  publishes, and rebuilds self-contained; exit 1 (`STALE-LOCKED`) means
  OpenCode holds DLLs — close it, rebuild, relaunch.
- `scripts\Start-OpenCode.ps1` runs the gate automatically, then launches
  OpenCode Desktop from the workspace root.

## Bridge safety

`toolkits/editor/bridge/OsLiveBridge/OsLiveBridge.csproj` has a `CopyToPlugins`
target that installs the DLL into Service Studio on build unless
`-p:SkipCopy=true`. ALWAYS compile via `scripts\Build-Bridge.ps1` (SkipCopy by
default). Only `-Install` deploys — requires explicit user confirmation.

## Config rules

- `opencode.json` must validate against https://opencode.ai/config.json.
- MCP tools are names `<server>_<tool>`; global disable via
  `"tools": {"outsystems-*": false}`, per-agent enable via agent file
  frontmatter. After changes, opencode must be restarted to reload config.
- Skills live in four discovery paths (root `.opencode/skills` + three
  toolkit paths); keep names collision-free across paths.

## Integrity gate

`scripts\Verify-Project.ps1` prints `OK:`/`MISSING:`/`STALE:` lines and exits
0 only when clean. Run it after any toolkit change; fix what it reports
instead of bypassing it.
