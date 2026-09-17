<#
.SYNOPSIS
  Pure-PowerShell .oml header parser (no OutSystems DLLs needed).

  Verified against the SS11 .oml container format (all 13 sample files):
    offset 0..2 : ASCII magic "OML" (4F 4D 4C)
    offset 3..6 : 4-byte field (per-file; byte[3] varies, bytes 5-6 are 0x00).
                  Includes a length hint but is NOT a reliable header length
                  when the description/timestamp fields vary, so we ignore it.
    offset 7..  : pipe-delimited header text (10 fields), terminated by the
                  2-byte body marker "wg" (0x77 0x67) immediately followed by
                  the first fragment path "|/..."  -> marker bytes = "wg|/"
        [0] SS build version        (e.g. 11.0.415.100)
        [1] Platform server version (e.g. 11.55.78.64991)
        [2] build number
        [3] eSpaceKey (base64url, 22 chars)  <-- pass to SC Download()
        [4] signature hash (base64url, 22 chars)
        [5] IsExtension (True/False)
        [6] module name
        [7] description (may be empty)
        [8] last-saved timestamp (yyyy-MM-dd HH:mm:ss)
        [9] product hash (fixed: J7pB0r6rRF5GhEoPIwOs on this build)
    after marker : body = "omi" serialization: <opcode>|/<Collection>.<b64key>|<base64-json> ...

.PARAMETER Path
  Path to a .oml file, or a directory containing .oml files.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File .\Parse-OmlHeader.ps1 -Path "C:\Users\ricar\Downloads\TestAIModules"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Path
)

$targets = if (Test-Path -LiteralPath $Path -PathType Container) {
    Get-ChildItem -LiteralPath $Path -Filter *.oml -Recurse
} else {
    Get-Item -LiteralPath $Path
}

$marker = [byte[]](0x77, 0x67, 0x7C, 0x2F) # "wg|/"

foreach ($f in $targets) {
    $b = [System.IO.File]::ReadAllBytes($f.FullName)
    if ($b.Length -lt 11) { Write-Warning "$($f.Name): too small"; continue }
    $magic = [char]$b[0] + [char]$b[1] + [char]$b[2]
    if ($magic -ne 'OML') { Write-Warning "$($f.Name): not an OML container (magic=$magic)"; continue }

    # find the body marker "wg|/" starting at offset 7
    $idx = -1
    for ($i = 7; $i -lt $b.Length - 4; $i++) {
        if ($b[$i] -eq $marker[0] -and $b[$i+1] -eq $marker[1] -and $b[$i+2] -eq $marker[2] -and $b[$i+3] -eq $marker[3]) { $idx = $i; break }
    }
    if ($idx -lt 0) { Write-Warning "$($f.Name): body marker 'wg|/' not found"; continue }

    $hdrText = -join ($b[7..($idx - 1)] | ForEach-Object { [char]$_ })
    $fields = $hdrText -split '\|'

    [pscustomobject]@{
        File        = $f.Name
        SSVersion   = $fields[0]
        PlatVersion = $fields[1]
        Build       = $fields[2]
        ESpaceKey   = $fields[3]   # pass this to Service Center Download()
        SigHash     = $fields[4]
        IsExtension = $fields[5]
        ModuleName  = $fields[6]
        Description = $fields[7]
        SavedAt     = $fields[8]
        ProductHash = $fields[9]
        BodyOffset  = $idx
        SizeKB      = [math]::Round($f.Length / 1KB)
    }
}
