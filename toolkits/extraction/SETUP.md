# SETUP.md — First-time setup on a new PC

This guide gets the OutSystems **extraction** toolkit running after a fresh
`git clone` on a new machine. It exists because the compiled MCP server binaries
are **gitignored** (see `.gitignore` lines `bin/`, `obj/`, `publish/`), so they
are NOT in the repo and must be rebuilt locally.

If your MCP servers show as **inactive/disabled** in opencode right after a
clone, that means the `.exe` files don't exist yet — follow this guide.

> This baseline is **extraction-only** (ClrMD readers). **Editing** (live
> in-process + headless `.oml`) lives in the separate
> `AI Outsystems Automation Editor` baseline — set it up independently.

---

## Why the MCPs are inactive after a fresh clone

`opencode.json` points the three MCP servers at compiled `.exe` files that
live inside gitignored build-output folders:

| Server | Expected exe path | Target framework |
|---|---|---|
| `outsystems-tools` | `mcp/outsystems-tools/publish/OutSystemsMcp.exe` | net8.0 |
| `outsystems-logic` | `mcp/outsystems-logic/publish/OutSystemsMcpLogic.exe` | net8.0 |
| `outsystems-ui` | `mcp/outsystems-ui/publish/OutSystemsMcpUi.exe` | net8.0 |

All three use the same `<project>/publish/` convention, kept in sync by
`scripts/Build-All.ps1`. `git clone` only downloads the C# source. The `.exe`
files don't exist, so opencode cannot launch them and the servers appear
inactive. Rebuild them with the steps below.

---

## Prerequisites on the new PC

- **Git** — https://git-scm.com
- **.NET 8+ SDK** — `dotnet --list-sdks` must show an `8.0.x` (or newer) entry.
  All three MCP servers are `net8.0` (ClrMD). No FlaUI/Ollama/.NET 10 needed
  here (those belong to the Editor baseline).
- **opencode CLI** — https://opencode.ai
- **OutSystems Service Studio 11** — installed and able to open your module.
  Required at runtime (the extractors read its process memory via ClrMD).
- **Windows** (x64).

---

## Step-by-step

### 1. Clone the repo

```powershell
git clone https://github.com/RicardoMeloFig/OutsystemsAIAutomation.git
cd OutsystemsAIAutomation
```

> If your local folder is named differently (e.g. `Outsystem Automation`),
> that's fine — all paths below are relative to the repo root.

### 2. Build (publish) all three MCP servers

Run the build script from the repo root. It publishes every server to a
normalized `<project>/publish/` folder, matching the paths in `opencode.json`.

```powershell
.\scripts\Build-All.ps1
```

For a PC with **no .NET runtime installed**, build self-contained (bundles
the runtime, larger output):

```powershell
.\scripts\Build-All.ps1 -SelfContained
```

The script fails (non-zero exit) if any server fails to publish. The exact
`dotnet publish` invocations are in `scripts/Build-All.ps1` if you need to
run one manually.

### 3. Verify the toolkit is complete

Run the integrity gate — it checks all 7 skills, the 3 MCP source projects,
`schemas/`, `scripts/`, `tools/`, the root files, `opencode.json` structure,
and the 3 built exes:

```powershell
.\scripts\Verify-Project.ps1
```

It must print `OK:` and exit 0. If it lists `MISSING:` lines, fix them
before launching opencode (the message tells you what to do). To check
**source only** before building, use `.\scripts\Verify-Project.ps1 -SourceOnly`.

The three built exes it requires:

```powershell
Test-Path mcp/outsystems-tools/publish/OutSystemsMcp.exe
Test-Path mcp/outsystems-logic/publish/OutSystemsMcpLogic.exe
Test-Path mcp/outsystems-ui/publish/OutSystemsMcpUi.exe
```

### 4. Smoke-test each server (optional but recommended)

Each server should start and wait silently on stdin (MCP runs over stdio).
Press `Ctrl+C` to exit. If any prints a missing-runtime or DLL error, see
the Troubleshooting section below.

```powershell
& ./mcp/outsystems-tools/publish/OutSystemsMcp.exe
& ./mcp/outsystems-logic/publish/OutSystemsMcpLogic.exe
& ./mcp/outsystems-ui/publish/OutSystemsMcpUi.exe
```

### 5. Launch opencode from the repo root

opencode reads the local `opencode.json`, so you MUST launch it from the
repo root (not from a subfolder):

```powershell
opencode
```

Inside opencode, run `/mcp` to confirm all three servers connect. They
should show as active/connected.

### 5b. Pre-launch staleness check (recommended habit)

The built exes go **stale** whenever the MCP C# source (`mcp/*/Program.cs`)
is edited but not re-published — for example after a `git pull`. Stale exes
silently produce single-module output (no `MODULE:` sections) and break
per-module attribution. The AI inside opencode cannot rebuild them while
opencode is running (the DLLs are locked), which forces a mid-session restart.

To avoid this, run the staleness gate **before** launching opencode:

```powershell
.\scripts\Ensure-Built.ps1
opencode
```

When all exes are fresh, `Ensure-Built.ps1` is a ~1-second no-op. When stale,
it rebuilds them cleanly (no locks, since opencode isn't running yet). This
turns a disruptive mid-session restart into a silent pre-launch fix.

**Optional launch alias** — add to your PowerShell profile (`$PROFILE`) so the
staleness check runs automatically every time you start the toolkit:

```powershell
function Start-OSAuto {
    .\scripts\Ensure-Built.ps1
    if ($LASTEXITCODE -eq 0) { opencode }
}
```

Then just run `Start-OSAuto` from the repo root.

### 6. Configure your local module name (for the UiExtractor tool only)

This is NOT needed for the MCP servers themselves — only for the standalone
UiExtractor under `tools/`:

```powershell
Copy-Item .env.example .env
# Edit .env and set:  OUTSYSTEMS_MODULE=<your-module-name>
```

`.env` is gitignored, so your module name never enters version control.

### 7. Open your module in Service Studio

The extractors (`outsystems-tools_*`, `outsystems-logic_*`, `outsystems-ui_*`)
need the target module **open and fully loaded into memory**. UI focus is not
required. Open the module manually in SS (DevEnv "Recent modules" → select →
Enter). Editing the module is done via the Editor baseline, not here.

See `AGENTS.md` for the full prerequisite matrix.

---

## Generating a derivative project (ArkkiAutomation, etc.)

Use this toolkit as the base for a new project. Two methods, in order of
preference. Both end with the same build + verify + launch sequence.

### Method A — clone + rename (preferred)

Guarantees every tracked file is present. Then edit/rename to taste.

```powershell
git clone https://github.com/RicardoMeloFig/OutsystemsAIAutomation.git ArkkiAutomation
cd ArkkiAutomation
.\scripts\Build-All.ps1          # builds the 3 gitignored MCP exes
.\scripts\Verify-Project.ps1     # hard-fail gate; MUST print OK
opencode                         # launch from the new repo root
```

### Method B — scaffold with New-OutSystemsProject.ps1 (when a clone is not possible)

Copies the full toolkit — all 3 MCP server source projects, all 7 skills,
`schemas/`, `scripts/`, `tools/`, and the root files — then runs
`Verify-Project.ps1 -SourceOnly` against the target. Run it from THIS repo's
root:

```powershell
.\scripts\New-OutSystemsProject.ps1 -Target "C:\Users\me\Documents\ArkkiAutomation"
cd C:\Users\me\Documents\ArkkiAutomation
.\scripts\Build-All.ps1
.\scripts\Verify-Project.ps1     # hard-fail gate; MUST print OK
opencode
```

Add `-CopyExes` to the scaffold command to also copy the built `publish/`
folders from this machine (skips `Build-All.ps1` on the target; only valid
when both PCs are the same OS/arch).

### Method C — linked consumer (thin project, no copy)

When the baseline toolkit is **already built on this machine** and you want a
thin per-app project that references it (no MCP source/skills/scripts copied),
scaffold a linked consumer. `opencode.json` will point at the baseline's exes
and skills via absolute paths, so the consumer picks up baseline updates
immediately. Run from THIS repo's root:

```powershell
.\scripts\New-LinkedProject.ps1 -Target "C:\Users\me\Documents\ArkkiAutomation" -AppName "Arkki"
```

The consumer does **not** need `Build-All.ps1` (the exes come from the
baseline). The script runs a readiness check at the end - if it reports missing
exes, build the baseline first: `.\scripts\Build-All.ps1`. Then launch from the
new project root:

```powershell
cd C:\Users\me\Documents\ArkkiAutomation
opencode
```

### Hard rule

`scripts\Verify-Project.ps1` MUST exit 0 before the new project is used. If it
lists `MISSING:` lines, fix them — do not proceed, do not work around them.
**Never** create a derivative by selectively writing files from memory; that is
how pieces silently go missing (skills, an MCP server, schemas, scripts, tools).
See `AGENTS.md` "Creating derivative projects" for the agent-facing rule.

---

## Troubleshooting

### A server fails to start with a framework/runtime error
You're likely missing the runtime for that TFM. Easiest fix: rebuild all
servers self-contained so each bundles its own runtime:

```powershell
.\scripts\Build-All.ps1 -SelfContained
```

Self-contained exes run on a PC with **no .NET SDK/runtime installed**, at
the cost of larger output folders. The `<project>/publish/` paths stay
aligned with `opencode.json`.

### `dotnet publish` says the SDK is missing
Install the **.NET 8+ SDK** (`dotnet --list-sdks` must show `8.0.x`+).

### MCPs still inactive after building
- Confirm you launched opencode from the **repo root** (where `opencode.json` lives).
- Run `.\scripts\Verify-Project.ps1` — it lists exactly what is missing.
- Restart opencode (it only reads `opencode.json` at startup).
- Check that `opencode.json` still lists all three servers with `"enabled": true`.

### Creating a derivative project (ArkkiAutomation, etc.)
See the "Generating a derivative project" section above for the full
step-by-step (clone+rename, or `New-OutSystemsProject.ps1`, then build +
verify + launch).

---

## Quick reference (copy-paste block)

```powershell
# From repo root, after git clone:
.\scripts\Build-All.ps1          # builds all 3 MCP exes into <project>/publish/
.\scripts\Verify-Project.ps1     # hard-fail gate; must print OK

# Launch:
opencode
```
