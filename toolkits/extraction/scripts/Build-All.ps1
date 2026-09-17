<#
.SYNOPSIS
    Builds (publishes) all three OutSystems extraction MCP servers.

.DESCRIPTION
    Runs `dotnet publish -c Release` for every MCP server project, writing each
    exe to a normalized `<project>/publish/` folder. The output paths match the
    `command` entries in opencode.json exactly, so once this script succeeds the
    MCP servers are launchable by opencode.

    The compiled exes are gitignored (bin/, obj/, publish/), so this script is
    the single command to run after a fresh `git clone` to bring the toolkit
    online. It is also run by New-OutSystemsProject.ps1 when scaffolding a
    derivative project.

    This baseline is extraction-only (ClrMD readers). Editing lives in the
    `AI Outsystems Automation Editor` baseline (live + headless .oml). Requires
    the .NET 8+ SDK (all servers are net8.0). Run `dotnet --list-sdks` to confirm.

.PARAMETER SelfContained
    Publish self-contained (bundles the .NET runtime, no SDK needed on the
    target machine). Larger output. Use when deploying to a PC without the
    .NET runtime installed.

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

# Server -> (project relative path, output exe name). Output folder is always
# <project>/publish/, matching opencode.json.
$Servers = @(
    @{ Name = "outsystems-tools";  Project = "mcp\outsystems-tools\OutSystemsMcp.csproj";        Exe = "OutSystemsMcp.exe" },
    @{ Name = "outsystems-logic";  Project = "mcp\outsystems-logic\OutSystemsMcpLogic.csproj";   Exe = "OutSystemsMcpLogic.exe" },
    @{ Name = "outsystems-ui";     Project = "mcp\outsystems-ui\OutSystemsMcpUi.csproj";         Exe = "OutSystemsMcpUi.exe" }
)

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "dotnet not found on PATH. Install the .NET 8+ SDK from https://dotnet.microsoft.com"
    exit 1
}

# Warn if MCP server processes are running: opencode holds the publish DLLs
# open, so `dotnet publish` cannot overwrite them. A clean rebuild requires
# closing opencode first. (Existing exes remain valid; only refresh fails.)
$running = Get-Process -ErrorAction SilentlyContinue | Where-Object {
    $_.ProcessName -like "OutSystemsMcp*"
}
if ($running) {
    Write-Host "NOTE: OutSystemsMcp processes are running (opencode is active)." -ForegroundColor DarkYellow
    Write-Host "      Locked DLLs cannot be overwritten. For a clean rebuild, close opencode" -ForegroundColor DarkYellow
    Write-Host "      and re-run this script. Existing exes are kept as-is where locked." -ForegroundColor DarkYellow
}

$failures = 0
$stale    = 0
foreach ($s in $Servers) {
    $projPath = Join-Path $RepoRoot $s.Project
    $outDir   = Join-Path (Split-Path $projPath -Parent) "publish"
    $exePath  = Join-Path $outDir $s.Exe

    if (-not (Test-Path -LiteralPath $projPath)) {
        Write-Host "MISSING project for $($s.Name): $projPath" -ForegroundColor Red
        $failures++
        continue
    }

    # Clean the publish dir before publishing. Prevents "poisoned" mixed
    # deployments: a previous -SelfContained run leaves coreclr.dll /
    # hostpolicy.dll on disk, then a framework-dependent run only rewrites the
    # runtimeconfig -> the apphost loads the leftover local hostpolicy, cannot
    # resolve the shared framework, and crashes on launch with
    # "You must install or update .NET". Files locked by a running opencode are
    # skipped (warning); close opencode for a fully clean rebuild.
    if (Test-Path -LiteralPath $outDir) {
        $locked = @()
        foreach ($item in (Get-ChildItem -LiteralPath $outDir -Force)) {
            try { Remove-Item -LiteralPath $item.FullName -Recurse -Force -ErrorAction Stop }
            catch { $locked += $item.Name }
        }
        if ($locked.Count -gt 0) {
            Write-Host "NOTE: $($locked.Count) locked file(s) kept in $outDir (opencode running?)." -ForegroundColor DarkYellow
            Write-Host "      Close opencode and re-run for a clean rebuild." -ForegroundColor DarkYellow
        }
    }

    Write-Host ""
    Write-Host "==> Building $($s.Name) ..." -ForegroundColor Cyan
    $args = @("publish", $projPath, "-c", "Release", "-o", $outDir)
    if ($SelfContained) {
        $args += @("-r", $Runtime, "--self-contained", "true")
    }

    & dotnet @args
    $publishExit = $LASTEXITCODE

    if (Test-Path -LiteralPath $exePath) {
        if ($publishExit -ne 0) {
            # Publish failed (typically a locked-DLL overwrite while opencode
            # runs), but a valid exe is already on disk. Warn, don't fail.
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
    Write-Host "Build-All: $failures server(s) failed (exe missing). MCP toolkit is NOT ready." -ForegroundColor Red
    exit 1
}
if ($stale -gt 0) {
    Write-Host "Build-All: all exes present; $stale could not be refreshed (locked by running opencode)." -ForegroundColor Yellow
    Write-Host "          Close opencode and re-run for a clean rebuild. Verify-Project will still pass." -ForegroundColor Yellow
    exit 0
}

Write-Host "Build-All: all servers published. Next: run scripts\Verify-Project.ps1" -ForegroundColor Green
exit 0
