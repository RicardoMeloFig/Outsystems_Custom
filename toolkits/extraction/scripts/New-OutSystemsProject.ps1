<#
.SYNOPSIS
    Scaffolds a derivative OutSystems MCP project from this toolkit.

.DESCRIPTION
    Copies the FULL toolkit into a target directory so the result is a
    self-sufficient project, not a partial skeleton. Deploys:
      - all 3 MCP server SOURCE projects (mcp/outsystems-{tools,logic,ui}; extraction-only)
      - all skills (.opencode/skills/*, auto-discovered)
      - schemas/, scripts/, tools/, references/ (generated ground-of-truth catalog;
        machine-specific source-path.txt excluded)
      - root files: AGENTS.md, SETUP.md, opencode.json, .gitignore
      - optionally the built exes (publish/ folders) when -CopyExes is set

    Then runs Verify-Project.ps1 -SourceOnly against the target to confirm
    nothing was dropped, and prints the next steps (Build-All.ps1, then
    Verify-Project.ps1).

    This replaces an older version of the script that only deployed 2 servers
    and omitted the editor, the UI server, schemas, scripts, and tools. That
    omission was the root cause of derivative projects (e.g. ArkkiAutomation)
    being missing pieces.

    IMPORTANT for AI agents: the preferred way to create a derivative project
    is `git clone` (or `git archive`) of the full repo, NOT selective file
    writing. This script exists for cases where a clone is not possible. See
    AGENTS.md "Creating derivative projects".

.PARAMETER Target
    Path to the project directory to deploy into. Created if it doesn't exist.

.PARAMETER Force
    Overwrite existing files without prompting.

.PARAMETER CopyExes
    Also copy the publish/ (built exe) folders from this machine. Only useful
    when this machine has already run Build-All.ps1 and the target runs the
    same OS/arch. Default: off (the target rebuilds via Build-All.ps1).

.EXAMPLE
    .\scripts\New-OutSystemsProject.ps1 -Target "C:\Users\me\Documents\ArkkiAutomation"
    .\scripts\New-OutSystemsProject.ps1 -Target . -Force -CopyExes
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Target,
    [switch]$Force,
    [switch]$CopyExes
)

$ErrorActionPreference = "Stop"
$Source = Split-Path $PSScriptRoot -Parent
$Target = [System.IO.Path]::GetFullPath($Target)

# Build-output folders to never copy (they are gitignored and machine-specific).
$ExcludeDirs = @("\bin\", "\obj\", "\publish\")

function Copy-TreeExcludingBuild {
    param([string]$From, [string]$To)
    if (-not (Test-Path -LiteralPath $From)) { return }
    if (-not (Test-Path -LiteralPath $To)) {
        New-Item -ItemType Directory -Path $To -Force | Out-Null
    }
    Get-ChildItem -LiteralPath $From -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($From.Length)
        foreach ($ex in $ExcludeDirs) {
            if ($rel -like "*$ex*") { return }
        }
        $dest = Join-Path $To $rel.TrimStart('\')
        $destDir = Split-Path $dest -Parent
        if (-not (Test-Path -LiteralPath $destDir)) {
            New-Item -ItemType Directory -Path $destDir -Force | Out-Null
        }
        Copy-Item -LiteralPath $_.FullName -Destination $dest -Force:$Force
    }
}

function Copy-File {
    param([string]$From, [string]$To)
    if (-not (Test-Path -LiteralPath $From)) {
        Write-Error "Source file not found: $From"
        exit 1
    }
    $destDir = Split-Path $To -Parent
    if (-not (Test-Path -LiteralPath $destDir)) {
        New-Item -ItemType Directory -Path $destDir -Force | Out-Null
    }
    Copy-Item -LiteralPath $From -Destination $To -Force:$Force
}

# --- validate source has all 3 MCP projects (extraction-only) ---
$mcpServers = @("outsystems-tools", "outsystems-logic", "outsystems-ui")
foreach ($srv in $mcpServers) {
    $dir = Join-Path $Source "mcp\$srv"
    if (-not (Test-Path -LiteralPath $dir)) {
        Write-Error "Source missing MCP server: mcp\$srv (is this run from a complete toolkit?)"
        exit 1
    }
}

Write-Host "Scaffolding derivative project at: $Target" -ForegroundColor Cyan
if (-not (Test-Path -LiteralPath $Target)) {
    New-Item -ItemType Directory -Path $Target -Force | Out-Null
}

# --- 1. MCP source projects (+ publish folders if -CopyExes) ---
foreach ($srv in $mcpServers) {
    Write-Host "  copying mcp\$srv (source)..." -ForegroundColor Gray
    Copy-TreeExcludingBuild -From (Join-Path $Source "mcp\$srv") -To (Join-Path $Target "mcp\$srv")
    if ($CopyExes) {
        $pubSrc = Join-Path $Source "mcp\$srv\publish"
        if (Test-Path -LiteralPath $pubSrc) {
            $pubDst = Join-Path $Target "mcp\$srv\publish"
            New-Item -ItemType Directory -Path $pubDst -Force | Out-Null
            Copy-Item -Path "$pubSrc\*" -Destination $pubDst -Recurse -Force:$Force
            Write-Host "    + publish\ (built exes)" -ForegroundColor DarkGray
        } else {
            Write-Host "    ! no publish\ on source; target will need Build-All.ps1" -ForegroundColor DarkYellow
        }
    }
}

# --- 2. Skills (all 6) ---
$skillsDir = Join-Path $Source ".opencode\skills"
if (Test-Path -LiteralPath $skillsDir) {
    Copy-TreeExcludingBuild -From $skillsDir -To (Join-Path $Target ".opencode\skills")
    Write-Host "  copied .opencode\skills" -ForegroundColor Gray
}

# --- 3. schemas, scripts, tools ---
foreach ($top in @("schemas", "scripts", "tools")) {
    $srcTop = Join-Path $Source $top
    if (Test-Path -LiteralPath $srcTop) {
        Copy-TreeExcludingBuild -From $srcTop -To (Join-Path $Target $top)
        Write-Host "  copied $top" -ForegroundColor Gray
    }
}

# --- 3b. references (generated ground-of-truth catalog; NOT the machine-specific source-path.txt) ---
$refSrc = Join-Path $Source "references"
if (Test-Path -LiteralPath $refSrc) {
    Copy-TreeExcludingBuild -From $refSrc -To (Join-Path $Target "references")
    $spFile = Join-Path $Target "references\source-path.txt"
    if (Test-Path -LiteralPath $spFile) { Remove-Item -LiteralPath $spFile -Force }
    Write-Host "  copied references (source-path.txt excluded - machine-specific)" -ForegroundColor Gray
}

# --- 4. docs (template only) ---
$docsSrc = Join-Path $Source "docs"
if (Test-Path -LiteralPath $docsSrc) {
    Copy-TreeExcludingBuild -From $docsSrc -To (Join-Path $Target "docs")
    Write-Host "  copied docs" -ForegroundColor Gray
}

# --- 5. root files ---
foreach ($f in @("AGENTS.md", "SETUP.md", "opencode.json", ".gitignore")) {
    $srcFile = Join-Path $Source $f
    if (Test-Path -LiteralPath $srcFile) {
        Copy-File -From $srcFile -To (Join-Path $Target $f)
        Write-Host "  copied $f" -ForegroundColor Gray
    }
}

# --- 6. Verify source completeness ---
Write-Host ""
Write-Host "Running Verify-Project.ps1 -SourceOnly against target..." -ForegroundColor Cyan
$verify = Join-Path $Target "scripts\Verify-Project.ps1"
if (Test-Path -LiteralPath $verify) {
    & $verify -Root $Target -SourceOnly
    $verifyExit = $LASTEXITCODE
} else {
    Write-Host "  (Verify-Project.ps1 not found in target; skipping)" -ForegroundColor DarkYellow
    $verifyExit = 0
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Scaffold complete: $Target" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Next steps (run from the target directory):"
Write-Host "    1. dotnet SDK 10 must be installed (dotnet --version)"
Write-Host "    2. .\scripts\Build-All.ps1            # builds the 3 MCP exes"
Write-Host "    3. .\scripts\Verify-Project.ps1        # hard-fail gate (must pass)"
Write-Host "    4. opencode                            # launch from target root"
Write-Host ""
if ($verifyExit -ne 0) {
    Write-Host "  WARNING: source verification reported missing pieces above." -ForegroundColor Red
    Write-Host "  Do NOT use the project until Verify-Project.ps1 passes." -ForegroundColor Red
    exit $verifyExit
}
if (-not $CopyExes) {
    Write-Host "  (Exes were not copied. Build-All.ps1 in step 2 creates them.)" -ForegroundColor DarkYellow
}
exit 0
