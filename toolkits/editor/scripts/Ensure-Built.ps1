<#
.SYNOPSIS
    Staleness gate for both MCP servers (headless .oml editor + live in-process editor).

.DESCRIPTION
    Checks whether each MCP server's published exe exists and is newer than its C#
    source. If missing or stale, rebuilds via dotnet publish. Use before launching
    opencode to avoid a mid-session stale-exe situation where tools are missing.

    Covers BOTH servers:
      - mcp/outsystems-omleditor  (OutSystemsMcpOmlEditor)
      - mcp/outsystems-liveeditor (OutSystemsMcpLiveEditor)

    Exit 0 (all FRESH or REBUILT): safe to proceed.
    Exit 1 (any STALE-LOCKED): opencode is holding a DLL open. Close opencode,
        re-run in a terminal, restart opencode.

.EXAMPLE
    .\scripts\Ensure-Built.ps1
#>
$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path $PSScriptRoot -Parent

# Both MCP servers - same list as Build-All.ps1 and Verify-Project.ps1.
$Servers = @(
    @{ Name = "outsystems-omleditor";  Dir = "mcp\outsystems-omleditor";  Csproj = "OutSystemsMcpOmlEditor.csproj";  Exe = "OutSystemsMcpOmlEditor.exe" },
    @{ Name = "outsystems-liveeditor"; Dir = "mcp\outsystems-liveeditor"; Csproj = "OutSystemsMcpLiveEditor.csproj"; Exe = "OutSystemsMcpLiveEditor.exe" }
)

function Get-NewestSourceTime([string]$dir) {
    $files = Get-ChildItem -LiteralPath $dir -Recurse -File -Include *.cs,*.csproj -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(bin|obj|publish)\\' }
    if (-not $files -or $files.Count -eq 0) { return [datetime]::MinValue }
    return ($files | Measure-Object -Property LastWriteTime -Maximum).Maximum
}

$staleLocked = $false

foreach ($s in $Servers) {
    $Proj    = Join-Path $RepoRoot (Join-Path $s.Dir $s.Csproj)
    $OutDir  = Join-Path $RepoRoot (Join-Path $s.Dir "publish")
    $Exe     = Join-Path $OutDir $s.Exe
    $ProjDir = Join-Path $RepoRoot $s.Dir

    if (-not (Test-Path -LiteralPath $Proj)) {
        Write-Host "MISSING project for $($s.Name): $Proj" -ForegroundColor Red
        $staleLocked = $true; continue
    }

    if (-not (Test-Path -LiteralPath $Exe)) {
        Write-Host "$($s.Name): MISSING exe -> building ..." -ForegroundColor Cyan
        & dotnet publish $Proj -c Release -o $OutDir
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $Exe)) {
            Write-Host "$($s.Name): STALE-LOCKED (build failed - opencode running?)." -ForegroundColor Red
            $staleLocked = $true
        } else {
            Write-Host "$($s.Name): REBUILT -> $Exe" -ForegroundColor Green
        }
        continue
    }

    $srcTime = Get-NewestSourceTime $ProjDir
    $exeTime = (Get-Item -LiteralPath $Exe).LastWriteTime
    if ($srcTime -gt $exeTime) {
        Write-Host "$($s.Name): STALE (source newer than exe) -> rebuilding ..." -ForegroundColor Cyan
        & dotnet publish $Proj -c Release -o $OutDir
        if ($LASTEXITCODE -ne 0) {
            Write-Host "$($s.Name): STALE-LOCKED (could not refresh - opencode holding DLLs open). Close opencode and re-run." -ForegroundColor Red
            $staleLocked = $true
        } else {
            Write-Host "$($s.Name): REBUILT -> $Exe" -ForegroundColor Green
        }
    } else {
        Write-Host "$($s.Name): FRESH -> $Exe" -ForegroundColor Green
    }
}

if ($staleLocked) {
    Write-Host ""
    Write-Host "Ensure-Built: one or more servers could not be refreshed. Close opencode and re-run." -ForegroundColor Red
    exit 1
}
Write-Host ""
Write-Host "Ensure-Built: all MCP servers fresh." -ForegroundColor Green
exit 0
