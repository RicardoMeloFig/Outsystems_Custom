<#
.SYNOPSIS
    Integrity gate for a linked consumer project. Must print READY and exit 0
    before the consumer is used.

.DESCRIPTION
    Checks a consumer project against its baseline:
      - opencode.json parses; exactly the 5 expected MCP servers, enabled,
        each command pointing at an existing baseline exe
      - default_agent, if set, points at an existing primary-mode agent;
        os-coordinator must NOT be the default (it is a subagent the
        primary agent calls for OutSystems work)
      - permission denies outsystems-* for the primary agent and allows the
        baseline tree via external_directory
      - subagent_depth >= 2 (primary -> os-coordinator -> specialists)
      - instructions include the consumer AGENTS.md and the baseline AGENTS.md
      - skills.paths covers the four baseline skill folders (with expected
        minimum skill counts, read live from the baseline)
      - the 8 agents exist (coordinator + architect + 6 specialists), use
        permission: (not deprecated tools:), carry the expected
        per-specialist allow keys, and reference the baseline path
        (stale-generation detection)
      - the 5 workflow commands exist
      - consumer skeleton folders and user files exist
      - baseline git commit drift vs the link manifest (warning only)

    Exit codes: 0 = READY, 1 = problems found.

.PARAMETER Target
    Consumer project root. Defaults to the current directory.

.PARAMETER Baseline
    Baseline workspace root. Defaults to this script's parent folder.

.EXAMPLE
    .\scripts\Verify-LinkedProject.ps1 -Target "C:\Users\me\Documents\NewAppAutomation"
#>
param(
    [string]$Target,
    [string]$Baseline
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($Baseline)) { $Baseline = Split-Path $PSScriptRoot -Parent }
$Baseline = [System.IO.Path]::GetFullPath($Baseline)
if ([string]::IsNullOrWhiteSpace($Target)) { $Target = (Get-Location).Path }
$Target = [System.IO.Path]::GetFullPath($Target)
$BaselineFwd = $Baseline -replace '\\', '/'

$fail = 0; $warn = 0
function Fail($m) { Write-Host "MISSING: $m" -ForegroundColor Red; $script:fail++ }
function Warn($m) { Write-Host "WARN:    $m" -ForegroundColor Yellow; $script:warn++ }
function Ok($m)   { Write-Host "OK:      $m" -ForegroundColor Green }

Write-Host "Verify-LinkedProject" -ForegroundColor Cyan
Write-Host "  target:   $Target" -ForegroundColor DarkGray
Write-Host "  baseline: $Baseline" -ForegroundColor DarkGray
Write-Host ""

# --- 1. opencode.json parses -------------------------------------------------------
$ocPath = Join-Path $Target "opencode.json"
if (-not (Test-Path -LiteralPath $ocPath)) {
    Fail "opencode.json not found at $ocPath"
    Write-Host "Verify-LinkedProject: NOT READY." -ForegroundColor Red; exit 1
}
try {
    $cfg = Get-Content -LiteralPath $ocPath -Raw | ConvertFrom-Json
    Ok "opencode.json parses"
} catch {
    Fail "opencode.json does not parse: $_"
    Write-Host "Verify-LinkedProject: NOT READY." -ForegroundColor Red; exit 1
}

# --- 2. MCP servers ----------------------------------------------------------------
$expectedServers = @{
    "outsystems-tools"      = "OutSystemsMcp.exe"
    "outsystems-logic"      = "OutSystemsMcpLogic.exe"
    "outsystems-ui"         = "OutSystemsMcpUi.exe"
    "outsystems-omleditor"  = "OutSystemsMcpOmlEditor.exe"
    "outsystems-liveeditor" = "OutSystemsMcpLiveEditor.exe"
}
$mcpNames = @()
if ($cfg.mcp) { $mcpNames = @($cfg.mcp.PSObject.Properties.Name) }
foreach ($name in @($expectedServers.Keys)) {
    $srv = $cfg.mcp.PSObject.Properties[$name].Value
    if (-not $srv) { Fail "mcp.$name entry missing"; continue }
    if ($srv.type -ne "local") { Fail "mcp.$name type must be 'local' (got '$($srv.type)')"; continue }
    if ($srv.enabled -ne $true) { Fail "mcp.$name is not enabled"; continue }
    $cmdArr = @($srv.command)
    if ($cmdArr.Count -lt 1) { Fail "mcp.$name command is empty"; continue }
    $exePath = $cmdArr[0]
    if ($exePath -notmatch '^[A-Za-z]:') { $exePath = [System.IO.Path]::GetFullPath((Join-Path $Target $exePath)) }
    if (Test-Path -LiteralPath $exePath) {
        if ([System.IO.Path]::GetFileName($exePath) -eq $expectedServers[$name]) { Ok "mcp.$name -> $exePath" }
        else { Fail "mcp.$name points at unexpected exe name: $exePath" }
    } else {
        Fail "mcp.$name exe not found: $exePath (build in the baseline: .\scripts\Build-All.ps1)"
    }
}
$missing = @($expectedServers.Keys | Where-Object { $mcpNames -notcontains $_ })
if ($missing.Count -gt 0) { Fail "MCP server(s) missing from opencode.json: $($missing -join ', ')" }
$extra = @($mcpNames | Where-Object { -not $expectedServers.ContainsKey($_) })
if ($extra.Count -gt 0) { Warn "extra MCP server(s) present: $($extra -join ', ')" }

# --- 3. Permission block -------------------------------------------------------------
if ($cfg.permission -and $cfg.permission.PSObject.Properties['outsystems-*'] -and $cfg.permission.'outsystems-*' -eq 'deny') {
    Ok "permission denies outsystems-* for the primary agent"
} else {
    Fail "permission['outsystems-*'] must be 'deny' (keeps MCP tools out of primary context)"
}
$extDir = $null
if ($cfg.permission) { $extDir = $cfg.permission.PSObject.Properties['external_directory'].Value }
if ($extDir) {
    $normBaseline = $Baseline.TrimEnd('\', '/')
    $covered = $false
    foreach ($pat in @($extDir.PSObject.Properties.Name)) {
        $normPat = ($pat -replace '/', '\').TrimEnd('\', '*').TrimEnd('\')
        if ($normPat -eq $normBaseline -or $normPat.StartsWith($normBaseline)) { $covered = $true; break }
    }
    if ($covered) { Ok "external_directory allows the baseline tree" }
    else { Fail "permission.external_directory does not cover the baseline ($Baseline) - agents cannot read baseline runbooks" }
} else {
    Fail "permission.external_directory missing (agents cannot read the baseline)"
}

# --- 4. Default agent (optional) ---------------------------------------------------------
if ($cfg.PSObject.Properties['default_agent'] -and $cfg.default_agent) {
    $daPath = Join-Path $Target ".opencode\agent\$($cfg.default_agent).md"
    if ($cfg.default_agent -eq "os-coordinator") {
        Fail "default_agent must not be 'os-coordinator' (it is a subagent; regenerate with New-LinkedProject.ps1 -Refresh)"
    } elseif (Test-Path -LiteralPath $daPath) {
        $da = [System.IO.File]::ReadAllText($daPath)
        if ($da -match '(?m)^mode:\s*primary') { Ok "default_agent = $($cfg.default_agent) (primary)" }
        else { Fail "default_agent '$($cfg.default_agent)' is not mode: primary" }
    } else {
        Fail "default_agent '$($cfg.default_agent)' has no agent file"
    }
} else {
    Ok "default_agent unset (normal primary agent routes OutSystems work to os-coordinator)"
}

# --- 5. subagent depth --------------------------------------------------------------------
$depth = 0
if ($cfg.PSObject.Properties['subagent_depth']) { $depth = [int]$cfg.subagent_depth }
if ($depth -ge 2) { Ok "subagent_depth = $depth (primary -> os-coordinator -> specialists)" }
else { Fail "subagent_depth must be >= 2 (primary -> os-coordinator -> specialist; got $depth)" }

# --- 6. instructions --------------------------------------------------------------------
$instr = @()
if ($cfg.instructions) { $instr = @($cfg.instructions) }
if ($instr -contains "AGENTS.md") { Ok "instructions includes consumer AGENTS.md" }
else { Fail "instructions must include 'AGENTS.md'" }
$baselineAgentsMd = "$BaselineFwd/AGENTS.md"
if ($instr -contains $baselineAgentsMd) { Ok "instructions includes baseline AGENTS.md" }
else { Warn "instructions does not include '$baselineAgentsMd' (baseline routing doc not auto-loaded)" }

# --- 7. Skills ---------------------------------------------------------------------------
$skillExpect = @{
    "toolkits/extraction/.opencode/skills" = 7
    "toolkits/editor/.opencode/skills"     = 26
    "toolkits/html-docs/.opencode/skills"  = 1
    ".opencode/skills"                     = 2
}
$skillPaths = @()
if ($cfg.skills -and $cfg.skills.paths) { $skillPaths = @($cfg.skills.paths) }
foreach ($k in @($skillExpect.Keys)) {
    $absFwd = "$BaselineFwd/$k"
    if ($skillPaths -contains $absFwd) {
        $abs = Join-Path $Baseline ($k -replace '/', '\')
        $n = @(Get-ChildItem -LiteralPath $abs -Directory -ErrorAction SilentlyContinue | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName "SKILL.md") }).Count
        if ($n -ge $skillExpect[$k]) { Ok "skills: $k ($n skills)" }
        else { Fail "skills: $k has $n skills in the baseline, expected >= $($skillExpect[$k])" }
    } else {
        Fail "skills.paths missing '$absFwd'"
    }
}

# --- 8. Agents -------------------------------------------------------------------
$agentExpect = @{
    "os-coordinator"   = @()
    "os-architect"     = @()
    "os-extract"       = @("outsystems-tools_*", "outsystems-logic_*", "outsystems-ui_*")
    "os-edit-headless" = @("outsystems-omleditor_*")
    "os-edit-live"     = @("outsystems-liveeditor_*")
    "os-docs"          = @()
    "os-reference"     = @()
    "os-toolkit"       = @()
}
foreach ($name in @($agentExpect.Keys)) {
    $p = Join-Path $Target ".opencode\agent\$name.md"
    if (-not (Test-Path -LiteralPath $p)) { Fail "agent $name.md missing"; continue }
    $raw = [System.IO.File]::ReadAllText($p)
    $agentOk = $true
    if ($raw -match '(?m)^tools:') { Fail "agent $name.md still uses deprecated 'tools:' frontmatter (regenerate)"; $agentOk = $false }
    if ($raw -notmatch '(?m)^permission:') { Fail "agent $name.md has no 'permission:' frontmatter (regenerate)"; $agentOk = $false }
    foreach ($key in $agentExpect[$name]) {
        if ($raw -notmatch [regex]::Escape('"' + $key + '": allow')) {
            Fail "agent $name.md: permission allow key `"$key`" missing (regenerate)"
            $agentOk = $false
        }
    }
    if ($agentExpect[$name].Count -eq 0 -and $raw -notmatch [regex]::Escape('"outsystems-*": deny')) {
        Fail "agent $name.md: expected broad deny `"outsystems-*`" (regenerate)"
        $agentOk = $false
    }
    if ($name -eq "os-coordinator" -and $raw -notmatch '(?m)^mode:\s*subagent') {
        Fail "agent os-coordinator.md: mode must be 'subagent' (called by the primary agent)"
        $agentOk = $false
    }
    if ($name -eq "os-architect" -and $raw -notmatch '(?m)^\s*edit:\s*deny') {
        Fail "agent os-architect.md: must deny 'edit' (read-only design gate)"
        $agentOk = $false
    }
    if ($raw -notmatch [regex]::Escape($BaselineFwd)) {
        Warn "agent $name.md: no baseline path found - possibly generated against a different baseline (stale?)"
    }
    if ($agentOk) { Ok "agent $name.md" }
}
$allAgents = @(Get-ChildItem -LiteralPath (Join-Path $Target ".opencode\agent") -Filter *.md -ErrorAction SilentlyContinue)
$extraAgents = @($allAgents | Where-Object { -not $agentExpect.ContainsKey($_.BaseName) })
if ($extraAgents.Count -gt 0) { Warn "extra agent file(s): $($extraAgents.Name -join ', ')" }

# --- 9. Commands --------------------------------------------------------------------------------
foreach ($name in @("extract-module", "document-module", "research", "build", "design")) {
    $p = Join-Path $Target ".opencode\command\$name.md"
    if (Test-Path -LiteralPath $p) { Ok "command $name.md" } else { Fail "command $name.md missing" }
}

# --- 10. references entry (optional) -----------------------------------------------------------------
if ($cfg.references -and $cfg.references.PSObject.Properties['outsystems-toolkit']) {
    Ok "references entry 'outsystems-toolkit' (baseline advertised)"
} else {
    Warn "references entry 'outsystems-toolkit' missing (baseline not advertised to agents)"
}

# --- 11. Consumer skeleton + user files -----------------------------------------------------------------
foreach ($d in @("OMLs", "open", "docs")) {
    if (Test-Path -LiteralPath (Join-Path $Target $d)) { Ok "folder $d\" } else { Fail "folder $d\ missing" }
}
foreach ($f in @("AGENTS.md", ".gitignore")) {
    if (Test-Path -LiteralPath (Join-Path $Target $f)) { Ok $f } else { Fail "$f missing" }
}

# --- 12. Baseline commit drift (warning only) ------------------------------------------------------------
$manifestPath = Join-Path $Target ".opencode\link-manifest.json"
if (Test-Path -LiteralPath $manifestPath) {
    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        $currentCommit = $null
        try {
            $currentCommit = (git -C $Baseline rev-parse HEAD 2>$null)
            if ($currentCommit) { $currentCommit = $currentCommit.Trim() } else { $currentCommit = $null }
        } catch { $currentCommit = $null }
        if ($manifest.baselineCommit -and $currentCommit -and $manifest.baselineCommit -ne $currentCommit) {
            Warn "baseline has moved since generation (manifest $($manifest.baselineCommit.Substring(0,10)) vs current $($currentCommit.Substring(0,10))) - refresh when convenient"
        } elseif (-not $manifest.baselineCommit) {
            Warn "manifest has no baselineCommit (generated before toolchain tracking) - refresh to record it"
        }
    } catch {
        Warn "link-manifest.json unreadable: $_"
    }
}

# --- Result ----------------------------------------------------------------------------------------------
Write-Host ""
if ($fail -gt 0) {
    Write-Host "Verify-LinkedProject: NOT READY - $fail problem(s), $warn warning(s)." -ForegroundColor Red
    Write-Host "Fix (usually): re-run New-LinkedProject.ps1 -Target `"$Target`" -Refresh, or rebuild the baseline exes." -ForegroundColor Yellow
    exit 1
}
Write-Host "Verify-LinkedProject: READY ($warn warning(s))." -ForegroundColor Green
exit 0
