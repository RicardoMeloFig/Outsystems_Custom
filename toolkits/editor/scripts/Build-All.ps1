<#
.SYNOPSIS
    Builds (publishes) the headless .oml editor MCP server.

.DESCRIPTION
    Runs `dotnet publish -c Release -r win-x64 --self-contained true` for the
    outsystems-omleditor and outsystems-liveeditor projects, writing each exe
    to mcp/<server>/publish/. Self-contained is the DEFAULT: each exe bundles
    the .NET runtime, so it runs on any Windows x64 PC with no .NET installs.
    The output paths match the `command` entries in opencode.json, so once
    this succeeds the servers are launchable by opencode.

    The compiled exes are gitignored (bin/, obj/, publish/), so this is the
    command to run after a fresh clone to bring the editor online.

    Requires the .NET 8+ SDK to BUILD (the editors are net8.0) — but the built
    exes need no runtime to RUN. Run `dotnet --list-sdks`.

.PARAMETER FrameworkDependent
    Publish framework-dependent instead (smaller output, but the machine
    running the server needs a .NET 8 runtime). Dev-only opt-out; NOT portable.

.PARAMETER Runtime
    The RID for the self-contained publish (default: win-x64).

.EXAMPLE
    .\scripts\Build-All.ps1                      # self-contained win-x64 (default, portable)
    .\scripts\Build-All.ps1 -FrameworkDependent  # dev machines with a .NET 8 runtime
#>
param(
    [switch]$FrameworkDependent,
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

    # Clean publish dir to avoid poisoned mixed deployments (self-contained
    # leftovers + a partial overwrite from the other mode). Locks are probed
    # BEFORE cleaning: if a running opencode holds any file open, SKIP this
    # server — publishing over a partially-cleaned dir would mix a new build
    # with the locked old exe/DLLs, and deleting unlocked files would leave a
    # gutted, unlaunchable dir.
    if (Test-Path -LiteralPath $outDir) {
        $locked = @()
        foreach ($item in (Get-ChildItem -LiteralPath $outDir -Force -File)) {
            try { $fs = [System.IO.File]::Open($item.FullName, 'Open', 'ReadWrite', 'None'); $fs.Close() } catch { $locked += $item.Name }
        }
        if ($locked.Count -gt 0) {
            Write-Host "NOTE: $($locked.Count) locked file(s) in $outDir (opencode running?)." -ForegroundColor DarkYellow
            Write-Host "      SKIPPING $($s.Name) to avoid a poisoned mixed deployment - existing exe kept." -ForegroundColor DarkYellow
            $stale++
            continue
        }
        foreach ($item in (Get-ChildItem -LiteralPath $outDir -Force)) {
            Remove-Item -LiteralPath $item.FullName -Recurse -Force -ErrorAction Stop
        }
    }

    Write-Host ""
    Write-Host "==> Building $($s.Name) ..." -ForegroundColor Cyan
    $args = @("publish", $projPath, "-c", "Release", "-o", $outDir)
    if (-not $FrameworkDependent) { $args += @("-r", $Runtime, "--self-contained", "true") }
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
