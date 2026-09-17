<#
.SYNOPSIS
  Assembles an interactive HTML documentation site from authored content
  fragments + the baseline's shared chrome (sidebar, search, TOC, theme
  toggle, mermaid, module-graph).

.DESCRIPTION
  Pipeline:
    1. Read <Project>/docs/site-src/site.json  (manifest: site title + pages)
    2. For each page listed in the manifest:
         read <Project>/docs/site-src/<file>   (content fragment = <main> inner HTML)
         wrap it in templates/base.html         (sidebar, nav, TOC, scripts)
         write <Project>/docs/site/<file>       (final static HTML page)
    3. Copy shared assets (site.css, site.js, module-graph.js, mermaid.min.js)
       into <Project>/docs/site/assets/
    4. Validate every output HTML is non-empty and references assets/site.css.
    5. Print a SUMMARY table.

  No Node, no Python, no Docker. Mermaid renders client-side from the vendored
  mermaid.min.js. Output is a pure static site: open index.html in a browser,
  or host the folder on any static server.

.PARAMETER Project
  The consumer project root that contains docs/site-src/. Defaults to the
  current working directory.

.PARAMETER Inputs
  One or more content fragment files to render (paths under site-src/).
  Defaults to every page listed in site.json.

.PARAMETER Clean
  Delete docs/site/ before rebuilding (clean rebuild). Otherwise existing
  site/ is overwritten in place.

.EXAMPLE
  .\Render-Site.ps1 -Project C:\Users\me\Documents\ArkkiAutomation -Clean
  Assembles the site from ArkkiAutomation\docs\site-src\ into ArkkiAutomation\docs\site\.
#>
[CmdletBinding()]
param(
  [string]$Project = (Get-Location).Path,
  [string[]]$Inputs,
  [switch]$Clean
)

$ErrorActionPreference = 'Continue'
$ScriptsDir = $PSScriptRoot
$BaselineRoot = Split-Path $ScriptsDir -Parent
$TemplatesDir = Join-Path $BaselineRoot 'templates'
$AssetsDir = Join-Path $BaselineRoot 'assets'
$BaseHtml = Join-Path $TemplatesDir 'base.html'
$SiteSrc = Join-Path $Project 'docs\site-src'
$SiteOut = Join-Path $Project 'docs\site'
$OutAssets = Join-Path $SiteOut 'assets'

function Write-Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host "    OK  $msg" -ForegroundColor Green }
function Write-Err($msg)  { Write-Host "    ERR $msg" -ForegroundColor Red }

# --- 0. Resolve manifest -----------------------------------------------------
$manifestPath = Join-Path $SiteSrc 'site.json'
if (-not (Test-Path $manifestPath)) {
  Write-Err "Manifest not found: $manifestPath"
  Write-Err "Author docs/site-src/site.json first (see the html-docs-generation skill)."
  exit 1
}
try {
  $manifest = Get-Content $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
} catch {
  Write-Err "Could not parse site.json: $($_.Exception.Message)"
  exit 1
}
if (-not $manifest.title -or -not $manifest.pages) {
  Write-Err "site.json must have 'title' (string) and 'pages' (array of {file,label})."
  exit 1
}

$siteTitle = $manifest.title
$lang = if ($manifest.lang) { $manifest.lang } else { 'en' }

# --- 1. Resolve page list ----------------------------------------------------
$pages = @($manifest.pages)
if ($Inputs -and $Inputs.Count -gt 0) {
  $wanted = $Inputs | ForEach-Object { [System.IO.Path]::GetFileName($_) }
  $pages = $pages | Where-Object { $wanted -contains $_.file }
}
if (-not $pages -or $pages.Count -eq 0) {
  Write-Err "No pages to render."
  exit 1
}

Write-Step "Project:   $Project"
Write-Step "Site title: $siteTitle"
Write-Step "Pages:     $($pages.Count)"

# --- 2. Load base.html template ---------------------------------------------
if (-not (Test-Path $BaseHtml)) { Write-Err "Missing template: $BaseHtml"; exit 1 }
$base = Get-Content $BaseHtml -Raw -Encoding UTF8

# --- 3. Clean if requested ---------------------------------------------------
if ($Clean -and (Test-Path $SiteOut)) {
  Remove-Item $SiteOut -Recurse -Force
  Write-Step "Cleaned existing docs/site/."
}

# --- 4. Ensure output dirs ---------------------------------------------------
New-Item -ItemType Directory -Force -Path $SiteOut | Out-Null
New-Item -ItemType Directory -Force -Path $OutAssets | Out-Null

# --- 5. Build nav (shared across pages) --------------------------------------
function Build-Nav($pages, $activeFile) {
  $sb = [System.Text.StringBuilder]::new()
  foreach ($p in $pages) {
    $cls = if ($p.file -eq $activeFile) { 'nav-link active' } else { 'nav-link' }
    $badge = if ($p.badge) { " <span class=`"nav-badge`">$($p.badge)</span>" } else { '' }
    [void]$sb.Append("<a class=`"$cls`" href=`"$($p.file)`">$($p.label)$badge</a>")
    [void]$sb.Append("`n      ")
  }
  return $sb.ToString().TrimEnd()
}

# --- 6. Render each page -----------------------------------------------------
$summary = @()
foreach ($p in $pages) {
  $file = $p.file
  $label = $p.label
  $srcPath = Join-Path $SiteSrc $file
  $outPath = Join-Path $SiteOut $file

  Write-Step "Rendering: $file ($label)"

  if (-not (Test-Path $srcPath)) {
    Write-Err "Missing content fragment: $srcPath"
    $summary += [pscustomobject]@{ Page=$file; Label=$label; Bytes=0; Status='missing fragment' }
    continue
  }
  $content = Get-Content $srcPath -Raw -Encoding UTF8
  if ([string]::IsNullOrWhiteSpace($content)) {
    Write-Err "Empty content fragment: $srcPath"
    $summary += [pscustomobject]@{ Page=$file; Label=$label; Bytes=0; Status='empty fragment' }
    continue
  }

  # Derive <title> from the first <h1> in the fragment, else the manifest label.
  $pageTitle = $label
  if ($content -match '(?s)<h1[^>]*>(.*?)</h1>') {
    $h1 = $matches[1] -replace '<[^>]+>','' -replace '\s+',' '
    $pageTitle = $h1.Trim()
  }

  $nav = Build-Nav $pages $file
  # Use literal .Replace (not -replace regex) so authored HTML/CSS in content
  # is not re-interpreted as regex patterns.
  $html = $base
  $html = $html.Replace('{{LANG}}', $lang)
  $html = $html.Replace('{{SITE_TITLE}}', [System.Net.WebUtility]::HtmlEncode($siteTitle))
  $html = $html.Replace('{{PAGE_TITLE}}', [System.Net.WebUtility]::HtmlEncode($pageTitle))
  $html = $html.Replace('{{NAV}}', $nav)
  $html = $html.Replace('{{CONTENT}}', $content)

  [System.IO.File]::WriteAllText($outPath, $html, (New-Object System.Text.UTF8Encoding($true)))

  $bytes = (Get-Item $outPath).Length
  if ($bytes -gt 0) { Write-Ok "$file ($([math]::Round($bytes/1KB)) KB)" }
  else { Write-Err "$file is zero-length" }
  $summary += [pscustomobject]@{ Page=$file; Label=$label; Bytes=$bytes; Status=$(if($bytes -gt 0){'ok'}else{'empty'}) }
}

# --- 7. Copy shared assets ---------------------------------------------------
Write-Step "Copying assets..."
$assetFiles = @(
  (Join-Path $TemplatesDir 'site.css'),
  (Join-Path $TemplatesDir 'site.js'),
  (Join-Path $TemplatesDir 'module-graph.js'),
  (Join-Path $AssetsDir 'mermaid.min.js')
)
foreach ($a in $assetFiles) {
  if (-not (Test-Path $a)) { Write-Err "Missing baseline asset: $a"; continue }
  Copy-Item $a $OutAssets -Force
  Write-Ok ("{0} ({1} KB)" -f (Split-Path $a -Leaf), [math]::Round((Get-Item $a).Length/1KB))
}

# --- 8. Verify ---------------------------------------------------------------
Write-Step "Verifying..."
$ok = $true
foreach ($p in $pages) {
  $f = Join-Path $SiteOut $p.file
  if (-not (Test-Path $f)) { Write-Err "Output missing: $f"; $ok = $false; continue }
  $len = (Get-Item $f).Length
  if ($len -eq 0) { Write-Err "Output empty: $f"; $ok = $false; continue }
  $txt = Get-Content $f -Raw -Encoding UTF8
  if ($txt -notmatch 'assets/site\.css') { Write-Err "$($p.file) does not reference assets/site.css"; $ok = $false }
  if ($txt -notmatch 'assets/site\.js')  { Write-Err "$($p.file) does not reference assets/site.js"; $ok = $false }
}
foreach ($asset in @('site.css','site.js','module-graph.js','mermaid.min.js')) {
  $a = Join-Path $OutAssets $asset
  if (-not (Test-Path $a) -or (Get-Item $a).Length -eq 0) { Write-Err "Asset missing/empty: $asset"; $ok = $false }
}

# --- 9. Summary --------------------------------------------------------------
Write-Host ""
Write-Step "SUMMARY"
$summary | Format-Table -AutoSize

if (-not $ok -or ($summary | Where-Object { $_.Bytes -eq 0 })) {
  Write-Err "Site render had failures."
  exit 1
}
Write-Ok "Site assembled successfully."
Write-Host ""
Write-Host "    Open in a browser:" -ForegroundColor White
Write-Host "    $SiteOut\index.html" -ForegroundColor White
Write-Host ""
Write-Host "    Or serve locally (optional):" -ForegroundColor White
Write-Host "    python -m http.server 8000 --directory `"$SiteOut`"" -ForegroundColor DarkGray
exit 0
