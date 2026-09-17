# Test-Client.ps1 - drives the OsLiveBridge named-pipe server inside Service Studio.
# Usage:
#   .\Test-Client.ps1                       # smoke: ping + list_modules + module_info + try_create_service_action (on first module)
#   .\Test-Client.ps1 -Cmd ping
#   .\Test-Client.ps1 -Cmd list_modules
#   .\Test-Client.ps1 -Cmd module_info -Module MyModule
#   .\Test-Client.ps1 -Cmd try_create_service_action -Module MyModule -Name BridgeTestSvc
param(
  [string]$Cmd = "smoke",
  [string]$Module,
  [string]$Name = "BridgeTestSvc",
  [int]$SSPid
)
$ErrorActionPreference = "Stop"

function Resolve-SsPid {
  param([int]$SSPid)
  if ($SSPid) { return $SSPid }
  $procs = Get-Process -Name ServiceStudio -ErrorAction SilentlyContinue
  if (-not $procs -or $procs.Count -eq 0) { throw "Service Studio is not running. Start it (plugin loads at startup) and open a module." }
  if ($procs.Count -gt 1) { throw "Multiple Service Studio instances (PIDs: $($procs.Id -join ', ')). Pass -SSPid." }
  return $procs[0].Id
}

function Send-Bridge {
  param([string]$Pipe, [string]$JsonReq, [int]$TimeoutMs = 8000)
  $s = New-Object System.IO.Pipes.NamedPipeClientStream(".", $Pipe, [System.IO.Pipes.PipeDirection]::InOut)
  try {
    $s.Connect($TimeoutMs)
    $r = New-Object System.IO.StreamReader($s)
    $w = New-Object System.IO.StreamWriter($s); $w.AutoFlush = $true
    $w.WriteLine($JsonReq)
    return $r.ReadLine()
  } finally { $s.Dispose() }
}

$pidv = Resolve-SsPid -SSPid $SSPid
$pipe = "OsLiveBridge-$pidv"
Write-Host ">> SS pid=$pidv pipe=$pipe" -ForegroundColor Cyan

function Run([string]$jsonReq, [string]$label) {
  Write-Host "`n--- $label ---" -ForegroundColor Yellow
  try {
    $resp = Send-Bridge -Pipe $pipe -JsonReq $jsonReq
    Write-Host $resp
    return $resp | ConvertFrom-Json
  } catch { Write-Host "ERR: $($_.Exception.Message)" -ForegroundColor Red; return $null }
}

if ($Cmd -eq "smoke") {
  $p = Run '{"cmd":"ping"}' "ping"
  if (-not $p -or -not $p.ok) { Write-Host "ping failed - bridge not loaded? Check %TEMP%\OsLiveBridge.log" -ForegroundColor Red; return }
  $lm = Run '{"cmd":"list_modules"}' "list_modules"
  if (-not $lm -or -not $lm.modules -or $lm.modules.Count -eq 0) { Write-Host "no open modules - open a throwaway module in SS first" -ForegroundColor Red; return }
  $mod = if ($Module) { $Module } else { $lm.modules[0] }
  Write-Host "using module: $mod" -ForegroundColor Cyan
  Run "{`"cmd`":`"module_info`",`"module`":`"$mod`"}" "module_info"
  Run "{`"cmd`":`"try_create_service_action`",`"module`":`"$mod`",`"name`":`"$Name`"}" "try_create_service_action (MUTATES - creates '$Name')"
  Write-Host "`n>> Check SS: does a new Service Action '$Name' appear in the tree (no reload)?" -ForegroundColor Green
  return
}

$req = switch ($Cmd) {
  "ping" { '{"cmd":"ping"}' }
  "list_modules" { '{"cmd":"list_modules"}' }
  "module_info" { "{`"cmd`":`"module_info`",`"module`":`"$Module`"}" }
  "try_create_service_action" { "{`"cmd`":`"try_create_service_action`",`"module`":`"$Module`",`"name`":`"$Name`"}" }
  default { throw "unknown cmd: $Cmd" }
}
Run $req $Cmd
