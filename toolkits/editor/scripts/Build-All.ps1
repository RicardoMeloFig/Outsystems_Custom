<#
.SYNOPSIS
    Builds (publishes) the headless .oml editor MCP server.

.DESCRIPTION
    Runs `dotnet publish -c Release` for the outsystems-omleditor project,
    writing the exe to mcp/outsystems-omleditor/publish/. The output path matches
    the `command` entry in opencode.json, so once this succeeds the server is
    launchable by opencode.

    The compiled exe is gitignored (bin/, obj/, publish/), so this is the command
    to run after a fresh clone to bring the editor online.

    Requires the .NET 8+ SDK (the editor is net8.0). Run `dotnet --list-sdks`.

.PARAMETER SelfContained
    Publish self-contained (bundles the .NET runtime). Larger output. Use on a PC
    with no .NET runtime installed.

.PARAMETER Runtime
    The RID to use with -SelfContained (default: win-x64).

.EXAMPLE
    .\scripts\Build-All.ps1
    .\scripts\Build-All.ps1 -SelfContained
#>
param(
    [switch]$SelfContained,
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path $PSScriptRoot -Parent

# Editor-only baseline: two MCP servers (headless .oml editor + live in-process editor).
$Servers = @(
    @{ Name = "outsystems-omleditor";  Project = "mcp\outsystems-omleditor\OutSystemsMcpOmlEditor.csproj";  Exe = "OutSystemsMcpOmlEditor.exe" },
    @{ Name = "outsystems-liveeditor"; Project = "mcp\outsystems-liveeditor\OutSystemsMcpLiveEditor.csproj"; Exe = "OutSystemsMcpLiveEditor.exe" }
)

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "dotnet not found on PATH. Install the .NET 8+ SDK from https://dotnet.microsoft.com"
    exit 1
}

# Warn if an MCP process is running (opencode holds the publish DLLs open).
$running = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like "OutSystemsMcp*" }
if ($running) {
    Write-Host "NOTE: an OutSystemsMcp* server is running (opencode is active)." -ForegroundColor DarkYellow
    Write-Host "      Locked DLLs cannot be overwritten. Close opencode and re-run for a clean rebuild." -ForegroundColor DarkYellow
}

$failures = 0; $stale = 0
foreach ($s in $Servers) {
    $projPath = Join-Path $RepoRoot $s.Project
    $outDir   = Join-Path (Split-Path $projPath -Parent) "publish"
    $exePath  = Join-Path $outDir $s.Exe

    if (-not (Test-Path -LiteralPath $projPath)) {
        Write-Host "MISSING project for $($s.Name): $projPath" -ForegroundColor Red
        $failures++; continue
    }

    # Clean publish dir to avoid poisoned mixed deployments (self-contained leftovers).
    if (Test-Path -LiteralPath $outDir) {
        $locked = @()
        foreach ($item in (Get-ChildItem -LiteralPath $outDir -Force)) {
            try { Remove-Item -LiteralPath $item.FullName -Recurse -Force -ErrorAction Stop }
            catch { $locked += $item.Name }
        }
        if ($locked.Count -gt 0) {
            Write-Host "NOTE: $($locked.Count) locked file(s) kept in $outDir (opencode running?)." -ForegroundColor DarkYellow
        }
    }

    Write-Host ""
    Write-Host "==> Building $($s.Name) ..." -ForegroundColor Cyan
    $args = @("publish", $projPath, "-c", "Release", "-o", $outDir)
    if ($SelfContained) { $args += @("-r", $Runtime, "--self-contained", "true") }
    & dotnet @args
    $publishExit = $LASTEXITCODE

    if (Test-Path -LiteralPath $exePath) {
        if ($publishExit -ne 0) {
            Write-Host "STALE   $($s.Name): publish could not refresh (locked?), keeping existing exe -> $exePath" -ForegroundColor Yellow
            $stale++
        } else {
            Write-Host "OK      $($s.Name) -> $exePath" -ForegroundColor Green
        }
    } else {
        Write-Host "FAILED  $($s.Name): exe not produced at $exePath (dotnet publish exit $publishExit)" -ForegroundColor Red
        $failures++
    }
}

Write-Host ""
if ($failures -gt 0) {
    Write-Host "Build-All: server failed (exe missing). Editor toolkit is NOT ready." -ForegroundColor Red
    exit 1
}
if ($stale -gt 0) {
    Write-Host "Build-All: exe present but could not be refreshed (locked by running opencode)." -ForegroundColor Yellow
    Write-Host "          Close opencode and re-run for a clean rebuild. Verify-Project will still pass." -ForegroundColor Yellow
    exit 0
}
Write-Host "Build-All: editor server published. Next: run scripts\Verify-Project.ps1" -ForegroundColor Green
exit 0
