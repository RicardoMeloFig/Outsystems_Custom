<#
.SYNOPSIS
    Integrity gate for the OutSystems MCP toolkit. Hard-fails on any missing piece.

.DESCRIPTION
    Verifies that every piece of the toolkit is present on disk:
      - skills under .opencode/skills/ (auto-discovered; every subdir needs SKILL.md)
      - 3 MCP server source projects under mcp/ (extraction-only: tools/logic/ui)
      - schemas/, scripts/, tools/ directories with their key files
      - root files (opencode.json, AGENTS.md, SETUP.md, .gitignore)
      - opencode.json lists all 3 servers, each enabled, each pointing at a
        <project>/publish/<exe> path
      - (default) the 3 built exes exist at those paths

    This baseline is extraction-only. Editing lives in the
    `AI Outsystems Automation Editor` baseline (live + headless .oml).

    Exits 0 only when everything is present. Exits 1 with a clear MISSING list
    otherwise. Intended to be run right after scaffolding a derivative project
    (see New-OutSystemsProject.ps1) and after Build-All.ps1.

    AGENTS.md instructs AI agents to ABORT and fix rather than proceed when this
    script fails.

.PARAMETER Root
    Repo root to verify. Defaults to the parent of this script's folder.

.PARAMETER SourceOnly
    Skip the built-exe checks. Use to verify that a fresh clone has all the
    source (before running Build-All.ps1). Missing exes are reported as a hint
    but do not cause failure in this mode.

.PARAMETER Strict
    Also fail when a built exe is STALE (source newer than exe). By default
    staleness is reported as a warning only (exes exist but may be outdated).
    Use -Strict as a hard gate before extraction work. Stale exes silently
    produce single-module output (no MODULE: sections) and break per-module
    attribution. To rebuild stale exes, run scripts\Ensure-Built.ps1.

.EXAMPLE
    .\scripts\Verify-Project.ps1
    .\scripts\Verify-Project.ps1 -SourceOnly
    .\scripts\Verify-Project.ps1 -Strict
#>
param(
    [string]$Root = (Split-Path $PSScriptRoot -Parent),
    [switch]$SourceOnly,
    [switch]$Strict
)

$ErrorActionPreference = "Stop"
$Root = [System.IO.Path]::GetFullPath($Root)

$missing = New-Object System.Collections.Generic.List[string]
$warn    = New-Object System.Collections.Generic.List[string]
$skillCount = 0

function Test-Required($relativePath, $label) {
    $full = Join-Path $Root $relativePath
    if (-not (Test-Path -LiteralPath $full)) {
        $missing.Add("MISSING: $label -> $relativePath")
    }
}

# --- 1. Root files ---
Test-Required "opencode.json"  "root file"
Test-Required "AGENTS.md"      "root file"
Test-Required "SETUP.md"       "root file"
Test-Required ".gitignore"     "root file"

# --- 2. Skills (auto-discovered; opencode loads every .opencode/skills/* dir) ---
# Every immediate subdirectory must contain a SKILL.md. This auto-adapts when
# skills are added/removed, so there is no hardcoded list to keep in sync. A
# missing skills folder, or a skill folder without SKILL.md, is a hard failure.
$skillsRoot = Join-Path $Root ".opencode\skills"
if (Test-Path -LiteralPath $skillsRoot) {
    $skillDirs = @(Get-ChildItem -LiteralPath $skillsRoot -Directory -ErrorAction SilentlyContinue)
    if ($skillDirs.Count -eq 0) {
        $missing.Add("MISSING: no skills found under .opencode\skills\")
    } else {
        $skillCount = $skillDirs.Count
        foreach ($d in $skillDirs) {
            $sk = Join-Path $d.FullName "SKILL.md"
            if (-not (Test-Path -LiteralPath $sk)) {
                $missing.Add("MISSING: skill -> .opencode\skills\$($d.Name)\SKILL.md")
            }
        }
    }
} else {
    $missing.Add("MISSING: skills directory -> .opencode\skills")
}

# --- 3. MCP server source projects (3: extraction-only) ---
$mcpProjects = @(
    @{ Dir = "mcp\outsystems-tools";  Csproj = "OutSystemsMcp.csproj";        Exe = "OutSystemsMcp.exe" },
    @{ Dir = "mcp\outsystems-logic";  Csproj = "OutSystemsMcpLogic.csproj";   Exe = "OutSystemsMcpLogic.exe" },
    @{ Dir = "mcp\outsystems-ui";     Csproj = "OutSystemsMcpUi.csproj";      Exe = "OutSystemsMcpUi.exe" }
)
foreach ($p in $mcpProjects) {
    Test-Required "$($p.Dir)\$($p.Csproj)" "mcp source"
}

# --- 4. Schemas ---
Test-Required "schemas\metadata.schema.json"       "schema"
Test-Required "schemas\client-actions.schema.json" "schema"

# --- 5. Scripts (key files) ---
$scripts = @(
    "scripts\Build-All.ps1",
    "scripts\Verify-Project.ps1",
    "scripts\New-OutSystemsProject.ps1",
    "scripts\Get-OpenModule.ps1",
    "scripts\Parse-OmlHeader.ps1",
    "scripts\extract-ui.ps1",
    "scripts\sc-download.sh"
)
foreach ($sc in $scripts) {
    Test-Required $sc "script"
}

# --- 6. Tools (key projects) ---
$toolProjects = @(
    "tools\UiExtractor\UiExtractor.csproj",
    "tools\LiveReader\LiveReader.csproj",
    "tools\ModuleInfoExtractor\ModuleInfoExtractor.csproj",
    "tools\UiProbe\UiProbe.csproj",
    "tools\ThemeProbe\ThemeProbe.csproj",
    "tools\ApiProbe\ApiProbe.csproj",
    "tools\OmlExtractor\OmlExtractor.csproj",
    "tools\probe\probe.csproj"
)
foreach ($tp in $toolProjects) {
    Test-Required $tp "tool project"
}

# --- 7. opencode.json structure: 3 servers, enabled, publish/ paths ---
$ocPath = Join-Path $Root "opencode.json"
if (Test-Path -LiteralPath $ocPath) {
    try {
        $oc = Get-Content -LiteralPath $ocPath -Raw | ConvertFrom-Json
        $expectedServers = @("outsystems-tools", "outsystems-logic", "outsystems-ui")
        foreach ($srv in $expectedServers) {
            $entry = $oc.mcp.$srv
            if ($null -eq $entry) {
                $missing.Add("MISSING: opencode.json server entry -> mcp.$srv")
                continue
            }
            if ($entry.enabled -ne $true) {
                $missing.Add("MISSING: opencode.json mcp.$srv.enabled is not true")
            }
            $cmd = ($entry.command | Select-Object -First 1)
            if ($cmd -notmatch "/publish/[^/]+\.exe$") {
                $missing.Add("MISSING: opencode.json mcp.$srv.command must point at <project>/publish/<exe> (got: $cmd)")
            }
        }
    } catch {
        $missing.Add("MISSING: opencode.json could not be parsed as JSON -> $_")
    }
}

# --- 8. Built exes (skipped in -SourceOnly mode) + staleness check ---
# Returns the newest LastWriteTime among .cs/.csproj files in a project
# directory (excluding bin/obj/publish subdirectories).
function Get-NewestSourceTime([string]$projectDir) {
    $files = Get-ChildItem -LiteralPath $projectDir -Recurse -File -Include *.cs,*.csproj -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(bin|obj|publish)\\' }
    if (-not $files -or $files.Count -eq 0) { return [datetime]::MinValue }
    return ($files | Measure-Object -Property LastWriteTime -Maximum).Maximum
}

foreach ($p in $mcpProjects) {
    $exeRel = "$($p.Dir)\publish\$($p.Exe)"
    $exeFull = Join-Path $Root $exeRel
    if (-not (Test-Path -LiteralPath $exeFull)) {
        if ($SourceOnly) {
            $warn.Add("HINT (not built yet): $exeRel  -> run scripts\Build-All.ps1")
        } else {
            $missing.Add("MISSING: built exe -> $exeRel  (run scripts\Build-All.ps1)")
        }
        continue
    }
    # Exe exists — check staleness (source newer than exe). Skipped in
    # -SourceOnly mode (exes not expected to exist yet there).
    if ($SourceOnly) { continue }
    $projDir = Join-Path $Root $p.Dir
    $srcTime = Get-NewestSourceTime $projDir
    $exeTime = (Get-Item -LiteralPath $exeFull).LastWriteTime
    if ($srcTime -gt $exeTime) {
        $staleMsg = "STALE: $($p.Dir) (source newer than exe) -> run scripts\Ensure-Built.ps1"
        if ($Strict) {
            $missing.Add($staleMsg)
        } else {
            $warn.Add($staleMsg)
        }
    }
}

# --- Report ---
Write-Host ""
Write-Host "Verify-Project: $Root" -ForegroundColor White
Write-Host ""

if ($warn.Count -gt 0) {
    foreach ($w in $warn) { Write-Host $w -ForegroundColor Yellow }
    Write-Host ""
}

if ($missing.Count -gt 0) {
    Write-Host "FAIL: $($missing.Count) problem(s) found:" -ForegroundColor Red
    foreach ($m in $missing) { Write-Host "  $m" -ForegroundColor Red }
    Write-Host ""
    Write-Host "This project is incomplete. Do NOT proceed." -ForegroundColor Red
    Write-Host "If this is a derivative project, re-scaffold with scripts\New-OutSystemsProject.ps1" -ForegroundColor Red
    Write-Host "or start from a clean 'git clone' of the full repo (never selective file copying)." -ForegroundColor Red
    Write-Host "If exes are missing, run scripts\Build-All.ps1." -ForegroundColor Red
    exit 1
}

if ($SourceOnly) {
    Write-Host "OK (source only): all source pieces present. Build exes next: scripts\Build-All.ps1" -ForegroundColor Green
} else {
    Write-Host "OK: all skills ($skillCount), MCP source, schemas, scripts, tools, opencode.json, and built exes present." -ForegroundColor Green
    if (-not $Strict) {
        Write-Host "     (Stale-exe warnings, if any above, do not fail this check. Use -Strict to fail on stale." -ForegroundColor DarkGray
        Write-Host "      Run scripts\Ensure-Built.ps1 before extraction to guarantee fresh exes.)" -ForegroundColor DarkGray
    }
}
exit 0
