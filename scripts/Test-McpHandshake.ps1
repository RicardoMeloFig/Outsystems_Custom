<#
.SYNOPSIS
    Best-effort MCP stdio handshake probe: initialize + tools/list against one
    or all of the baseline's MCP server exes.

.DESCRIPTION
    Spawns each server exe, performs the MCP stdio handshake (initialize ->
    notifications/initialized -> tools/list), reports the tool count, then
    kills the process. This verifies that a built exe actually speaks the
    protocol BEFORE relying on it from opencode - it does NOT call any tool,
    so no Service Studio state is touched.

    Newline-delimited JSON-RPC 2.0 over stdin/stdout, per the MCP stdio
    transport. Best-effort: a server that logs heavily to stderr could in
    theory wedge the pipe buffer; in that case the probe times out, kills
    the process, and reports FAIL with the captured stderr tail.

    Exit codes: 0 = all probed servers OK, 1 = at least one failed.

.PARAMETER Name
    Probe a single server by name (outsystems-tools, outsystems-logic,
    outsystems-ui, outsystems-omleditor, outsystems-liveeditor).

.PARAMETER Exe
    Probe an explicit exe path instead of a named baseline server.

.PARAMETER Baseline
    Baseline workspace root. Defaults to this script's parent folder.

.PARAMETER TimeoutSec
    Per-response timeout in seconds (default 20).

.EXAMPLE
    .\scripts\Test-McpHandshake.ps1                    # all five servers
    .\scripts\Test-McpHandshake.ps1 -Name outsystems-ui
#>
param(
    [string]$Name,
    [string]$Exe,
    [string]$Baseline,
    [int]$TimeoutSec = 20
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($Baseline)) { $Baseline = Split-Path $PSScriptRoot -Parent }
$Baseline = [System.IO.Path]::GetFullPath($Baseline)

$servers = @{
    "outsystems-tools"      = "toolkits\extraction\mcp\outsystems-tools\publish\OutSystemsMcp.exe"
    "outsystems-logic"      = "toolkits\extraction\mcp\outsystems-logic\publish\OutSystemsMcpLogic.exe"
    "outsystems-ui"         = "toolkits\extraction\mcp\outsystems-ui\publish\OutSystemsMcpUi.exe"
    "outsystems-omleditor"  = "toolkits\editor\mcp\outsystems-omleditor\publish\OutSystemsMcpOmlEditor.exe"
    "outsystems-liveeditor" = "toolkits\editor\mcp\outsystems-liveeditor\publish\OutSystemsMcpLiveEditor.exe"
}

# Resolve probe set
$probeSet = @()   # @( @{ Name; Exe } )
if ($Exe) {
    $exePath = [System.IO.Path]::GetFullPath($Exe)
    if (-not (Test-Path -LiteralPath $exePath)) { Write-Host "ERROR: exe not found: $exePath" -ForegroundColor Red; exit 1 }
    $probeSet += @{ Name = [System.IO.Path]::GetFileNameWithoutExtension($exePath); Exe = $exePath }
} elseif ($Name) {
    if (-not $servers.ContainsKey($Name)) {
        Write-Host "ERROR: unknown server '$Name'. Known: $($servers.Keys -join ', ')" -ForegroundColor Red; exit 1
    }
    $exePath = Join-Path $Baseline $servers[$Name]
    if (-not (Test-Path -LiteralPath $exePath)) { Write-Host "ERROR: exe not found: $exePath (run Build-All.ps1)" -ForegroundColor Red; exit 1 }
    $probeSet += @{ Name = $Name; Exe = $exePath }
} else {
    foreach ($k in @($servers.Keys)) {
        $exePath = Join-Path $Baseline $servers[$k]
        if (-not (Test-Path -LiteralPath $exePath)) {
            Write-Host "FAIL    ${k}: exe not found: $exePath" -ForegroundColor Red
            continue
        }
        $probeSet += @{ Name = $k; Exe = $exePath }
    }
}

function Read-RpcResponse {
    param($Reader, [int]$Id, [int]$TimeoutSec)
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSec)
    while ([DateTime]::UtcNow -lt $deadline) {
        $remainingMs = [int](($deadline - [DateTime]::UtcNow).TotalMilliseconds)
        if ($remainingMs -lt 100) { $remainingMs = 100 }
        $task = $Reader.ReadLineAsync()
        if (-not $task.Wait($remainingMs)) { return $null }   # timeout
        $line = $task.Result
        if ($null -eq $line) { return $null }                 # EOF: server exited
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        try { $obj = $line | ConvertFrom-Json } catch { continue }
        if ($obj.PSObject.Properties['id'] -and [int]$obj.id -eq $Id) { return $obj }
        # notification or unrelated message - keep reading
    }
    return $null
}

function Invoke-McpProbe {
    param([string]$ServerName, [string]$ExePath)
    $result = @{ Ok = $false; Tools = -1; Detail = ""; StdErr = "" }
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $ExePath
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $psi.WorkingDirectory = $env:TEMP
    $p = New-Object System.Diagnostics.Process
    $p.StartInfo = $psi
    if (-not $p.Start()) { $result.Detail = "failed to start process"; return $result }
    try {
        $sw = $p.StandardInput
        $sr = $p.StandardOutput
        $initReq = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"handshake-probe","version":"1.0"}}}'
        $sw.WriteLine($initReq); $sw.Flush()
        $initResp = Read-RpcResponse -Reader $sr -Id 1 -TimeoutSec $TimeoutSec
        if (-not $initResp) { $result.Detail = "no/timeout response to initialize"; return $result }
        if ($initResp.PSObject.Properties['error'] -and $initResp.error) {
            $result.Detail = "initialize error: $($initResp.error.message)"; return $result
        }
        $sw.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}'); $sw.Flush()
        $sw.WriteLine('{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}'); $sw.Flush()
        $toolsResp = Read-RpcResponse -Reader $sr -Id 2 -TimeoutSec $TimeoutSec
        if (-not $toolsResp) { $result.Detail = "initialized OK but no/timeout response to tools/list"; return $result }
        if ($toolsResp.PSObject.Properties['error'] -and $toolsResp.error) {
            $result.Detail = "tools/list error: $($toolsResp.error.message)"; return $result
        }
        $toolsArr = @()
        if ($toolsResp.result -and $toolsResp.result.tools) { $toolsArr = @($toolsResp.result.tools) }
        $result.Ok = $true
        $result.Tools = $toolsArr.Count
        $srvName = ""
        if ($initResp.result -and $initResp.result.serverInfo -and $initResp.result.serverInfo.name) { $srvName = $initResp.result.serverInfo.name }
        $result.Detail = "handshake OK (server: $srvName)"
        return $result
    } finally {
        try { if (-not $p.HasExited) { $p.Kill() } } catch { }
        try { $p.WaitForExit(5000) | Out-Null } catch { }
        try { $result.StdErr = $p.StandardError.ReadToEnd() } catch { }
        $p.Dispose()
    }
}

Write-Host "Test-McpHandshake (timeout ${TimeoutSec}s per response)" -ForegroundColor Cyan
$failed = 0
foreach ($probe in $probeSet) {
    $r = Invoke-McpProbe -ServerName $probe.Name -ExePath $probe.Exe
    if ($r.Ok) {
        Write-Host ("OK      {0}: {1}, {2} tools" -f $probe.Name, $r.Detail, $r.Tools) -ForegroundColor Green
    } else {
        Write-Host ("FAIL    {0}: {1}" -f $probe.Name, $r.Detail) -ForegroundColor Red
        if ($r.StdErr) {
            $tail = (@($r.StdErr -split "`r?`n") | Where-Object { $_ } | Select-Object -First 3) -join " | "
            if ($tail) { Write-Host "        stderr: $tail" -ForegroundColor DarkYellow }
        }
        $failed++
    }
}
Write-Host ""
if ($failed -gt 0) { Write-Host "Test-McpHandshake: $failed of $($probeSet.Count) FAILED." -ForegroundColor Red; exit 1 }
Write-Host "Test-McpHandshake: all $($probeSet.Count) probed server(s) OK." -ForegroundColor Green
exit 0
