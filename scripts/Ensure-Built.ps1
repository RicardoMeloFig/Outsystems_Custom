<#
.SYNOPSIS
    Staleness gate: rebuilds any MCP exe whose source is newer than the exe.

.DESCRIPTION
    Compares the newest source timestamp (.cs/.csproj) against the publish
    exe for every server under toolkits/*/mcp/*/. The app exe is resolved by
    name from the csproj (AssemblyName) — a self-contained publish also
    contains runtime-pack exes like createdump.exe whose preserved old
    timestamps must never be used for freshness. Rebuilds stale or missing
    servers. A publish is also treated as stale when it is framework-dependent
    (marker: no coreclr.dll in the publish folder), so the gate can never
    silently revert to non-portable exes. Rebuilds are always SELF-CONTAINED
    win-x64. The publish dir is cleaned before rebuilding (prevents poisoned
    mixed deployments); a server whose files are locked by a running opencode
    is skipped, not half-overwritten.

    Exit codes: 0 = all FRESH/REBUILT, 1 = STALE-LOCKED (opencode holds
    DLLs; close it, re-run, relaunch).
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
    # App exe is named after the csproj (AssemblyName). Never pick
    # "alphabetically first *.exe": a self-contained publish contains
    # runtime-pack exes (createdump.exe) with old preserved timestamps.
    $appExeName = [System.IO.Path]::GetFileNameWithoutExtension($proj.FullName) + ".exe"
    $exe = Get-ChildItem -LiteralPath $outDir -Filter $appExeName -File -ErrorAction SilentlyContinue | Select-Object -First 1
    $newestSrc = Get-ChildItem -LiteralPath $dir.FullName -Recurse -Include *.cs,*.csproj -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(publish|bin|obj)\\' } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1

    $needsBuild = $false; $why = ""
    if (-not $exe) { $needsBuild = $true; $why = "exe missing" }
    elseif (-not (Test-Path -LiteralPath (Join-Path $outDir "coreclr.dll"))) { $needsBuild = $true; $why = "framework-dependent publish (no coreclr.dll)" }
    elseif ($newestSrc -and $newestSrc.LastWriteTime -gt $exe.LastWriteTime) { $needsBuild = $true; $why = "source newer than exe" }

    if (-not $needsBuild) { Write-Host "FRESH   $($dir.Name)" -ForegroundColor DarkGray; continue }

    Write-Host "STALE   $($dir.Name) ($why) - rebuilding self-contained..." -ForegroundColor Yellow

    # Probe for locked files BEFORE cleaning, then rebuild. Prevents "poisoned"
    # mixed deployments (leftover coreclr.dll/hostpolicy.dll from one mode plus
    # a partial overwrite from the other) and gutted dirs. If any file is
    # locked (running opencode), skip this server entirely.
    if (Test-Path -LiteralPath $outDir) {
        $locked = @()
        foreach ($item in (Get-ChildItem -LiteralPath $outDir -Force -File)) {
            try { $fs = [System.IO.File]::Open($item.FullName, 'Open', 'ReadWrite', 'None'); $fs.Close() } catch { $locked += $item.Name }
        }
        if ($locked.Count -gt 0) {
            Write-Host "STALE-LOCKED $($dir.Name): $($locked.Count) file(s) locked (opencode running?). Close opencode, re-run, relaunch." -ForegroundColor Red
            $staleLocked = $true; continue
        }
        foreach ($item in (Get-ChildItem -LiteralPath $outDir -Force)) {
            Remove-Item -LiteralPath $item.FullName -Recurse -Force -ErrorAction Stop
        }
    }

    & dotnet publish $proj.FullName -c Release -r win-x64 --self-contained true -o $outDir | Out-Null
    $exe2 = Get-ChildItem -LiteralPath $outDir -Filter $appExeName -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $exe2) { Write-Host "FAILED  $($dir.Name): build produced no exe." -ForegroundColor Red; $staleLocked = $true; continue }
    if (-not (Test-Path -LiteralPath (Join-Path $outDir "coreclr.dll"))) {
        Write-Host "FAILED  $($dir.Name): build produced a framework-dependent publish (no coreclr.dll)." -ForegroundColor Red
        $staleLocked = $true; continue
    }
    if ($newestSrc -and $newestSrc.LastWriteTime -gt $exe2.LastWriteTime) {
        Write-Host "STALE-LOCKED $($dir.Name): could not refresh (opencode running?). Close opencode, re-run, relaunch." -ForegroundColor Red
        $staleLocked = $true
    } else {
        Write-Host "REBUILT $($dir.Name)" -ForegroundColor Green
    }
}

if ($staleLocked) { exit 1 }
Write-Host "Ensure-Built: all MCP servers fresh (self-contained)." -ForegroundColor Green
exit 0
