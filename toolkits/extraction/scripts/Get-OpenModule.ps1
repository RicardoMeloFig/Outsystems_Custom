<#
.SYNOPSIS
  Watches the Service Studio AutoSave cache and extracts metadata from any
  module that is OPEN in a running Service Studio instance - no .NET 6 SDK,
  no server, no productKey needed.

  HOW IT WORKS
  While a module is open, Service Studio continuously auto-saves a FULL .oml
  copy to:
    %LOCALAPPDATA%\OutSystems\ServiceStudio 11 XPlatform Stable\AutoSave\
        <ServiceStudioPID>_<rand>.<rand>.bak
  The filename embeds the SS process PID. The file IS a standard .oml
  (magic "OML") - verified. This script:
    1. Finds the running ServiceStudio.exe PID(s).
    2. Maps each AutoSave .bak to its PID.
    3. Parses the .oml header (module name, eSpaceKey, version, ...).
    4. Optionally copies the full .oml out for offline DLL-based extraction.

  NOTE: header extraction is fully offline. Full entity/action extraction
  from the .oml BODY still requires the OutSystems DLLs (net6) - see
  OmlExtractor. The body uses the proprietary "omi" format (not deflate/
  base64-JSON in a trivially parseable way) so it cannot be cracked with
  pure PowerShell.

.PARAMETER CopyOml
  If set, copies each open module's .oml to -OutDir as <ModuleName>.oml.

.PARAMETER OutDir
  Directory to copy .oml files into (default: current dir).

.PARAMETER Once
  Run once and exit. Otherwise loops every -Interval seconds.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File .\Get-OpenModule.ps1 -Once
  powershell -NoProfile -ExecutionPolicy Bypass -File .\Get-OpenModule.ps1 -CopyOml -OutDir .\open
#>
[CmdletBinding()]
param(
    [switch]$CopyOml,
    [string]$OutDir = ".",
    [switch]$Once,
    [int]$Interval = 15
)

$autoSaveDir = Join-Path $env:LOCALAPPDATA "OutSystems\ServiceStudio 11 XPlatform Stable\AutoSave"
$marker = [byte[]](0x77, 0x67, 0x7C, 0x2F) # "wg|/"

function Get-SsPidMap {
    $map = @{}
    Get-Process -Name ServiceStudio -ErrorAction SilentlyContinue | ForEach-Object { $map[$_.Id] = $_.Path }
    return $map
}

function Read-OmlHeader([string]$path) {
    $b = [System.IO.File]::ReadAllBytes($path)
    if ($b.Length -lt 11 -or ([char]$b[0]+[char]$b[1]+[char]$b[2]) -ne 'OML') { return $null }
    $idx = -1
    for ($i = 7; $i -lt $b.Length - 4; $i++) {
        if ($b[$i] -eq $marker[0] -and $b[$i+1] -eq $marker[1] -and $b[$i+2] -eq $marker[2] -and $b[$i+3] -eq $marker[3]) { $idx = $i; break }
    }
    if ($idx -lt 0) { return $null }
    $hdr = -join ($b[7..($idx-1)] | ForEach-Object { [char]$_ })
    $f = $hdr -split '\|'
    return [pscustomobject]@{
        File = (Split-Path $path -Leaf)
        PID = ($f[0])  # placeholder, set by caller
        ModuleName = $f[6]
        ESpaceKey  = $f[3]
        IsExtension = $f[5]
        Description = $f[7]
        SavedAt    = $f[8]
        SSVersion  = $f[0]
        SizeKB     = [math]::Round($b.Length/1KB,1)
        BodyOffset = $idx
        Path       = $path
    }
}

if (-not (Test-Path $autoSaveDir)) { Write-Warning "AutoSave dir not found: $autoSaveDir"; return }

do {
    $pidMap = Get-SsPidMap
    $now = Get-Date
    Write-Host "`n=== Open Service Studio modules @ $now ===" -ForegroundColor Cyan
    if (-not $pidMap.Count) { Write-Host "  (Service Studio is not running)" }
    foreach ($p in $pidMap.Keys) {
        $bak = Get-ChildItem $autoSaveDir -Filter "$p_*" -ErrorAction SilentlyContinue
        if (-not $bak) { Write-Host "  PID $p : running, no AutoSave file (no module open?)"; continue }
        foreach ($f in $bak) {
            $h = Read-OmlHeader $f.FullName
            if (-not $h) { continue }
            $h.PID = $p
            Write-Host ("  PID {0}  module={1}  key={2}  ext={3}  saved={4}  {5}KB" -f `
                $h.PID, $h.ModuleName, $h.ESpaceKey, $h.IsExtension, $h.SavedAt, $h.SizeKB)
            if ($CopyOml) {
                if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir | Out-Null }
                $out = Join-Path $OutDir "$($h.ModuleName).oml"
                Copy-Item -LiteralPath $f.FullName -Destination $out -Force
                Write-Host "    -> copied full .oml to $out" -ForegroundColor Green
            }
        }
    }
    if ($Once) { break }
    Start-Sleep -Seconds $Interval
} while ($true)
