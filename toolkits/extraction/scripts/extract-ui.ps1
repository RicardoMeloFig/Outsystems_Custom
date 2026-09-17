<#
.SYNOPSIS
  Generic OutSystems UI (presentation-layer) extractor wrapper.
  Builds the UiExtractor tool (if needed) and runs it against the module open
  in a running Service Studio instance. 100% generic - no module name is
  hardcoded; it is supplied as a parameter.

  The module is resolved in priority order: -Module flag, OUTSYSTEMS_MODULE
  env var, or a local .env file.

.PARAMETER Module
  Name of the OutSystems module to extract. The tool attaches to the running
  Service Studio instance whose open module matches this name. If omitted, the
  OUTSYSTEMS_MODULE env var / .env file is used.

.PARAMETER SsPid
  Optional Service Studio process id (skips auto-detection by module name).

.PARAMETER OutputDir
  Base directory for output (default: docs). Files are written to
  <OutputDir>/<Module>/{<ThemeName>.css, ui-tree.txt, metadata.json, client-actions.json}.

.EXAMPLE
  .\scripts\extract-ui.ps1 -Module MyApp
  .\scripts\extract-ui.ps1 -Module MyApp -OutputDir docs
  npm run extract-ui -- -Module MyApp
#>
[CmdletBinding()]
param(
    [string]$Module,
    [int]$SsPid,
    [string]$OutputDir
)

$repoRoot = Split-Path -Parent $PSScriptRoot
$project  = Join-Path $repoRoot 'tools\UiExtractor\UiExtractor.csproj'
$exe      = Join-Path $repoRoot 'tools\UiExtractor\bin\Release\net8.0\UiExtractor.exe'

if (-not (Test-Path $exe)) {
    Write-Host "Building UiExtractor..." -ForegroundColor Cyan
    dotnet build $project -c Release -nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Error "Build failed."; exit 1 }
}

$args = @()
if ($Module)     { $args += @('--module', $Module) }
if ($SsPid)      { $args += @('--pid', $SsPid) }
if ($OutputDir)  { $args += @('--output-dir', $OutputDir) }

& $exe @args
exit $LASTEXITCODE
