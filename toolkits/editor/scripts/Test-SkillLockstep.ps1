<#
.SYNOPSIS
    Consistency checks for the skills/docs set (lockstep grep + structure).

.DESCRIPTION
    Modeled on the official outsystems-mcp repo's lockstep practice
    (their CLAUDE.md greps a distinctive phrase across the five parallel skill
    docs and requires equal counts). This project has one canonical skill per
    topic instead of five parallel copies, so the script offers three checks:

    1. DEFAULT (structure): every .opencode/skills/*/SKILL.md has frontmatter
       with a `name:` matching its folder and a non-empty `description:`.
    2. -Conventions: the behavioral conventions ("Never guess opaque keys",
       "Succeeded != landed", "Confirm before destructive") are present in
       AGENTS.md and mirrored in the live-editing skill.
    3. -Phrase "..." -Files a.md,b.md,...: manual lockstep grep - prints the
       per-file match counts and fails (exit 1) when the counts differ, unless
       -AllowMissing lists files where 0 is expected (setup-recipe exception,
       same rule as the official repo: "state the real numbers").

.EXAMPLE
    .\scripts\Test-SkillLockstep.ps1

.EXAMPLE
    .\scripts\Test-SkillLockstep.ps1 -Conventions

.EXAMPLE
    .\scripts\Test-SkillLockstep.ps1 -Phrase "user CSS source" -Files "docs/ui-styling-reference.md",".opencode/skills/styling-and-css-live/SKILL.md"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$Phrase,

    [Parameter(Mandatory = $false)]
    [string]$Files,

    [Parameter(Mandatory = $false)]
    [string]$AllowMissing,

    [Parameter(Mandatory = $false)]
    [switch]$Conventions
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$fail = $false

# ---- Mode 3: manual lockstep grep -----------------------------------------
if ($Phrase) {
    if (-not $Files) { Write-Error '-Phrase requires -Files "a.md,b.md,...".'; exit 1 }
    $allow = @(); if ($AllowMissing) { $allow = $AllowMissing.Split(',') | ForEach-Object { $_.Trim() } }
    $counts = @{}
    foreach ($f in $Files.Split(',') | ForEach-Object { $_.Trim() }) {
        $p = Join-Path $repo $f
        if (-not (Test-Path -LiteralPath $p)) { Write-Output "MISSING-FILE  $f"; $fail = $true; continue }
        $c = ([regex]::Matches((Get-Content -LiteralPath $p -Raw), [regex]::Escape($Phrase))).Count
        $counts[$f] = $c
        Write-Output ("{0,3}  {1}" -f $c, $f)
    }
    $expected = @($counts.Keys | Where-Object { $allow -notcontains $_ })
    $distinct = @($expected | ForEach-Object { $counts[$_] } | Sort-Object -Unique)
    if ($distinct.Count -gt 1) {
        Write-Output 'FAIL: counts differ across files that must stay in lockstep (phrase drift).'
        $fail = $true
    } else {
        Write-Output 'OK: lockstep counts match (with the stated exceptions).'
    }
    if ($fail) { exit 1 } else { exit 0 }
}

# ---- Mode 2: conventions mirror --------------------------------------------
if ($Conventions) {
    $agents = Join-Path $repo 'AGENTS.md'
    $live = Join-Path $repo '.opencode\skills\live-editing\SKILL.md'
    $required = @(
        @{ phrase = 'Never guess opaque keys';            files = @($agents) },
        @{ phrase = 'Succeeded';                          files = @($agents, $live) },
        @{ phrase = 'Confirm before destructive';         files = @($agents) }
    )
    foreach ($r in $required) {
        foreach ($f in $r.files) {
            if (-not (Test-Path -LiteralPath $f)) { Write-Output "MISSING-FILE  $f"; $fail = $true; continue }
            $c = ([regex]::Matches((Get-Content -LiteralPath $f -Raw), [regex]::Escape($r.phrase))).Count
            $status = if ($c -gt 0) { 'OK' } else { 'FAIL'; $fail = $true }
            Write-Output ("{0,-14} {1,3}x  '{2}'  in {3}" -f $status, $c, $r.phrase, (Split-Path -Leaf $f))
        }
    }
    if ($fail) { exit 1 } else { Write-Output 'OK: conventions present in canonical files.'; exit 0 }
}

# ---- Mode 1: structure (default) -------------------------------------------
$skillsDir = Join-Path $repo '.opencode\skills'
$skills = Get-ChildItem -LiteralPath $skillsDir -Directory | Sort-Object Name
Write-Output ("skills found: {0}" -f $skills.Count)
foreach ($s in $skills) {
    $sk = Join-Path $s.FullName 'SKILL.md'
    if (-not (Test-Path -LiteralPath $sk)) {
        Write-Output ("FAIL  {0}: no SKILL.md" -f $s.Name); $fail = $true; continue
    }
    $raw = Get-Content -LiteralPath $sk -Raw
    $fm = [regex]::Match($raw, '(?s)^---\s*(.*?)---')
    if (-not $fm.Success) {
        Write-Output ("FAIL  {0}: no frontmatter" -f $s.Name); $fail = $true; continue
    }
    $name = [regex]::Match($fm.Groups[1].Value, '(?m)^name:\s*(.+)$')
    $desc = [regex]::Match($fm.Groups[1].Value, '(?m)^description:\s*(.+)$')
    if (-not $name.Success -or $name.Groups[1].Value.Trim() -ne $s.Name) {
        Write-Output ("FAIL  {0}: frontmatter name '{1}' != folder" -f $s.Name, $(if ($name.Success) { $name.Groups[1].Value.Trim() } else { '<none>' })); $fail = $true; continue
    }
    if (-not $desc.Success -or $desc.Groups[1].Value.Trim().Length -lt 20) {
        Write-Output ("FAIL  {0}: description missing/short" -f $s.Name); $fail = $true; continue
    }
    Write-Output ("OK    {0}" -f $s.Name)
}

if ($fail) { Write-Output 'RESULT: FAIL'; exit 1 }
Write-Output 'RESULT: OK'
exit 0
