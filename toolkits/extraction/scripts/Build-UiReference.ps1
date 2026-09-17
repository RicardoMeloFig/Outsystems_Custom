<#
.SYNOPSIS
    Builds the OutSystems UI framework reference (patterns.md + classic theme CSS)
    from the local OutSystems ground-of-truth repo.

.DESCRIPTION
    Reads the `outsystems-ui` repository inside the ground-of-truth repo
    (default path from references\source-path.txt, or pass -SourceRepo) and
    generates, under references\outsystems-ui\:

      - patterns.md            pattern catalog: name, category, provider,
                               public JS API, CSS classes, CSS custom properties
      - classic-theme-o11.css  verbatim copy of the O11 classic theme CSS

    Output is COMMITTED to git (do not edit by hand - re-run this script).

.PARAMETER SourceRepo
    Absolute path to the ground-of-truth repo (the folder that contains
    `outsystems-ui`). Defaults to the path stored in references\source-path.txt.

.EXAMPLE
    .\scripts\Build-UiReference.ps1
    .\scripts\Build-UiReference.ps1 -SourceRepo "C:\Users\Ricardo Figueiredo\Documents\Outsystems_Custom\references\repos"

.NOTES
    Exit codes: 0 = generated; 1 = source repo not found / misconfigured.
#>
param(
    [string]$SourceRepo
)

$ErrorActionPreference = "Stop"
$root    = Split-Path -Parent $PSScriptRoot
$srcFile = Join-Path $root "references\source-path.txt"

# --- Resolve the source repo path -------------------------------------------
if (-not $SourceRepo) {
    if (Test-Path -LiteralPath $srcFile) {
        $SourceRepo = (Get-Content -LiteralPath $srcFile -Raw).Trim()
    }
}
if (-not $SourceRepo) {
    Write-Host @"
Build-UiReference: no source repo configured.
Create references\source-path.txt containing one line - the absolute path to the
Outsystems REPO folder - or call:
    .\scripts\Build-UiReference.ps1 -SourceRepo "C:\path\to\Outsystems REPO"
"@ -ForegroundColor Yellow
    exit 1
}
if (-not (Test-Path -LiteralPath $SourceRepo)) {
    Write-Host "Build-UiReference: source repo not found: $SourceRepo" -ForegroundColor Red
    exit 1
}

$uiRepo = Join-Path $SourceRepo "outsystems-ui"
$cssClassic = Join-Path $uiRepo "classic-theme\O11.OutSystemsUI.css"
$apiDir     = Join-Path $uiRepo "src\scripts\OutSystems\OSUI\Patterns"
$scssDir    = Join-Path $uiRepo "src\scss"

foreach ($required in @($cssClassic, $apiDir, $scssDir)) {
    if (-not (Test-Path -LiteralPath $required)) {
        Write-Host "Build-UiReference: expected path missing in source repo: $required" -ForegroundColor Red
        Write-Host "(Is '$SourceRepo' the ground-of-truth repo containing 'outsystems-ui'?)" -ForegroundColor Red
        exit 1
    }
}

$outDir = Join-Path $root "references\outsystems-ui"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# Persist the resolved path back (normalized) for skills to read.
Set-Content -LiteralPath $srcFile -Value $SourceRepo -Encoding UTF8

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

# --- 1. Classic theme CSS (verbatim copy) -----------------------------------
Copy-Item -LiteralPath $cssClassic -Destination (Join-Path $outDir "classic-theme-o11.css") -Force
Write-Host "Build-UiReference: copied classic-theme-o11.css ($([math]::Round((Get-Item $cssClassic).Length/1KB)) KB)"

# --- 2. Helpers ---------------------------------------------------------------
function ConvertTo-Kebab([string]$s) {
    return ([regex]::Replace($s, '(?<!^)(?=[A-Z])', '-')).ToLowerInvariant()
}

function Get-ScssFacts([string[]]$files) {
    # Returns a hashtable: Classes (sorted string[]), Vars (sorted string[])
    $classes = New-Object System.Collections.Generic.HashSet[string]
    $vars    = New-Object System.Collections.Generic.HashSet[string]
    $noise   = @('scss', 'css')  # tokens that only appear inside comments
    foreach ($f in $files) {
        if (-not (Test-Path -LiteralPath $f)) { continue }
        $content = Get-Content -LiteralPath $f -Raw
        foreach ($m in [regex]::Matches($content, '\.([a-zA-Z][a-zA-Z0-9_-]*)')) {
            if ($noise -notcontains $m.Groups[1].Value) {
                [void]$classes.Add($m.Groups[1].Value)
            }
        }
        foreach ($m in [regex]::Matches($content, '(--[a-zA-Z][a-zA-Z0-9_-]*)\s*:')) {
            [void]$vars.Add($m.Groups[1].Value)
        }
    }
    return @{
        Classes = @($classes | Sort-Object)
        Vars    = @($vars | Sort-Object)
    }
}

# Provider backing libraries (from outsystems-ui ARCHITECTURE.md)
$providerByFolder = @{
    "carousel"     = "Splide 4.1.3"
    "date-picker"  = "Flatpickr 4.6.13"
    "month-picker" = "Flatpickr 4.6.13"
    "time-picker"  = "Flatpickr 4.6.13"
    "dropdown"     = "VirtualSelect 1.4.0"
    "range-slider" = "noUiSlider 15.8.1"
}

$patternCategories = @("01-adaptive", "02-content", "03-interaction", "04-navigation", "05-numbers", "06-utilities")

# Map: scss folder name -> @{ Category; Dir }
$scssFolders = @{}
foreach ($cat in $patternCategories) {
    $catDir = Join-Path $scssDir "04-patterns\$cat"
    if (-not (Test-Path -LiteralPath $catDir)) { continue }
    foreach ($d in (Get-ChildItem -LiteralPath $catDir -Directory)) {
        $scssFolders[$d.Name] = @{ Category = $cat; Dir = $d.FullName }
    }
}

# Map: scss flat file (css-only pattern candidates) name -> @{ Category; File }
$scssFlat = @{}
foreach ($cat in $patternCategories) {
    $catDir = Join-Path $scssDir "04-patterns\$cat"
    if (-not (Test-Path -LiteralPath $catDir)) { continue }
    foreach ($f in (Get-ChildItem -LiteralPath $catDir -File -Filter "*.scss")) {
        $name = $f.BaseName -replace '^_', ''
        $scssFlat[$name] = @{ Category = $cat; File = $f.FullName }
    }
}

# --- 3. JS-driven patterns (from *API.ts) ------------------------------------
$jsPatterns = @()
foreach ($apiFile in (Get-ChildItem -LiteralPath $apiDir -File -Filter "*API.ts" | Sort-Object Name)) {
    $name = $apiFile.BaseName -replace 'API$', ''
    $content = Get-Content -LiteralPath $apiFile.FullName -Raw
    $functions = @([regex]::Matches($content, 'export function (\w+)') |
        ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    $kebab = ConvertTo-Kebab $name

    $category = "-"
    $provider = ""
    $scssNote = ""
    $facts = $null

    if ($scssFolders.ContainsKey($kebab)) {
        $info = $scssFolders[$kebab]
        $category = $info.Category
        $scssFiles = @(Get-ChildItem -LiteralPath $info.Dir -File -Filter "*.scss" -Recurse |
            Where-Object { $_.DirectoryName -notmatch '\\provider$' } |
            Select-Object -ExpandProperty FullName)
        $facts = Get-ScssFacts $scssFiles
        if ($providerByFolder.ContainsKey($kebab)) { $provider = $providerByFolder[$kebab] }
        elseif (Test-Path -LiteralPath (Join-Path $info.Dir "provider")) { $provider = "(see provider folder)" }
    } else {
        # Sub-patterns (e.g. TabsContentItem) attribute to their parent folder when prefixed
        $parent = $null
        foreach ($key in $scssFolders.Keys) {
            if ($kebab -like "$key-*") {
                if ($null -eq $parent -or $key.Length -gt $parent.Length) { $parent = $key }
            }
        }
        if ($parent) {
            $category = $scssFolders[$parent].Category
            $scssNote = "CSS shared with the $parent pattern (no dedicated SCSS)"
        } else {
            $scssNote = "no dedicated SCSS (logic/behavior only)"
        }
    }

    $jsPatterns += [pscustomobject]@{
        Name     = $name
        Kebab    = $kebab
        Category = $category
        Provider = $provider
        Facts    = $facts
        ScssNote = $scssNote
        Api      = $functions
    }
}

# --- 4. CSS-only patterns (scss without a JS API) -----------------------------
$jsKebabs = @{}
foreach ($p in $jsPatterns) { $jsKebabs[$p.Kebab] = $true }

$cssOnly = @()
foreach ($name in ($scssFlat.Keys | Sort-Object)) {
    $info = $scssFlat[$name]
    $facts = Get-ScssFacts @($info.File)
    $cssOnly += [pscustomobject]@{
        Name     = $name
        Category = $info.Category
        Facts    = $facts
    }
}
foreach ($folderName in ($scssFolders.Keys | Sort-Object)) {
    if (-not $jsKebabs.ContainsKey($folderName)) {
        $info = $scssFolders[$folderName]
        $scssFiles = @(Get-ChildItem -LiteralPath $info.Dir -File -Filter "*.scss" -Recurse |
            Select-Object -ExpandProperty FullName)
        $cssOnly += [pscustomobject]@{
            Name     = $folderName
            Category = $info.Category
            Facts    = (Get-ScssFacts $scssFiles)
        }
    }
}
$cssOnly = @($cssOnly | Sort-Object Name)

# --- 5. Index of the remaining SCSS groups ------------------------------------
$otherGroups = @("00-abstract", "01-foundations", "02-layout", "03-widgets", "05-useful",
                 "06-screen-transitions", "07-keyframes", "08-servicestudio-preview", "09-excluders")
$otherIndex = @()
foreach ($g in $otherGroups) {
    $gDir = Join-Path $scssDir $g
    if (-not (Test-Path -LiteralPath $gDir)) { continue }
    $files = @(Get-ChildItem -LiteralPath $gDir -File -Filter "*.scss" | Sort-Object Name |
        ForEach-Object { ($_.BaseName -replace '^_', '') })
    $otherIndex += [pscustomobject]@{ Group = $g; Files = $files }
}

# --- 6. Emit patterns.md -------------------------------------------------------
$lines = New-Object System.Collections.Generic.List[string]
$genDate = (Get-Date).ToString("yyyy-MM-dd")

$lines.Add("# OutSystems UI Pattern Reference (O11)")
$lines.Add("")
$lines.Add("> Generated $genDate by ``scripts/Build-UiReference.ps1`` from the ground-of-truth repo's")
$lines.Add("> ``outsystems-ui`` source (src/scss + src/scripts/OutSystems/OSUI/Patterns). DO NOT EDIT BY HAND.")
$lines.Add("> Source repo: ``$SourceRepo``")
$lines.Add("")
$lines.Add("Use this catalog to identify OutSystems UI patterns in extracted modules: web block names,")
$lines.Add("CSS classes (``osui-*``), and CSS custom properties (``--osui-*``) map to these patterns.")
$lines.Add("The base stylesheet every O11 Reactive theme extends is ``classic-theme-o11.css`` (same folder).")
$lines.Add("")
$lines.Add("| | Count |")
$lines.Add("|---|---|")
$lines.Add("| JS-driven patterns (public JS API) | $($jsPatterns.Count) |")
$lines.Add("| CSS-only patterns | $($cssOnly.Count) |")
$lines.Add("")
$lines.Add("---")
$lines.Add("")
function Format-CodeList([string[]]$items, [string]$prefix) {
    $parts = @($items | ForEach-Object { '`' + $prefix + $_ + '`' })
    return ($parts -join ', ')
}

$lines.Add("## JS-driven patterns")
$lines.Add("")
foreach ($p in $jsPatterns) {
    $providerSuffix = ''
    if ($p.Provider) { $providerSuffix = ' | Provider: ' + $p.Provider }
    $lines.Add("### $($p.Name)")
    $lines.Add("")
    $lines.Add("- CSS class root: ``.osui-$($p.Kebab)`` | Category: $($p.Category)$providerSuffix")
    if ($p.Facts) {
        $lines.Add("- Classes: " + (Format-CodeList $p.Facts.Classes '.'))
        if ($p.Facts.Vars.Count -gt 0) {
            $lines.Add("- CSS variables (CSS API): " + (Format-CodeList $p.Facts.Vars ''))
        }
    } elseif ($p.ScssNote) {
        $lines.Add("- SCSS: $($p.ScssNote)")
    }
    $lines.Add("- JS API (``OutSystems.OSUI.Patterns.$($p.Name)API``): $($p.Api -join ', ')")
    $lines.Add("")
}
$lines.Add("---")
$lines.Add("")
$lines.Add("## CSS-only patterns")
$lines.Add("")
foreach ($p in $cssOnly) {
    $lines.Add("### $($p.Name)")
    $lines.Add("")
    $lines.Add("- Category: $($p.Category)")
    if ($p.Facts -and $p.Facts.Classes.Count -gt 0) {
        $lines.Add("- Classes: " + (Format-CodeList $p.Facts.Classes '.'))
    }
    if ($p.Facts -and $p.Facts.Vars.Count -gt 0) {
        $lines.Add("- CSS variables (CSS API): " + (Format-CodeList $p.Facts.Vars ''))
    }
    $lines.Add("")
}
$lines.Add("---")
$lines.Add("")
$lines.Add("## Other SCSS groups (foundations, layout, widgets, utilities)")
$lines.Add("")
foreach ($g in $otherIndex) {
    $lines.Add("- **$($g.Group):** $($g.Files -join ', ')")
}
$lines.Add("")

[System.IO.File]::WriteAllLines((Join-Path $outDir "patterns.md"), $lines, $utf8NoBom)

Write-Host "Build-UiReference: generated patterns.md ($($jsPatterns.Count) JS-driven, $($cssOnly.Count) CSS-only patterns)"
Write-Host "Build-UiReference: OK - output in $outDir"
exit 0
