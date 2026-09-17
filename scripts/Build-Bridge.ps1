<#
.SYNOPSIS
    Compiles the OsLiveBridge Service Studio plugin (compile-only by default).

.DESCRIPTION
    Builds toolkits\editor\bridge\OsLiveBridge\OsLiveBridge.csproj. The project
    has a CopyToPlugins target that would install the DLL into Service Studio's
    Plugins\ServiceStudio\ folder on every build; this script passes
    -p:SkipCopy=true so a plain build NEVER touches Service Studio.

.PARAMETER Install
    Additionally deploy the DLL into Service Studio
    ($env:OSSS_DIR or C:\Program Files\OutSystems\Service Studio 11\Service Studio\Plugins\ServiceStudio\).
    This modifies Service Studio's installation - review before use.

.EXAMPLE
    .\scripts\Build-Bridge.ps1
    .\scripts\Build-Bridge.ps1 -Install
#>
param([switch]$Install)

$ErrorActionPreference = "Stop"
$Root = Split-Path $PSScriptRoot -Parent
$proj = Join-Path $Root "toolkits\editor\bridge\OsLiveBridge\OsLiveBridge.csproj"
if (-not (Test-Path -LiteralPath $proj)) { Write-Host "Bridge project not found: $proj" -ForegroundColor Red; exit 1 }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { Write-Error "dotnet not found on PATH."; exit 1 }

$ssDir = if ($env:OSSS_DIR) { $env:OSSS_DIR } else { "C:\Program Files\OutSystems\Service Studio 11\Service Studio" }

Write-Host "==> Building OsLiveBridge (compile only, no install)..." -ForegroundColor Cyan
& dotnet build $proj -c Release -p:SkipCopy=true "-p:SSDir=$ssDir"
if ($LASTEXITCODE -ne 0) { Write-Host "Build-Bridge: build FAILED." -ForegroundColor Red; exit 1 }
Write-Host "Build-Bridge: compiled OK (not installed)." -ForegroundColor Green

if ($Install) {
    $dest = Join-Path $ssDir "Plugins\ServiceStudio"
    Write-Host "Install: copying ServiceStudio.Plugin.OsLiveBridge.dll -> $dest" -ForegroundColor Yellow
    if (-not (Test-Path -LiteralPath $dest)) { New-Item -ItemType Directory -Force -Path $dest | Out-Null }
    Copy-Item -Path (Join-Path ($proj | Split-Path) "bin\Release\ServiceStudio.Plugin.OsLiveBridge.dll") -Destination $dest -Force
    Write-Host "Build-Bridge: installed into Service Studio. Restart Service Studio to load the plugin." -ForegroundColor Green
}
exit 0
