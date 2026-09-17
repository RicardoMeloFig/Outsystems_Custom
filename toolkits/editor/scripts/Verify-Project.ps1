<#
.SYNOPSIS
    Integrity gate for the headless .oml editor baseline. Hard-fails on any missing piece.

.DESCRIPTION
    Verifies that every piece of the editor toolkit is present on disk:
      - skills under .opencode/skills/ (auto-discovered; every subdir needs SKILL.md)
      - the outsystems-omleditor MCP source project under mcp/
      - schemas/, scripts/, docs/ directories with their key files
      - root files (opencode.json, AGENTS.md, SETUP.md, .gitignore)
      - opencode.json lists outsystems-omleditor, enabled, pointing at a
        <project>/publish/<exe> path
      - (default) the built exe exists at that path

    Exits 0 only when everything is present. Exits 1 with a clear MISSING list
    otherwise. Run after Build-All.ps1.

.PARAMETER Root
    Repo root to verify. Defaults to the parent of this script's folder.

.PARAMETER SourceOnly
    Skip the built-exe check. Use to verify a fresh clone has all source before
    running Build-All.ps1.

.PARAMETER Strict
    Also fail when the built exe is STALE (source newer than exe).

.EXAMPLE
    .\scripts\Verify-Project.ps1
    .\scripts\Verify-Project.ps1 -SourceOnly
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
    if (-not (Test-Path -LiteralPath $full)) { $missing.Add("MISSING: $label -> $relativePath") }
}

# --- 1. Root files ---
Test-Required "opencode.json"  "root file"
Test-Required "AGENTS.md"      "root file"
Test-Required "SETUP.md"       "root file"
Test-Required ".gitignore"     "root file"

# --- 2. Skills (auto-discovered; every subdir needs SKILL.md) ---
$skillsRoot = Join-Path $Root ".opencode\skills"
if (Test-Path -LiteralPath $skillsRoot) {
    $skillDirs = @(Get-ChildItem -LiteralPath $skillsRoot -Directory -ErrorAction SilentlyContinue)
    if ($skillDirs.Count -eq 0) {
        $missing.Add("MISSING: no skills found under .opencode\skills\")
    } else {
        $skillCount = $skillDirs.Count
        foreach ($d in $skillDirs) {
            $sk = Join-Path $d.FullName "SKILL.md"
            if (-not (Test-Path -LiteralPath $sk)) { $missing.Add("MISSING: skill -> .opencode\skills\$($d.Name)\SKILL.md") }
        }
    }
} else {
    $missing.Add("MISSING: skills directory -> .opencode\skills")
}

# --- 3. MCP server source projects (headless .oml editor + live in-process editor) ---
$mcpProjects = @(
    @{ Dir = "mcp\outsystems-omleditor";  Csproj = "OutSystemsMcpOmlEditor.csproj";  Exe = "OutSystemsMcpOmlEditor.exe" },
    @{ Dir = "mcp\outsystems-liveeditor"; Csproj = "OutSystemsMcpLiveEditor.csproj"; Exe = "OutSystemsMcpLiveEditor.exe" }
)
foreach ($p in $mcpProjects) { Test-Required "$($p.Dir)\$($p.Csproj)" "mcp source" }

# --- 4. Docs ---
Test-Required "docs\oml-editing-reference.md" "doc"
Test-Required "docs\project_map.md"           "doc"
Test-Required "docs\element-class-reference.md" "doc"
Test-Required "docs\ui-styling-reference.md"  "doc"
Test-Required "docs\web-block-editing.md"     "doc"

# --- 5. Scripts (key files) ---
$scripts = @(
    "scripts\Build-All.ps1",
    "scripts\Verify-Project.ps1",
    "scripts\Ensure-Built.ps1",
    "scripts\start-opencode.ps1",
    "scripts\Get-OmlPath.ps1",
    "scripts\Parse-OmlHeader.ps1",
    "scripts\Send-BridgeCmd.ps1",
    "scripts\Open-OmlInSS.ps1",
    "scripts\Get-OsuiVars.ps1",
    "scripts\Test-SkillLockstep.ps1"
)
foreach ($sc in $scripts) { Test-Required $sc "script" }

# --- 6. opencode.json structure: 1 server, enabled, publish/ path ---
$ocPath = Join-Path $Root "opencode.json"
if (Test-Path -LiteralPath $ocPath) {
    try {
        $oc = Get-Content -LiteralPath $ocPath -Raw | ConvertFrom-Json
        $expectedServers = @("outsystems-omleditor", "outsystems-liveeditor")
        foreach ($srv in $expectedServers) {
            $entry = $oc.mcp.$srv
            if ($null -eq $entry) { $missing.Add("MISSING: opencode.json server entry -> mcp.$srv"); continue }
            if ($entry.enabled -ne $true) { $missing.Add("MISSING: opencode.json mcp.$srv.enabled is not true") }
            $cmd = ($entry.command | Select-Object -First 1)
            if ($cmd -notmatch "/publish/[^/]+\.exe$") {
                $missing.Add("MISSING: opencode.json mcp.$srv.command must point at <project>/publish/<exe> (got: $cmd)")
            }
        }
    } catch {
        $missing.Add("MISSING: opencode.json could not be parsed as JSON -> $_")
    }
}

# --- 7. Built exe (+ staleness) ---
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
        if ($SourceOnly) { $warn.Add("HINT (not built yet): $exeRel  -> run scripts\Build-All.ps1") }
        else { $missing.Add("MISSING: built exe -> $exeRel  (run scripts\Build-All.ps1)") }
        continue
    }
    if ($SourceOnly) { continue }
    $projDir = Join-Path $Root $p.Dir
    $srcTime = Get-NewestSourceTime $projDir
    $exeTime = (Get-Item -LiteralPath $exeFull).LastWriteTime
    if ($srcTime -gt $exeTime) {
        $staleMsg = "STALE: $($p.Dir) (source newer than exe) -> run scripts\Ensure-Built.ps1"
        if ($Strict) { $missing.Add($staleMsg) } else { $warn.Add($staleMsg) }
    }
}

# --- Report ---
Write-Host ""
Write-Host "Verify-Project: $Root" -ForegroundColor White
Write-Host ""
if ($warn.Count -gt 0) { foreach ($w in $warn) { Write-Host $w -ForegroundColor Yellow }; Write-Host "" }
if ($missing.Count -gt 0) {
    Write-Host "FAIL: $($missing.Count) problem(s) found:" -ForegroundColor Red
    foreach ($m in $missing) { Write-Host "  $m" -ForegroundColor Red }
    Write-Host ""
    Write-Host "This project is incomplete. Do NOT proceed." -ForegroundColor Red
    Write-Host "If exes are missing, run scripts\Build-All.ps1." -ForegroundColor Red
    exit 1
}
if ($SourceOnly) {
    Write-Host "OK (source only): all source pieces present. Build exe next: scripts\Build-All.ps1" -ForegroundColor Green
} else {
    Write-Host "OK: all skills ($skillCount), MCP source, docs, scripts, opencode.json, and built exe present." -ForegroundColor Green
    if (-not $Strict) {
        Write-Host "     (Stale-exe warnings, if any above, do not fail this check. Use -Strict to fail on stale." -ForegroundColor DarkGray
        Write-Host "      Run scripts\Ensure-Built.ps1 before editing to guarantee a fresh exe.)" -ForegroundColor DarkGray
    }
}
exit 0
