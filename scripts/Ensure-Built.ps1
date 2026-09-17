<#
.SYNOPSIS
    Staleness gate: rebuilds any MCP exe whose source is newer than the exe.

.DESCRIPTION
    Compares the newest source timestamp (.cs/.csproj) against the publish
    exe for every server under toolkits/*/mcp/*/. Rebuilds stale or missing
    servers. Exit codes: 0 = all FRESH/REBUILT, 1 = STALE-LOCKED (opencode
    holds DLLs; close it, re-run, relaunch).
#>
param()

$ErrorActionPreference = "Stop"
$Root = Split-Path $PSScriptRoot -Parent

$serverDirs = Get-ChildItem -LiteralPath (Join-Path $Root "toolkits") -Directory | ForEach-Object {
    Get-ChildItem -LiteralPath (Join-Path $_.FullName "mcp") -Directory -ErrorAction SilentlyContinue
} | Where-Object { $_ -and (Test-Path (Join-Path $_.FullName "*.csproj")) }

$staleLocked = $false
foreach ($dir in $serverDirs) {
    $proj = Get-ChildItem -LiteralPath $dir.FullName -Filter *.csproj | Select-Object -First 1
    $outDir = Join-Path $dir.FullName "publish"
    $exe = Get-ChildItem -LiteralPath $outDir -Filter *.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    $newestSrc = Get-ChildItem -LiteralPath $dir.FullName -Recurse -Include *.cs,*.csproj -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(publish|bin|obj)\\' } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1

    $needsBuild = $false; $why = ""
    if (-not $exe) { $needsBuild = $true; $why = "exe missing" }
    elseif ($newestSrc -and $newestSrc.LastWriteTime -gt $exe.LastWriteTime) { $needsBuild = $true; $why = "source newer than exe" }

    if (-not $needsBuild) { Write-Host "FRESH   $($dir.Name)" -ForegroundColor DarkGray; continue }

    Write-Host "STALE   $($dir.Name) ($why) - rebuilding..." -ForegroundColor Yellow
    & dotnet publish $proj.FullName -c Release -o $outDir | Out-Null
    $exe2 = Get-ChildItem -LiteralPath $outDir -Filter *.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $exe2) { Write-Host "FAILED  $($dir.Name): build produced no exe." -ForegroundColor Red; $staleLocked = $true; continue }
    if ($newestSrc -and $newestSrc.LastWriteTime -gt $exe2.LastWriteTime) {
        Write-Host "STALE-LOCKED $($dir.Name): could not refresh (opencode running?). Close opencode, re-run, relaunch." -ForegroundColor Red
        $staleLocked = $true
    } else {
        Write-Host "REBUILT $($dir.Name)" -ForegroundColor Green
    }
}

if ($staleLocked) { exit 1 }
Write-Host "Ensure-Built: all MCP servers fresh." -ForegroundColor Green
exit 0
