<#
.SYNOPSIS
    Scaffolds a thin "linked consumer" OutSystems project that references a baseline toolkit.

.DESCRIPTION
    Unlike New-OutSystemsProject.ps1 (which copies the FULL toolkit - MCP source,
    skills, schemas, scripts, tools - into a self-sufficient project), this script
    creates a THIN consumer project that references an existing baseline toolkit
    via absolute paths in opencode.json. Nothing from the baseline is copied: the
    three MCP exes and the skills are consumed in place.

    This is the shape used by e.g. KopaAutomation: a project that holds only
    app-specific content (OMLs, hand-written docs, notes) and points at a shared
    baseline for all tooling. When the baseline is updated or its exes are
    rebuilt, the consumer picks up the changes immediately (no sync, no copy).

    Generates at <Target>:
      - opencode.json        3 MCP servers + skills.paths, absolute paths into <Baseline>
      - .gitignore           consumer-tailored (generated output + OMLs ignored)
      - open/.gitkeep        scratch folder skeleton
      - OMLs/                empty local folder for .oml files (local-only, not tracked)
      - <AppName>.md         minimal app-notes readme (genericized template)

    Does NOT create docs/ (extraction tools create docs/<Module>/ on demand).
    Does NOT copy MCP source, skills, schemas, scripts, or tools.
    Does NOT run git init (leaves all git operations to the user).

After scaffolding, runs a readiness check: confirms the four baseline exes
and the skills exist so the consumer is usable immediately. Prints
    READY / NOT READY with fix guidance.

.PARAMETER Target
    Path to the project directory to create. Created if it doesn't exist.

.PARAMETER AppName
    Application name (e.g. "Arkki"). Used for the <AppName>.md filename and content.

.PARAMETER Baseline
    Path to the baseline toolkit repo. Defaults to the repo this script lives in
    (parent of this script's folder).

.PARAMETER Force
    Overwrite existing files without prompting.

.EXAMPLE
    .\scripts\New-LinkedProject.ps1 -Target "C:\Users\me\Documents\ArkkiAutomation" -AppName "Arkki"
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Target,
    [Parameter(Mandatory = $true)]
    [string]$AppName,
    [string]$Baseline,
    [switch]$Force
)

$ErrorActionPreference = "Stop"
# Compute default in the body (not the param block): $PSScriptRoot is not
# populated during parameter default-value evaluation in some hosts.
if ([string]::IsNullOrWhiteSpace($Baseline)) { $Baseline = Split-Path $PSScriptRoot -Parent }
$Baseline = [System.IO.Path]::GetFullPath($Baseline)
$Target   = [System.IO.Path]::GetFullPath($Target)
$BaselineFwd = $Baseline -replace '\\', '/'

# --- 1. Validate this really is a baseline toolkit ---
$mcpServers = @(
    @{ Name = "outsystems-tools";  Exe = "OutSystemsMcp.exe" },
    @{ Name = "outsystems-logic";  Exe = "OutSystemsMcpLogic.exe" },
    @{ Name = "outsystems-ui";     Exe = "OutSystemsMcpUi.exe" }
)
foreach ($srv in $mcpServers) {
    $dir = Join-Path $Baseline "mcp\$($srv.Name)"
    if (-not (Test-Path -LiteralPath $dir)) {
        Write-Error "Baseline missing MCP server: mcp\$($srv.Name) at $Baseline. New-LinkedProject.ps1 must be run from a complete baseline toolkit (use -Baseline to point at one)."
        exit 1
    }
}
$skillsDir = Join-Path $Baseline ".opencode\skills"
if (-not (Test-Path -LiteralPath $skillsDir)) {
    Write-Error "Baseline missing .opencode\skills at $Baseline. Not a valid toolkit."
    exit 1
}

Write-Host "Scaffolding linked consumer project at: $Target" -ForegroundColor Cyan
Write-Host "  baseline: $Baseline" -ForegroundColor DarkGray
Write-Host "  app name: $AppName"   -ForegroundColor DarkGray
if (-not (Test-Path -LiteralPath $Target)) {
    New-Item -ItemType Directory -Path $Target -Force | Out-Null
}

function Write-File {
    param([string]$Path, [string]$Content)
    $dir = Split-Path $Path -Parent
    if (-not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    if ((Test-Path -LiteralPath $Path) -and -not $Force) {
        Write-Host "  skip (exists, no -Force): $Path" -ForegroundColor DarkYellow
        return
    }
    [System.IO.File]::WriteAllText($Path, $Content)
    Write-Host "  wrote: $Path" -ForegroundColor Gray
}

# --- 2. opencode.json (absolute paths into baseline) ---
$ocTemplate = @'
{
  "$schema": "https://opencode.ai/config.json",
  "mcp": {
    "outsystems-tools": {
      "type": "local",
      "command": [
        "__BASELINE__/mcp/outsystems-tools/publish/OutSystemsMcp.exe"
      ],
      "enabled": true
    },
    "outsystems-logic": {
      "type": "local",
      "command": [
        "__BASELINE__/mcp/outsystems-logic/publish/OutSystemsMcpLogic.exe"
      ],
      "enabled": true
    },
    "outsystems-ui": {
      "type": "local",
      "command": [
        "__BASELINE__/mcp/outsystems-ui/publish/OutSystemsMcpUi.exe"
      ],
      "enabled": true
    },
    "outsystems": {
      "type": "remote",
      "url": "https://YOUR-TENANT.outsystems.dev/mcp",
      "enabled": false
    }
  },
  "skills": {
    "paths": ["__BASELINE__/.opencode/skills"]
  }
}
'@
$oc = $ocTemplate.Replace('__BASELINE__', $BaselineFwd)
Write-File -Path (Join-Path $Target "opencode.json") -Content $oc

# --- 3. .gitignore (consumer-tailored) ---
$gitignore = @'
# IDE / editor
*.user
.vs/
.idea/

# Local environment (never commit)
.env

# Generated extraction output (docs/<ModuleName>/)
# Hand-written docs (docs/*.md) are still tracked.
docs/*/

# OutSystems binaries (OMLs kept locally, not tracked)
*.oml
*.oap

# Logs & local debug screenshots
*.log
*.png
*.jpg
*.jpeg
*.bmp
*.gif

# OS files
Thumbs.db
Desktop.ini
.DS_Store

# Local drop / generated output dirs (folder skeleton kept via .gitkeep)
open/*
!open/.gitkeep
'@
Write-File -Path (Join-Path $Target ".gitignore") -Content $gitignore

# --- 4. open/.gitkeep ---
$openDir = Join-Path $Target "open"
if (-not (Test-Path -LiteralPath $openDir)) {
    New-Item -ItemType Directory -Path $openDir -Force | Out-Null
}
$gitkeep = Join-Path $openDir ".gitkeep"
if (-not (Test-Path -LiteralPath $gitkeep) -or $Force) {
    [System.IO.File]::WriteAllText($gitkeep, "")
    Write-Host "  wrote: open/.gitkeep" -ForegroundColor Gray
}

# --- 5. OMLs/ (local-only, no .gitkeep) ---
$omlsDir = Join-Path $Target "OMLs"
if (-not (Test-Path -LiteralPath $omlsDir)) {
    New-Item -ItemType Directory -Path $omlsDir -Force | Out-Null
    Write-Host "  created: OMLs/ (local-only)" -ForegroundColor Gray
} else {
    Write-Host "  exists:  OMLs/" -ForegroundColor DarkGray
}

# --- 6. <AppName>.md (app-notes readme) ---
$mdTemplate = @'
# __APPNAME__ Automation

This project contains **__APPNAME__-specific content only**. It consumes the shared
OutSystems automation toolkit (MCP servers, skills, schemas, scripts, tools,
runbook) from the baseline repo via absolute paths in `opencode.json`.

## Baseline toolkit (single source of truth)

**Location:** `__BASELINE__`
**Remote:** https://github.com/RicardoMeloFig/OutsystemsAIAutomation.git

When the baseline is updated or exes are rebuilt there, this project picks up
the changes immediately (no sync, no copy). If you change PC or move the
baseline folder, update only the absolute paths in `opencode.json` and this file.

## Canonical runbook

Read FIRST, every session:
`__BASELINE__\AGENTS.md`

The baseline `AGENTS.md` is the single source of truth for tool usage, MCP
workflows, extraction, staleness gate, and editor operations. `SETUP.md` (same
folder) covers initial setup. This file records only __APPNAME__-specific notes.

## __APPNAME__ modules

The __APPNAME__ application (OMLs in `./OMLs/`, all locally kept - not git-tracked):

| Module | Type |
|--------|------|
| <!-- Add modules as you work with them. --> | |

## Conventions

- Extraction output goes under `./docs/<ModuleName>/` (gitignored; regenerated).
- Hand-written __APPNAME__ docs (`./docs/*.md`) are tracked.
- Local env config: `./.env` (gitignored; copy template from baseline `.env.example`).
- Open-module drop folder: `./open/` (gitkeep only).

## Building / verifying the toolkit

The MCP exes live in the baseline repo (not copied here). After a baseline
update or a fresh setup, rebuild from the baseline folder:

```powershell
__BASELINE__\scripts\Ensure-Built.ps1
```

Verify the baseline toolkit (must exit 0):

```powershell
__BASELINE__\scripts\Verify-Project.ps1
```
'@
$appMd = $mdTemplate.Replace('__BASELINE__', $Baseline).Replace('__APPNAME__', $AppName)
Write-File -Path (Join-Path $Target "$AppName.md") -Content $appMd

# --- 7. Readiness check ---
Write-Host ""
Write-Host "Readiness check:" -ForegroundColor Cyan

$missingExes = New-Object System.Collections.Generic.List[string]
foreach ($srv in $mcpServers) {
    $exe = Join-Path $Baseline "mcp\$($srv.Name)\publish\$($srv.Exe)"
    if (Test-Path -LiteralPath $exe) {
        Write-Host "  OK   mcp\$($srv.Name)\publish\$($srv.Exe)" -ForegroundColor DarkGreen
    } else {
        Write-Host "  MISS mcp\$($srv.Name)\publish\$($srv.Exe)" -ForegroundColor Red
        $missingExes.Add($srv.Name)
    }
}

# Skills are auto-discovered by opencode; verify every subdir has a SKILL.md.
$missingSkills = New-Object System.Collections.Generic.List[string]
$skillDirs = @(Get-ChildItem -LiteralPath $skillsDir -Directory -ErrorAction SilentlyContinue)
$skillCount = 0
if ($skillDirs.Count -eq 0) {
    $missingSkills.Add("(no skill folders found)")
} else {
    foreach ($d in $skillDirs) {
        if (-not (Test-Path -LiteralPath (Join-Path $d.FullName "SKILL.md"))) {
            $missingSkills.Add($d.Name)
        }
    }
    $skillCount = $skillDirs.Count
}
if ($missingSkills.Count -eq 0) {
    Write-Host "  OK   .opencode\skills ($skillCount skills)" -ForegroundColor DarkGreen
} else {
    Write-Host "  MISS .opencode\skills: $($missingSkills -join ', ')" -ForegroundColor Red
}

# --- Report ---
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Scaffold complete: $Target" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
if ($missingExes.Count -eq 0 -and $missingSkills.Count -eq 0) {
    Write-Host "  READY: baseline exes + skills found. You can launch now." -ForegroundColor Green
} else {
    Write-Host "  NOT READY: some baseline pieces are missing (see above)." -ForegroundColor Red
    if ($missingExes.Count -gt 0) {
        Write-Host "  Build the baseline exes first:" -ForegroundColor Yellow
        Write-Host "    $Baseline\scripts\Build-All.ps1" -ForegroundColor Yellow
    }
}
Write-Host ""
Write-Host "  Next steps:"
Write-Host "    1. Drop your .oml files into: $Target\OMLs\"
Write-Host "    2. Open the module(s) in Service Studio (wait until fully loaded)."
Write-Host "    3. Launch opencode from the project root:  cd $Target ; opencode"
Write-Host "    4. Run /mcp inside opencode - the 3 local servers must connect"
Write-Host "       (the 4th, official 'outsystems' ODC server, is a disabled placeholder;" -ForegroundColor Gray
Write-Host "        enable it in opencode.json only with a tenant-allowlisted ODC host)." -ForegroundColor Gray
Write-Host ""
Write-Host "  (No git init performed - initialize git yourself when ready:  git init)"
Write-Host ""
exit 0
