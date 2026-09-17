<#
.SYNOPSIS
    Send a raw command to the OsLiveBridge named-pipe server inside a running Service Studio.

.DESCRIPTION
    The OsLiveBridge plugin (loaded inside SS) exposes a named pipe "OsLiveBridge-<SSpid>"
    speaking newline-delimited JSON. This script finds the SS process whose bridge holds the
    given module and sends one command, printing the response.

    Unlike PowerShell `ConvertTo-Json` (which wraps strings > ~4 KB into {"value":...}), the
    css/string fields are serialized with JavaScriptSerializer and concatenated, so long CSS
    payloads arrive at the bridge as plain strings.

    Use this to drive bridge commands that the MCP server does not (yet) expose, or when the
    MCP tool list is frozen for a session.

.PARAMETER Module
    Open module name (e.g. FitnessManager). Required.

.PARAMETER Cmd
    Bridge command name (e.g. create_theme, set_theme_css, probe_theme).

.PARAMETER JsonArgs
    A JSON *string* of the remaining fields to merge into the command object, e.g.
    '{"sourceName":"FitnessManager","newName":"ZombieTheme"}'.
    For CSS, embed it with a JavaScriptSerializer inside this string (see -Css).

.PARAMETER Css
    Optional CSS text; serialized safely (JavaScriptSerializer) into the command as "css".

.PARAMETER AllPids
    Send to every bridge pipe (bypasses module lookup). Use when the module is not open but
    you still want to hit the main IDE process (e.g. probe_theme on any pipe).

.EXAMPLE
    .\scripts\Send-BridgeCmd.ps1 -Module FitnessManager -Cmd probe_theme

.EXAMPLE
    .\scripts\Send-BridgeCmd.ps1 -Module FitnessManager -Cmd create_theme `
        -JsonArgs '{"sourceName":"FitnessManager","newName":"ZombieTheme"}'

.EXAMPLE
    .\scripts\Send-BridgeCmd.ps1 -Module FitnessManager -Cmd set_theme_css `
        -JsonArgs '{"themeName":"ZombieTheme"}' -Css $bigCss
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$Module,

    [Parameter(Mandatory = $true)]
    [string]$Cmd,

    [Parameter(Mandatory = $false)]
    [string]$JsonArgs = '{}',

    [Parameter(Mandatory = $false)]
    [string]$Css,

    [switch]$AllPids
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Web.Extensions
$ser = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$ser.MaxJsonLength = 10485760

function Get-BridgePids {
    $pids = @()
    foreach ($p in (Get-Process -Name ServiceStudio -ErrorAction SilentlyContinue)) {
        try { $pids += $p.Id } finally { try { $p.Dispose() } catch { } }
    }
    return $pids
}

function Send-One([int]$bridgePid, [string]$line) {
    $client = New-Object System.IO.Pipes.NamedPipeClientStream(".", "OsLiveBridge-$bridgePid", [System.IO.Pipes.PipeDirection]::InOut)
    try {
        $client.Connect(8000)
        $writer = New-Object System.IO.StreamWriter($client)
        $writer.AutoFlush = $true
        $reader = New-Object System.IO.StreamReader($client)
        $writer.WriteLine($line)
        $resp = $reader.ReadLine()
        return $resp
    }
    finally {
        try { $client.Dispose() } catch { }
    }
}

# Build the command line: serialize css safely, merge JsonArgs.
$body = "`"cmd`":`"$($Cmd.Replace('"','\"'))`""
if ($Module) { $body += ",`"module`":`"" + $Module.Replace('"','\"') + "`"" }
if ($JsonArgs -and $JsonArgs -ne '{}') {
    $argsTrim = $JsonArgs.TrimStart('{').TrimEnd('}')
    if ($argsTrim) { $body += "," + $argsTrim }
}
if ($PSBoundParameters.ContainsKey('Css')) {
    $body += ",`"css`":" + $ser.Serialize([string]$Css)
}
$line = '{' + $body + '}'

$targets = @()
if ($AllPids) {
    $targets = Get-BridgePids
}
else {
    # Find the SS pid whose bridge holds the module (list_modules contains it).
    foreach ($bridgePid in (Get-BridgePids)) {
        try {
            $mods = Send-One $bridgePid '{"cmd":"list_modules"}'
            if ($mods -match "\`"$($Module.Replace('\','\\'))\`"") { $targets += $bridgePid }
        }
        catch { }
    }
    if ($targets.Count -eq 0) {
        Write-Error "no bridge pipe found holding module '$Module'. Is SS running with OsLiveBridge and is '$Module' open?"
        exit 1
    }
}

if ($targets.Count -eq 0) {
    Write-Error "no ServiceStudio processes found with the OsLiveBridge plugin."
    exit 1
}

foreach ($bridgePid in $targets) {
    Write-Host "==> sending '$Cmd' to SS pid $bridgePid" -ForegroundColor Cyan
    Write-Host "    payload: $line" -ForegroundColor DarkGray
    $resp = Send-One $bridgePid $line
    Write-Host $resp
}
