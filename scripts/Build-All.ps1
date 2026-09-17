<#
.SYNOPSIS
    Publishes all 5 OutSystems MCP servers (3 extraction + 2 editor).

.DESCRIPTION
    Finds every .csproj under toolkits/*/mcp/*/ and runs
    `dotnet publish -c Release -o <dir>\publish`. The output paths match the
    `command` entries in opencode.json. Exes are gitignored; run this after a
    fresh clone. Rebuild a single server with -Name.

    Exit codes: 0 = all published; 1 = hard failure (exe missing);
    2 = stale (publish failed, old exe kept — usually locked DLLs).

.PARAMETER SelfContained
    Publish self-contained (bundles the .NET runtime). For PCs with no .NET runtime.

.PARAMETER Runtime
    The RID to use with -SelfContained (default win-x64).

.PARAMETER Name
    Only build the server whose folder matches this name.

.EXAMPLE
    .\scripts\Build-All.ps1
    .\scripts\Build-All.ps1 -SelfContained
    .\scripts\Build-All.ps1 -Name outsystems-ui
#>
param(
    [switch]$SelfContained,
    [string]$Runtime = "win-x64",
    [string]$Name
)

$ErrorActionPreference = "Stop"
$Root = Split-Path $PSScriptRoot -Parent

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "dotnet not found on PATH. Install the .NET 8+ SDK from https://dotnet.microsoft.com"
    exit 1
}

$serverDirs = Get-ChildItem -LiteralPath (Join-Path $Root "toolkits") -Directory | ForEach-Object {
    Get-ChildItem -LiteralPath (Join-Path $_.FullName "mcp") -Directory -ErrorAction SilentlyContinue
} | Where-Object { $_ } | Where-Object { Test-Path (Join-Path $_.FullName "*.csproj") }
if ($Name) { $serverDirs = $serverDirs | Where-Object Name -eq $Name }
if (-not $serverDirs) { Write-Host "No MCP server projects found under toolkits\*\mcp\." -ForegroundColor Red; exit 1 }

$running = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like "OutSystemsMcp*" }
if ($running) {
    Write-Host "NOTE: an OutSystemsMcp* server is running (opencode active). Locked DLLs cannot be overwritten." -ForegroundColor DarkYellow
}

$failures = 0; $stale = 0
foreach ($dir in $serverDirs) {
    $proj = Get-ChildItem -LiteralPath $dir.FullName -Filter *.csproj | Select-Object -First 1
    $outDir = Join-Path $dir.FullName "publish"
    if (Test-Path -LiteralPath $outDir) {
        foreach ($item in (Get-ChildItem -LiteralPath $outDir -Force)) {
            try { Remove-Item -LiteralPath $item.FullName -Recurse -Force -ErrorAction Stop } catch { }
        }
    }
    Write-Host ""
    Write-Host "==> Building $($dir.Name) ..." -ForegroundColor Cyan
    $args = @("publish", $proj.FullName, "-c", "Release", "-o", $outDir)
    if ($SelfContained) { $args += @("-r", $Runtime, "--self-contained", "true") }
    & dotnet @args
    $exit = $LASTEXITCODE
    $exe = Get-ChildItem -LiteralPath $outDir -Filter *.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($exe) {
        if ($exit -ne 0) { Write-Host "STALE   $($dir.Name): publish failed but exe exists -> $($exe.FullName)" -ForegroundColor Yellow; $stale++ }
        else { Write-Host "OK      $($dir.Name) -> $($exe.FullName)" -ForegroundColor Green }
    } else {
        Write-Host "FAILED  $($dir.Name): no exe produced (dotnet publish exit $exit)" -ForegroundColor Red
        $failures++
    }
}

Write-Host ""
if ($failures -gt 0) { Write-Host "Build-All: $failures server(s) FAILED." -ForegroundColor Red; exit 1 }
if ($stale -gt 0) { Write-Host "Build-All: $stale server(s) stale (publish failed, old exe kept). Close opencode and re-run. Exit code 2." -ForegroundColor Yellow; exit 2 }
Write-Host "Build-All: all servers published. Next: .\scripts\Verify-Project.ps1" -ForegroundColor Green
exit 0
