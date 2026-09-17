<#
.SYNOPSIS
    Launches opencode from the editor baseline root after a staleness check.

.DESCRIPTION
    Runs Ensure-Built.ps1 (rebuilds the editor exe if stale), then launches
    opencode from the repo root (where opencode.json lives). Use this instead of
    calling opencode directly to avoid mid-session stale-exe restarts.

.EXAMPLE
    .\scripts\start-opencode.ps1
#>
$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path $PSScriptRoot -Parent

& (Join-Path $PSScriptRoot "Ensure-Built.ps1")
if ($LASTEXITCODE -ne 0) {
    Write-Host "Staleness gate failed. Close opencode, re-run scripts\Ensure-Built.ps1, then retry." -ForegroundColor Red
    exit 1
}

Write-Host "Launching opencode from $RepoRoot ..." -ForegroundColor Cyan
Set-Location -LiteralPath $RepoRoot
opencode
