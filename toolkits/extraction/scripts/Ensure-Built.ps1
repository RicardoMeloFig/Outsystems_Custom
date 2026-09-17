<#
.SYNOPSIS
    Staleness gate for the OutSystems MCP exes. Rebuilds stale servers on demand.

.DESCRIPTION
    For each of the 3 MCP servers, compares the newest source file (.cs/.csproj)
    timestamp against the built exe's LastWriteTime. If the source is newer, the
    exe is STALE and this script attempts a targeted `dotnet publish` for just
    that server.

    This is the gate that Verify-Project.ps1 does NOT enforce by default
    (Verify-Project only checks that exes EXIST, not that they are fresh). Run
    this before any extraction work to guarantee the per-module ownerESpace
    attribution code is actually compiled into the running exes.

    Output is one line per server:
        FRESH        outsystems-tools   -> <exe path>
        REBUILT      outsystems-logic   -> <exe path>
        STALE-LOCKED outsystems-ui      (close opencode and re-run)
        MISSING      outsystems-tools   (no exe and publish failed)

    Exit codes:
        0 = all servers FRESH or REBUILT (safe to proceed with extraction)
        1 = one or more servers STALE-LOCKED or MISSING (do NOT proceed)

    When exit 1 is returned, the script prints the exact restart instructions
    the user/AI should follow.

.PARAMETER Server
    Rebuild only the named server (e.g. "outsystems-tools"). Default: all 3.

.PARAMETER CheckOnly
    Report staleness without attempting a rebuild. Useful for diagnostics.

.EXAMPLE
    .\scripts\Ensure-Built.ps1
    .\scripts\Ensure-Built.ps1 -Server outsystems-ui
    .\scripts\Ensure-Built.ps1 -CheckOnly
#>
param(
    [string]$Server,
    [switch]$CheckOnly
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path $PSScriptRoot -Parent

# Server -> (project relative path, output exe name). Must match Build-All.ps1
# and opencode.json exactly.
$Servers = @(
    @{ Name = "outsystems-tools";  Project = "mcp\outsystems-tools\OutSystemsMcp.csproj";        Exe = "OutSystemsMcp.exe" },
    @{ Name = "outsystems-logic";  Project = "mcp\outsystems-logic\OutSystemsMcpLogic.csproj";   Exe = "OutSystemsMcpLogic.exe" },
    @{ Name = "outsystems-ui";     Project = "mcp\outsystems-ui\OutSystemsMcpUi.csproj";         Exe = "OutSystemsMcpUi.exe" }
)

if ($Server) {
    $Servers = $Servers | Where-Object { $_.Name -eq $Server }
    if ($Servers.Count -eq 0) {
        Write-Host "ERROR: unknown server '$Server'. Valid: outsystems-tools, outsystems-logic, outsystems-ui" -ForegroundColor Red
        exit 1
    }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue) -and -not $CheckOnly) {
    Write-Host "ERROR: dotnet not found on PATH. Install the .NET 8+ SDK from https://dotnet.microsoft.com" -ForegroundColor Red
    exit 1
}

# Detect whether opencode (or any OutSystemsMcp process) is running and holding
# the publish DLLs open. A locked exe cannot be overwritten by dotnet publish.
$running = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like "OutSystemsMcp*" })
$opencodeRunning = $running.Count -gt 0

# Returns the newest LastWriteTime among .cs/.csproj files in the project
# directory (excluding bin/obj/publish subdirectories).
function Get-NewestSourceTime([string]$projectDir) {
    $files = Get-ChildItem -LiteralPath $projectDir -Recurse -File -Include *.cs,*.csproj -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(bin|obj|publish)\\' }
    if (-not $files -or $files.Count -eq 0) { return [datetime]::MinValue }
    return ($files | Measure-Object -Property LastWriteTime -Maximum).Maximum
}

$results = @()
$badCount = 0

foreach ($s in $Servers) {
    $projPath = Join-Path $RepoRoot $s.Project
    $projDir  = Split-Path $projPath -Parent
    $outDir   = Join-Path $projDir "publish"
    $exePath  = Join-Path $outDir $s.Exe

    if (-not (Test-Path -LiteralPath $projPath)) {
        Write-Host "MISSING  $($s.Name): project not found at $projPath" -ForegroundColor Red
        $results += [pscustomobject]@{ Name=$s.Name; Status='MISSING' }
        $badCount++
        continue
    }

    $srcTime = Get-NewestSourceTime $projDir
    $exeExists = Test-Path -LiteralPath $exePath
    $exeTime = if ($exeExists) { (Get-Item -LiteralPath $exePath).LastWriteTime } else { [datetime]::MinValue }

    # Fresh = exe exists AND exe is at least as new as the newest source file.
    if ($exeExists -and $exeTime -ge $srcTime) {
        Write-Host "FRESH    $($s.Name)" -ForegroundColor Green
        $results += [pscustomobject]@{ Name=$s.Name; Status='FRESH' }
        continue
    }

    # Stale (or missing). Attempt rebuild unless -CheckOnly.
    $reason = if (-not $exeExists) { "exe missing" } else { "source newer than exe" }
    Write-Host "STALE    $($s.Name) ($reason)" -ForegroundColor Yellow

    if ($CheckOnly) {
        $results += [pscustomobject]@{ Name=$s.Name; Status='STALE' }
        $badCount++
        continue
    }

    # Try the targeted rebuild.
    Write-Host "         rebuilding $($s.Name) ..." -ForegroundColor Cyan
    $pubArgs = @("publish", $projPath, "-c", "Release", "-o", $outDir)
    & dotnet @pubArgs 2>&1 | Out-Null
    $pubExit = $LASTEXITCODE

    # Re-check the exe after publish.
    $exeExistsNow = Test-Path -LiteralPath $exePath
    $exeTimeNow = if ($exeExistsNow) { (Get-Item -LiteralPath $exePath).LastWriteTime } else { [datetime]::MinValue }

    if ($exeExistsNow -and $exeTimeNow -ge $srcTime) {
        Write-Host "REBUILT  $($s.Name) -> $exePath" -ForegroundColor Green
        $results += [pscustomobject]@{ Name=$s.Name; Status='REBUILT' }
        continue
    }

    # Publish did not produce a fresh exe. Diagnose why.
    if ($opencodeRunning) {
        Write-Host "STALE-LOCKED $($s.Name): publish could not refresh (opencode is holding the DLLs open)" -ForegroundColor Red
        $results += [pscustomobject]@{ Name=$s.Name; Status='STALE-LOCKED' }
    } elseif ($pubExit -ne 0) {
        Write-Host "FAILED   $($s.Name): dotnet publish exited $pubExit" -ForegroundColor Red
        $results += [pscustomobject]@{ Name=$s.Name; Status='FAILED' }
    } else {
        Write-Host "STALE-LOCKED $($s.Name): exe still older than source after publish (locked?)" -ForegroundColor Red
        $results += [pscustomobject]@{ Name=$s.Name; Status='STALE-LOCKED' }
    }
    $badCount++
}

# Summary + exit
Write-Host ""
if ($badCount -eq 0) {
    Write-Host "Ensure-Built: all servers FRESH/REBUILT. Safe to proceed with extraction." -ForegroundColor Green
    exit 0
}

Write-Host "Ensure-Built: $badCount server(s) are stale, locked, or failed. Do NOT proceed with extraction." -ForegroundColor Red
Write-Host ""
Write-Host "Stale exes produce single-module output (no MODULE: sections), which makes" -ForegroundColor Yellow
Write-Host "correct per-module attribution impossible. Do NOT fall back to naming conventions." -ForegroundColor Yellow
Write-Host ""
if ($opencodeRunning) {
    Write-Host "opencode is currently running and holding the publish DLLs open. To rebuild:" -ForegroundColor White
    Write-Host "  1. Close opencode" -ForegroundColor White
    Write-Host "  2. Open a terminal in the repo root and run: .\scripts\Ensure-Built.ps1" -ForegroundColor White
    Write-Host "  3. Restart opencode" -ForegroundColor White
    Write-Host "  4. Ask the AI to continue" -ForegroundColor White
} else {
    Write-Host "Re-run without -CheckOnly to rebuild, or run scripts\Build-All.ps1 for a full rebuild." -ForegroundColor White
}
exit 1
