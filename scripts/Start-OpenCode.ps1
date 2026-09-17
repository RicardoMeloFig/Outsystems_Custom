<#
.SYNOPSIS
    Runs the staleness gate, then launches OpenCode Desktop from the workspace root.

.DESCRIPTION
    Ensures all 5 MCP exes are fresh (while no DLLs are locked), then starts
    OpenCode Desktop with the workspace as its working directory so it picks
    up this folder's opencode.json / AGENTS.md.

.PARAMETER SkipRebuild
    Skip the Ensure-Built check and launch immediately.

.EXAMPLE
    .\scripts\Start-OpenCode.ps1
#>
param([switch]$SkipRebuild)

$ErrorActionPreference = "Stop"
$Root = Split-Path $PSScriptRoot -Parent
$OpenCodeExe = "$env:LOCALAPPDATA\Programs\@opencode-aidesktop\OpenCode.exe"
if (-not (Test-Path -LiteralPath $OpenCodeExe)) {
    Write-Host "ERROR: OpenCode Desktop not found at $OpenCodeExe (install from https://opencode.ai)" -ForegroundColor Red
    exit 1
}

if (-not $SkipRebuild) {
    Write-Host "Checking MCP server builds..." -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot "Ensure-Built.ps1")
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: staleness gate failed (STALE-LOCKED). A running opencode is holding the" -ForegroundColor Red
        Write-Host "       publish DLLs open, so the built exes do not match the current source." -ForegroundColor Red
        Write-Host "       Launching now would run stale MCP tools. Fix: close all opencode windows," -ForegroundColor Red
        Write-Host "       re-run .\scripts\Ensure-Built.ps1 (must exit 0), then re-run this script." -ForegroundColor Red
        Write-Host "       Use -SkipRebuild only if you deliberately accept the existing exes." -ForegroundColor Red
        exit 1
    }
    Write-Host "All MCP servers are fresh." -ForegroundColor Green
}

Write-Host "Launching OpenCode Desktop from $Root ..." -ForegroundColor Cyan
Start-Process -FilePath $OpenCodeExe -WorkingDirectory $Root
Write-Host "OpenCode launched. Run /mcp inside to confirm all five servers are connected." -ForegroundColor Green
Start-Sleep -Seconds 2
