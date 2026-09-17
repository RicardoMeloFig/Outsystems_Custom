<#
.SYNOPSIS
    Re-syncs the reference library snapshots from the original Outsystems REPO folder.

.DESCRIPTION
    Mirror-copies every repo folder (excluding .git) from -Source into
    references\repos\. /MIR removes workspace files that no longer exist in
    the source; use -NoMirror for additive-only sync. Run New-Catalog.ps1
    afterwards to refresh provenance.

.PARAMETER Source
    Path to the original library folder.

.PARAMETER NoMirror
    Copy without deleting extra files in the destination.

.EXAMPLE
    .\scripts\Sync-References.ps1 -Source "C:\Users\Ricardo Figueiredo\Documents\Outsystems REPO"
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Source,
    [switch]$NoMirror
)

$ErrorActionPreference = "Stop"
$Root = Split-Path $PSScriptRoot -Parent
if (-not (Test-Path -LiteralPath $Source)) { Write-Host "Source not found: $Source" -ForegroundColor Red; exit 1 }

$extra = @(); if ($NoMirror) { $extra = @('/E') } else { $extra = @('/MIR') }
robocopy $Source (Join-Path $Root "references\repos") @extra /XD .git /R:1 /W:1 /NFL /NDL /NP /MT:16
$rc = $LASTEXITCODE
if ($rc -ge 8) { Write-Host "Sync-References: robocopy FAILED (exit $rc)" -ForegroundColor Red; exit 1 }
Write-Host "Sync-References: done (robocopy exit $rc). Next: .\scripts\New-Catalog.ps1 to refresh provenance." -ForegroundColor Green
exit 0
