<#
.SYNOPSIS
    Open a local .oml module in Service Studio via its command-line interface.

.DESCRIPTION
    Wraps the Service Studio CLI (documented in the official O11 docs,
    docs-product\src\ref\ss-command\ss-command-line.md - summarized; commands
    verified there):

        servicestudio.exe <module.oml>          open a locally saved module
        servicestudio.exe -diff <a.oml> <b.oml> open Compare and Merge (no merge)
        servicestudio.exe -merge <a.oml> <b.oml> attempt an automatic merge
        servicestudio.exe -recover <module.oml> recover meta-info from a corrupted .oml
        servicestudio.exe -refresh <oml> <verify.xml> <host> <user> <pass>
                                                refresh references against an env + log

    This is the reload step of the headless editing loop: after an
    outsystems-omleditor write (omlPath -> outOml), open the result:

        .\scripts\Open-OmlInSS.ps1 -OmlPath <outOml>

.PARAMETER OmlPath
    The .oml to open (or the LOCAL module for -diff/-merge - the left side,
    labeled "Your version").

.PARAMETER OmlPath2
    Second .oml for -diff / -merge (right side, "The other version").

.PARAMETER Diff
    Open the Compare and Merge window (does not merge).

.PARAMETER Merge
    Attempt an automatic merge (Compare and Merge opens with mergeable
    elements pre-selected).

.PARAMETER Recover
    Recover module meta-information from a corrupted .oml. Use when SS errors
    or crashes on operations specific to one module.

.PARAMETER Refresh
    Refresh the module's references against an environment. Requires
    -HostName, -VerifyXml and credentials.

.PARAMETER HostName
    Environment hostname for -refresh (e.g. dev.example.com).

.PARAMETER VerifyXml
    Output path for the -refresh error log (verify.xml).

.PARAMETER UserName / Password
    Credentials for -refresh.

.PARAMETER SsDir
    Service Studio install dir. Defaults to the standard path or $env:OSSS_DIR.

.EXAMPLE
    .\scripts\Open-OmlInSS.ps1 -OmlPath "C:\temp\FitnessManager_edited.oml"

.EXAMPLE
    .\scripts\Open-OmlInSS.ps1 -OmlPath a.oml -OmlPath2 b.oml -Diff

.EXAMPLE
    .\scripts\Open-OmlInSS.ps1 -OmlPath mod.oml -Recover

.EXAMPLE
    .\scripts\Open-OmlInSS.ps1 -OmlPath mod.oml -Refresh -HostName dev.example.com -VerifyXml C:\temp\verify.xml -UserName admin -Password $pwd
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OmlPath,

    [Parameter(Mandatory = $false)]
    [string]$OmlPath2,

    [Parameter(Mandatory = $false)]
    [switch]$Diff,

    [Parameter(Mandatory = $false)]
    [switch]$Merge,

    [Parameter(Mandatory = $false)]
    [switch]$Recover,

    [Parameter(Mandatory = $false)]
    [switch]$Refresh,

    [Parameter(Mandatory = $false)]
    [string]$HostName,

    [Parameter(Mandatory = $false)]
    [string]$VerifyXml,

    [Parameter(Mandatory = $false)]
    [string]$UserName,

    [Parameter(Mandatory = $false)]
    [string]$Password,

    [Parameter(Mandatory = $false)]
    [string]$SsDir
)

$ErrorActionPreference = 'Stop'

if (-not $SsDir) {
    if ($env:OSSS_DIR) { $SsDir = $env:OSSS_DIR }
    else { $SsDir = 'C:\Program Files\OutSystems\Service Studio 11\Service Studio' }
}

$ss = Join-Path $SsDir 'servicestudio.exe'
if (-not (Test-Path -LiteralPath $ss)) {
    Write-Error ("Service Studio not found at '{0}'. Install SS 11 or set OSSS_DIR / -SsDir." -f $ss)
    exit 1
}

if (-not (Test-Path -LiteralPath $OmlPath)) {
    Write-Error ("Module not found: {0}" -f $OmlPath)
    exit 1
}

# Exclusive-mode guard: SS locks the .oml of a module it has open. Opening the
# SAME file that a running SS already shows will fail or attach confusingly.
$openSs = Get-Process -Name 'ServiceStudio' -ErrorAction SilentlyContinue
if ($openSs -and -not ($Diff -or $Merge)) {
    Write-Warning 'Service Studio is already running. If it has THIS module open, close it there first (the file is locked); opening another module in a second instance is fine.'
}

if ($Diff -or $Merge) {
    if (-not $OmlPath2) { Write-Error '-Diff/-Merge need -OmlPath2 (the second .oml).'; exit 1 }
    if (-not (Test-Path -LiteralPath $OmlPath2)) { Write-Error ("Module not found: {0}" -f $OmlPath2); exit 1 }
    $mode = if ($Diff) { '-diff' } else { '-merge' }
    Write-Output ("> {0} {1} {2} {3}" -f $ss, $mode, $OmlPath, $OmlPath2)
    & $ss $mode $OmlPath $OmlPath2
    return
}

if ($Recover) {
    Write-Output ("> {0} -recover {1}" -f $ss, $OmlPath)
    & $ss -recover $OmlPath
    return
}

if ($Refresh) {
    foreach ($req in @(
        @{ v = $HostName;  n = '-HostName' },
        @{ v = $VerifyXml; n = '-VerifyXml' },
        @{ v = $UserName;  n = '-UserName' },
        @{ v = $Password;  n = '-Password' }
    )) {
        if ([string]::IsNullOrEmpty($req.v)) { Write-Error ("-Refresh requires {0}." -f $req.n); exit 1 }
    }
    Write-Output ("> {0} -refresh {1} {2} {3} {4} ***" -f $ss, $OmlPath, $VerifyXml, $HostName, $UserName)
    & $ss -refresh $OmlPath $VerifyXml $HostName $UserName $Password
    Write-Output ("refresh kicked off. Error log (when it finishes): {0}" -f $VerifyXml)
    return
}

# Default: open the module (the headless-edit reload step).
Write-Output ("> {0} {1}" -f $ss, $OmlPath)
& $ss $OmlPath
Write-Output 'Service Studio launching. The new/edited elements appear in the tree after the module loads.'
