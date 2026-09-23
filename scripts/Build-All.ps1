<#
.SYNOPSIS
    Publishes all 5 OutSystems MCP servers (3 extraction + 2 editor).

.DESCRIPTION
    Finds every .csproj under toolkits/*/mcp/*/ and runs
    `dotnet publish -c Release -o <dir>\publish` SELF-CONTAINED win-x64 by
    default, so the exes run on any Windows x64 PC with no .NET installs.
    The output paths match the `command` entries in opencode.json. Exes are
    gitignored; run this after a fresh clone. Rebuild a single server with -Name.

    Exit codes: 0 = all published; 1 = hard failure (exe missing);
    2 = stale (old exe kept — locked files or failed publish; close opencode
    and re-run).

.PARAMETER FrameworkDependent
    Publish framework-dependent (smaller output, but the machine running the
    server needs a .NET 8 runtime). Dev-only opt-out; NOT portable.

.PARAMETER Runtime
    The RID for the self-contained publish (default win-x64).

.PARAMETER Name
    Only build the server whose folder matches this name.

.EXAMPLE
    .\scripts\Build-All.ps1                      # self-contained win-x64 (default, portable)
    .\scripts\Build-All.ps1 -FrameworkDependent  # dev machines with a .NET 8 runtime
    .\scripts\Build-All.ps1 -Name outsystems-ui
#>
param(
    [switch]$FrameworkDependent,
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
        # Probe for locked files BEFORE cleaning. If opencode holds any file
        # open, skip this server entirely: publishing over a partially-cleaned
        # dir would mix a new build with the locked old exe/DLLs (poisoned
        # deployment), and deleting the unlocked files would leave a gutted,
        # unlaunchable dir.
        $locked = @()
        foreach ($item in (Get-ChildItem -LiteralPath $outDir -Force -File)) {
            try { $fs = [System.IO.File]::Open($item.FullName, 'Open', 'ReadWrite', 'None'); $fs.Close() } catch { $locked += $item.Name }
        }
        if ($locked.Count -gt 0) {
            Write-Host "STALE   $($dir.Name): $($locked.Count) locked file(s) in publish (opencode running?). Kept existing exe - close opencode and re-run." -ForegroundColor Yellow
            $stale++
            continue
        }
        foreach ($item in (Get-ChildItem -LiteralPath $outDir -Force)) {
            Remove-Item -LiteralPath $item.FullName -Recurse -Force -ErrorAction Stop
        }
    }
    Write-Host ""
    Write-Host "==> Building $($dir.Name) ..." -ForegroundColor Cyan
    $args = @("publish", $proj.FullName, "-c", "Release", "-o", $outDir)
    if (-not $FrameworkDependent) { $args += @("-r", $Runtime, "--self-contained", "true") }
    & dotnet @args
    $exit = $LASTEXITCODE
    # App exe is named after the csproj (AssemblyName). Never pick
    # "alphabetically first *.exe": a self-contained publish contains
    # runtime-pack exes (createdump.exe) that would misreport the path.
    $appExeName = [System.IO.Path]::GetFileNameWithoutExtension($proj.FullName) + ".exe"
    $exe = Get-ChildItem -LiteralPath $outDir -Filter $appExeName -File -ErrorAction SilentlyContinue | Select-Object -First 1
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
