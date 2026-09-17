<#
.SYNOPSIS
    List the OutSystems UI component CSS API variables (--osui-*) defined for a
    component, from a compiled OutSystemsUI stylesheet.

.DESCRIPTION
    OutSystems UI styles overridable properties through CSS custom properties:
        background-color: var(--osui-tooltip-background-color);
    Overriding the variable beats overriding the rule (see
    docs/ui-styling-reference.md, "The OutSystemsUI CSS override hierarchy").

    This script greps a compiled OutSystemsUI bundle for the --osui- variables
    of one component (or all components) and prints the variable name with the
    fallback/role it maps to. Default bundle: the O11 classic-theme snapshot in
    the OutSystems ground-of-truth repo (override with -CssPath to point at
    your environment's deployed OutSystemsUI CSS).

.PARAMETER Component
    Component slug, e.g. 'tooltip', 'layout', 'menu', 'balloon'. Matched as a
    prefix against --osui-{component}-*. Omit for all components.

.PARAMETER CssPath
    Path to a compiled OutSystemsUI .css file. Defaults to the O11 classic-theme
    bundle relative to the repo root.

.EXAMPLE
    .\scripts\Get-OsuiVars.ps1 -Component tooltip

.EXAMPLE
    .\scripts\Get-OsuiVars.ps1                       # all --osui-* variables
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$Component,

    [Parameter(Mandatory = $false)]
    [string]$CssPath
)

$ErrorActionPreference = 'Stop'

if (-not $CssPath) {
    $candidate = Join-Path (Split-Path -Parent $PSScriptRoot) '..\Outsystems REPO\outsystems-ui\classic-theme\O11.OutSystemsUI.css'
    $CssPath = [System.IO.Path]::GetFullPath($candidate)
}

if (-not (Test-Path -LiteralPath $CssPath)) {
    Write-Error ("CSS bundle not found: {0} - pass -CssPath pointing at your OutSystemsUI stylesheet." -f $CssPath)
    exit 1
}

$css = Get-Content -LiteralPath $CssPath -Raw

if ($Component) {
    $pattern = '--osui-' + $Component + '-[a-z0-9-]+'
}
else {
    $pattern = '--osui-[a-z0-9-]+'
}

$defs = [regex]::Matches($css, '(' + $pattern + ')\s*:\s*([^;}]+)')
$uses = [regex]::Matches($css, 'var\(\s*(' + $pattern + ')')

$defined = @{}
foreach ($m in $defs) {
    $name = $m.Groups[1].Value
    if (-not $defined.ContainsKey($name)) { $defined[$name] = $m.Groups[2].Value.Trim() }
}
$used = @($uses | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)

Write-Output ("bundle: {0}" -f $CssPath)
Write-Output ("defined --osui-* variables: {0}   used in var(): {1}" -f $defined.Count, $used.Count)
Write-Output ''

$keys = @($defined.Keys | Sort-Object)
if ($Component) {
    Write-Output ('== {0} ==' -f $Component)
}
foreach ($k in $keys) {
    Write-Output ('  {0} = {1}' -f $k, $defined[$k])
}

if ($Component) {
    $missing = @($used | Where-Object { -not $defined.ContainsKey($_) } | Sort-Object -Unique)
    if ($missing.Count -gt 0) {
        Write-Output ''
        Write-Output '  (used in var() but not matched by the definition pass - defined on another line form:)'
        foreach ($k in $missing) { Write-Output ('  {0}' -f $k) }
    }
}
