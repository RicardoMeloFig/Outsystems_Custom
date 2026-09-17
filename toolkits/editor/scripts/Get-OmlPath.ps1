<#
.SYNOPSIS
    Finds .oml (and AutoSave .bak) files for a module by name.

.DESCRIPTION
    Headless helper for the editor: locate the .oml file to edit. Searches the
    user's Documents tree for <ModuleName>.oml, and lists SS AutoSave .bak files
    (the live in-memory state, written periodically). The on-disk .oml reflects
    the last Save in SS; the .bak reflects the live state (but rotates).

    Hardened install checks (pattern borrowed from OutSystems.SetupTools):
    verifies the Service Studio install (servicestudio.exe) before searching,
    and discovers the AutoSave directory by enumerating %LOCALAPPDATA%\OutSystems
    instead of hardcoding one version folder.

    Remember: have the user Save (Ctrl+S) in SS first so the .oml is current.

.PARAMETER ModuleName
    Module name to look for (matches <ModuleName>.oml).

.PARAMETER FullHome
    Search all of $HOME (slow). Default searches the fast, likely locations
    first (Documents, Desktop, Downloads) and only falls back to $HOME when
    nothing is found there.

.EXAMPLE
    .\scripts\Get-OmlPath.ps1 Diet_BL
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$ModuleName,

    [switch]$FullHome
)

$ErrorActionPreference = "Stop"

# --- SS install check (SetupTools pattern: verify the platform is where the
# --- tooling expects it before doing module work) -------------------------
$ssDir = if ($env:OSSS_DIR) { $env:OSSS_DIR } else { 'C:\Program Files\OutSystems\Service Studio 11\Service Studio' }
$ssExe = Join-Path $ssDir 'servicestudio.exe'
if (Test-Path -LiteralPath $ssExe) {
    $ver = (Get-Item -LiteralPath $ssExe).VersionInfo.ProductVersion
    Write-Host "SS install: OK  ($ssExe, version $ver)"
} else {
    Write-Warning "SS install NOT found at '$ssDir'. Set OSSS_DIR to the install dir if SS lives elsewhere."
}

# --- Locate the saved .oml -------------------------------------------------
Write-Host ""
Write-Host "=== .oml files named $ModuleName.oml ==="

$fastRoots = @(
    (Join-Path $HOME 'Documents'),
    (Join-Path $HOME 'Desktop'),
    (Join-Path $HOME 'Downloads')
) | Where-Object { Test-Path -LiteralPath $_ }

$searchRoots = if ($FullHome) { @($HOME) } else { $fastRoots }
$omls = @()
foreach ($root in $searchRoots) {
    $omls = Get-ChildItem -Path $root -Recurse -Filter "$ModuleName.oml" -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object FullName, @{n='KB';e={[math]::Round($_.Length/1KB)}}, LastWriteTime
    if ($omls) { break }
}
if (-not $omls -and -not $FullHome) {
    Write-Host "  (not in Documents/Desktop/Downloads - falling back to full HOME scan; use -FullHome next time to skip the two-pass)"
    $omls = Get-ChildItem -Path $HOME -Recurse -Filter "$ModuleName.oml" -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object FullName, @{n='KB';e={[math]::Round($_.Length/1KB)}}, LastWriteTime
}

if ($omls) { $omls | Format-Table -AutoSize } else { Write-Host "  (none found)" }

# --- AutoSave .bak files (dir discovered, not hardcoded) --------------------
Write-Host ""
Write-Host "=== SS AutoSave .bak files (live in-memory state; any module) ==="
$asDirs = @()
$osRoot = Join-Path $env:LOCALAPPDATA 'OutSystems'
if (Test-Path -LiteralPath $osRoot) {
    $asDirs = Get-ChildItem -LiteralPath $osRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like 'ServiceStudio*' } |
        ForEach-Object { Join-Path $_.FullName 'AutoSave' } |
        Where-Object { Test-Path -LiteralPath $_ }
}
if ($asDirs.Count -eq 0) { Write-Host "  (no AutoSave dir under $osRoot)" }
foreach ($as in $asDirs) {
    Write-Host "  [$as]"
    Get-ChildItem -LiteralPath $as -Filter "*.bak" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object Name, @{n='KB';e={[math]::Round($_.Length/1KB)}}, LastWriteTime |
        Format-Table -AutoSize
}

Write-Host ""
Write-Host "Tip: to edit, Save the module in SS (Ctrl+S), then pass the .oml path to an"
Write-Host "     outsystems-omleditor tool. The .bak is the live state but rotates - prefer the saved .oml."
Write-Host "     After a headless edit, reopen the result: scripts\Open-OmlInSS.ps1 -OmlPath <outOml>"
