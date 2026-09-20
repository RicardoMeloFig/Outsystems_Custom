<#
.SYNOPSIS
    Workspace integrity gate. Must print OK: and exit 0 before use.

.DESCRIPTION
    Checks: 5 MCP exes present + fresh, skills discoverable in all four
    paths, opencode.json valid JSON (+ default_agent valid if set),
    coordinator is a subagent (not default), agents present
    (coordinator + architect + 6 specialists), commands present, catalog
    present (incl. toolchain.json), reference library populated, AGENTS.md
    present. -Strict turns staleness into a hard failure.
#>
param([switch]$Strict)

$ErrorActionPreference = "Stop"
$Root = Split-Path $PSScriptRoot -Parent
$fail = 0; $warn = 0
function Fail($m) { Write-Host "MISSING: $m" -ForegroundColor Red; $script:fail++ }
function Warn($m) { Write-Host "STALE:   $m" -ForegroundColor Yellow; $script:warn++ }
function Ok($m)   { Write-Host "OK:      $m" -ForegroundColor Green }

# Core files
foreach ($f in @("opencode.json","AGENTS.md","catalog\repositories.json","catalog\topics.md","catalog\toolchain.json")) {
    if (Test-Path -LiteralPath (Join-Path $Root $f)) { Ok $f } else { Fail $f }
}

# opencode.json parses + toolchain manifest parses
try {
    $cfg = Get-Content -LiteralPath (Join-Path $Root "opencode.json") -Raw | ConvertFrom-Json
    $mcpNames = @($cfg.mcp.PSObject.Properties.Name)
    Ok "opencode.json valid ($($mcpNames.Count) MCP servers)"
} catch { Fail "opencode.json does not parse: $_" }
try {
    $null = Get-Content -LiteralPath (Join-Path $Root "catalog\toolchain.json") -Raw | ConvertFrom-Json
    Ok "catalog\toolchain.json valid"
} catch { Fail "catalog\toolchain.json does not parse: $_" }

# MCP exes
$serverDirs = Get-ChildItem -LiteralPath (Join-Path $Root "toolkits") -Directory | ForEach-Object {
    Get-ChildItem -LiteralPath (Join-Path $_.FullName "mcp") -Directory -ErrorAction SilentlyContinue
} | Where-Object { $_ -and (Test-Path (Join-Path $_.FullName "*.csproj")) }
foreach ($dir in $serverDirs) {
    $exe = Get-ChildItem -LiteralPath (Join-Path $dir.FullName "publish") -Filter *.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $exe) { Fail "$($dir.Name): publish exe not built (run scripts\Build-All.ps1)"; continue }
    $newestSrc = Get-ChildItem -LiteralPath $dir.FullName -Recurse -Include *.cs,*.csproj -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(publish|bin|obj)\\' } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($newestSrc -and $newestSrc.LastWriteTime -gt $exe.LastWriteTime) {
        if ($Strict) { Fail "$($dir.Name): exe is STALE (source newer) - rebuild" } else { Warn "$($dir.Name): exe is STALE - rebuild before extracting" }
    } else { Ok "$($dir.Name): exe fresh" }
}

# Skills
$expected = @{ "toolkits\extraction\.opencode\skills" = 7; "toolkits\editor\.opencode\skills" = 26; "toolkits\html-docs\.opencode\skills" = 1; ".opencode\skills" = 2 }
foreach ($k in $expected.Keys) {
    $p = Join-Path $Root $k
    $n = @(Get-ChildItem -LiteralPath $p -Directory -ErrorAction SilentlyContinue | Where-Object { Test-Path (Join-Path $_.FullName "SKILL.md") }).Count
    if ($n -ge $expected[$k]) { Ok "$k skills ($n)" } else { Fail "$k skills: found $n, expected $($expected[$k])" }
}

# Agents (coordinator + architect + 6 specialists)
$agents = @(Get-ChildItem -LiteralPath (Join-Path $Root ".opencode\agent") -Filter *.md -ErrorAction SilentlyContinue)
if ($agents.Count -ge 8) { Ok "agents ($($agents.Count))" } else { Fail ".opencode\agent: found $($agents.Count), expected 8" }
foreach ($req in @("os-coordinator.md","os-architect.md")) {
    $p = Join-Path $Root ".opencode\agent\$req"
    if (Test-Path -LiteralPath $p) {
        $raw = [System.IO.File]::ReadAllText($p)
        if ($req -eq "os-coordinator.md" -and $raw -notmatch '(?m)^mode:\s*subagent') { Fail "os-coordinator.md: mode must be 'subagent' (called by the primary agent, not a default agent)" }
        elseif ($req -eq "os-architect.md" -and $raw -notmatch '(?m)^\s*edit:\s*deny') { Fail "os-architect.md: must deny 'edit' (read-only design gate)" }
        else { Ok $req }
    } else { Fail ".opencode\agent\$req" }
}
# default_agent: optional; if set it must point at an existing primary-mode agent
$defaultAgent = $null
if ($cfg -and $cfg.PSObject.Properties['default_agent']) { $defaultAgent = $cfg.default_agent }
if ($defaultAgent) {
    $daPath = Join-Path $Root ".opencode\agent\$defaultAgent.md"
    if (Test-Path -LiteralPath $daPath) {
        $da = [System.IO.File]::ReadAllText($daPath)
        if ($da -match '(?m)^mode:\s*primary') { Ok "default_agent = $defaultAgent (primary)" }
        else { Fail "default_agent '$defaultAgent' is not mode: primary" }
    } else {
        Fail "default_agent '$defaultAgent' has no agent file"
    }
} else {
    Ok "default_agent unset (normal primary agent routes OutSystems work to os-coordinator)"
}

# Commands
$commands = @(Get-ChildItem -LiteralPath (Join-Path $Root ".opencode\command") -Filter *.md -ErrorAction SilentlyContinue)
if ($commands.Count -ge 5) { Ok "commands ($($commands.Count))" } else { Fail ".opencode\command: found $($commands.Count), expected 5" }

# Reference library
$repos = @(Get-ChildItem -LiteralPath (Join-Path $Root "references\repos") -Directory -ErrorAction SilentlyContinue)
if ($repos.Count -ge 230) { Ok "reference library ($($repos.Count) repos)" } else { Fail "references\repos: found $($repos.Count), expected ~236" }

Write-Host ""
if ($fail -gt 0) { Write-Host "Verify-Project: $fail missing/stale item(s). Fix before use." -ForegroundColor Red; exit 1 }
Write-Host "Verify-Project: OK ($warn warning(s))." -ForegroundColor Green
exit 0
