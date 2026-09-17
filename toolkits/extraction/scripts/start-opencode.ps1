<#
.SYNOPSIS
    Launcher script that ensures MCP server exes are fresh before starting OpenCode Desktop.

.DESCRIPTION
    OpenCode launches MCP servers from the publish\ directories. If the source code
    has been modified but the exes haven't been rebuilt, the stale exes produce
    incorrect output (e.g., truncated theme CSS due to the ClrMD 4096-char bug).

    This script runs Ensure-Built.ps1 BEFORE launching OpenCode, while no DLLs are
    locked. If any server is stale, it rebuilds automatically. The user never has
    to think about manual rebuilds.

    The Start Menu shortcut for OpenCode is updated to point to this script instead
    of OpenCode.exe directly, making the rebuild check fully automatic.

.PARAMETER SkipRebuild
    Skip the Ensure-Built check and launch OpenCode immediately.

.EXAMPLE
    .\scripts\start-opencode.ps1
    .\scripts\start-opencode.ps1 -SkipRebuild
#>
param(
    [switch]$SkipRebuild
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path $PSScriptRoot -Parent
$OpenCodeExe = "$env:LOCALAPPDATA\Programs\@opencode-aidesktop\OpenCode.exe"

# --- Verify OpenCode exists ---
if (-not (Test-Path -LiteralPath $OpenCodeExe)) {
    Write-Host "ERROR: OpenCode Desktop not found at:" -ForegroundColor Red
    Write-Host "  $OpenCodeExe" -ForegroundColor Red
    Write-Host "Install it from https://opencode.ai" -ForegroundColor Red
    exit 1
}

# --- Ensure MCP exes are fresh (before OpenCode locks them) ---
if (-not $SkipRebuild) {
    Write-Host "Checking MCP server builds..." -ForegroundColor Cyan
    $ensureScript = Join-Path $RepoRoot "scripts\Ensure-Built.ps1"
    if (Test-Path -LiteralPath $ensureScript) {
        & $ensureScript
        $exitCode = $LASTEXITCODE
        if ($exitCode -eq 0) {
            Write-Host "All MCP servers are fresh. Starting OpenCode..." -ForegroundColor Green
        } else {
            Write-Host ""
            Write-Host "WARNING: Some MCP servers could not be rebuilt." -ForegroundColor Yellow
            Write-Host "OpenCode will use the existing (possibly stale) exes." -ForegroundColor Yellow
            Write-Host "Check the output above for details." -ForegroundColor Yellow
            Write-Host ""
        }
    } else {
        Write-Host "WARNING: Ensure-Built.ps1 not found at $ensureScript" -ForegroundColor Yellow
        Write-Host "Skipping build check." -ForegroundColor Yellow
    }
}

# --- Launch OpenCode Desktop ---
Write-Host ""
Write-Host "Launching OpenCode Desktop..." -ForegroundColor Cyan
Start-Process -FilePath $OpenCodeExe
Write-Host "OpenCode launched." -ForegroundColor Green

# Keep the window open briefly so the user can read the build status
Start-Sleep -Seconds 3
