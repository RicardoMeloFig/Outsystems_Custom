<#
.SYNOPSIS
    Generates catalog\repositories.json from the original Outsystems REPO folder.

.DESCRIPTION
    For every repository folder under -Source, records: name, target path in
    this workspace, branch, commit, shallow-clone status, dirty (local
    changes) status, submodule presence, license file, file count, and size.
    Run BEFORE/AFTER syncing references; git data can only be read while the
    original folders (with .git) exist.

.PARAMETER Source
    Path to the original library folder containing the ~236 repo clones.

.EXAMPLE
    .\scripts\New-Catalog.ps1 -Source "C:\Users\Ricardo Figueiredo\Documents\Outsystems REPO"
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Source
)

$ErrorActionPreference = "Stop"
$Root = Split-Path $PSScriptRoot -Parent
if (-not (Test-Path -LiteralPath $Source)) { Write-Host "Source not found: $Source" -ForegroundColor Red; exit 1 }

$outDir = Join-Path $Root "catalog"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$repos = Get-ChildItem -LiteralPath $Source -Directory | Where-Object Name -ne '.git' | Sort-Object Name
$rows = New-Object System.Collections.Generic.List[object]
$i = 0
foreach ($r in $repos) {
    $i++
    $p = $r.FullName
    Write-Progress -Activity "Cataloging repos" -Status $r.Name -PercentComplete ([int](100 * $i / $repos.Count))
    $entry = [ordered]@{ name = $r.Name; path = "references/repos/$($r.Name)" }
    $git = Join-Path $p '.git'
    if (Test-Path -LiteralPath $git) {
        $entry.branch = (& git -C $p rev-parse --abbrev-ref HEAD 2>$null)
        $entry.commit = (& git -C $p rev-parse HEAD 2>$null)
        $entry.shallow = (Test-Path -LiteralPath (Join-Path $git 'shallow'))
        $entry.dirty = [bool](& git -C $p status --porcelain 2>$null | Select-Object -First 1)
        $entry.hasSubmodules = (Test-Path -LiteralPath (Join-Path $p '.gitmodules'))
    } else {
        $entry.branch = $null; $entry.commit = $null; $entry.shallow = $false; $entry.dirty = $false; $entry.hasSubmodules = $false
    }
    $files = 0; $bytes = [long]0
    try {
        foreach ($f in [System.IO.Directory]::EnumerateFiles($p, '*', [System.IO.SearchOption]::AllDirectories)) {
            if ($f -like '*\.git\*') { continue }
            $files++
            try { $bytes += ([System.IO.FileInfo]::new($f)).Length } catch { }
        }
    } catch { }
    $entry.fileCount = $files
    $entry.sizeBytes = $bytes
    $entry.licenseFile = $null
    foreach ($ln in @('LICENSE','LICENSE.md','LICENSE.txt','COPYING')) {
        if (Test-Path -LiteralPath (Join-Path $p $ln)) { $entry.licenseFile = $ln; break }
    }
    $rows.Add([pscustomobject]$entry)
}

$jsonPath = Join-Path $outDir 'repositories.json'
$rows | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $jsonPath -Encoding UTF8
Write-Host "New-Catalog: wrote $($rows.Count) repos -> $jsonPath" -ForegroundColor Green
exit 0
