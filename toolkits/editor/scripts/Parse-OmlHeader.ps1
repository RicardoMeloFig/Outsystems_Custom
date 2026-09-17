<#
.SYNOPSIS
    Parses an OutSystems .oml file header offline (no DLLs, no running SS).

.DESCRIPTION
    The .oml starts with magic "OML" + a short binary header, then pipe-delimited
    plaintext fields (terminated by the body marker). This script reads the first
    few KB, strips non-printables, splits on "|", and prints the key fields:
    Name, eSpaceKey, IsExtension, SS/Platform versions, Build, Description, SavedAt.

    Useful to identify a .oml's module without loading it.

.PARAMETER Path
    Path to a .oml file.

.EXAMPLE
    .\scripts\Parse-OmlHeader.ps1 .\OMLs\Diet_BL.oml
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Path
)

$ErrorActionPreference = "Stop"
if (-not (Test-Path -LiteralPath $Path)) { Write-Error "Not found: $Path"; exit 1 }

$bytes = [System.IO.File]::ReadAllBytes($Path)
$len = [Math]::Min(8192, $bytes.Length)
$raw = [System.Text.Encoding]::ASCII.GetString($bytes, 0, $len)
# Strip non-printable bytes so the binary header doesn't break the split.
$clean = ($raw -replace '[^\x20-\x7E]', '')
if (-not $clean.StartsWith('OML')) { Write-Error "Not an OML file (no OML magic): $Path"; exit 1 }

$parts = $clean -split '\|'
# parts[0] = "OML" + binary-remnants + "<ssVersion>"; the rest are clean fields.
$ssVer = ''
if ($parts[0] -match '(\d+\.\d+\.\d+\.\d+)') { $ssVer = $Matches[1] }

# Field layout (observed on SS 11.55.81):
#  [0] OML..ssVersion | [1] platformVersion | [2] build | [3] eSpaceKey |
#  [4] key/hash | [5] IsExtension | [6] Name | [7] Description | [8] SavedAt | ...
$o = [ordered]@{
    Path           = $Path
    Name           = if ($parts.Length -gt 6) { $parts[6] } else { '' }
    ESpaceKey      = if ($parts.Length -gt 3) { $parts[3] } else { '' }
    IsExtension    = if ($parts.Length -gt 5) { $parts[5] } else { '' }
    SSVersion      = $ssVer
    PlatformVersion= if ($parts.Length -gt 1) { $parts[1] } else { '' }
    Build          = if ($parts.Length -gt 2) { $parts[2] } else { '' }
    Description    = if ($parts.Length -gt 7) { $parts[7] } else { '' }
    SavedAt        = if ($parts.Length -gt 8) { $parts[8] } else { '' }
}
[pscustomobject]$o | Format-List
