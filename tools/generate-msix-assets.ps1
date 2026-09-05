<#
.SYNOPSIS
    Generates the MSIX tile/logo asset set (Phase 10 / ADR-0010) from the existing app.ico.

.DESCRIPTION
    PLACEHOLDER ASSETS - generated from src/PlaySphere.App/Assets/app.ico by simple resampling.
    Replace with real designed artwork before any public release; this script exists only to
    unblock MSIX packaging without waiting on a graphic-design pass (see ADR-0010).

    Requires PowerShell on Windows (uses System.Drawing, part of the .NET runtime already
    required to build this repo).
#>
[CmdletBinding()]
param(
    [string]$IconPath,
    [string]$OutputDir
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$scriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
if (-not $IconPath) { $IconPath = Join-Path $scriptRoot "..\src\PlaySphere.App\Assets\app.ico" }
if (-not $OutputDir) { $OutputDir = Join-Path $scriptRoot "..\src\PlaySphere.App\Assets" }

# System.Drawing.Icon's legacy GDI+ parser throws on the PNG-compressed large frames modern .ico
# files embed (this app.ico has 16x16/24x24+ PNG-format entries per its file signature), so parse
# the ICO directory ourselves and pull out the largest frame's raw image bytes directly.
function Get-LargestIconFrameBytes {
    param([string]$Path)
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $count = [BitConverter]::ToUInt16($bytes, 4)
    $best = $null
    for ($i = 0; $i -lt $count; $i++) {
        $entryOffset = 6 + ($i * 16)
        $w = $bytes[$entryOffset]; if ($w -eq 0) { $w = 256 }
        $h = $bytes[$entryOffset + 1]; if ($h -eq 0) { $h = 256 }
        $size = [long]$bytes[$entryOffset + 8] -bor ([long]$bytes[$entryOffset + 9] -shl 8) -bor ([long]$bytes[$entryOffset + 10] -shl 16) -bor ([long]$bytes[$entryOffset + 11] -shl 24)
        $offset = [long]$bytes[$entryOffset + 12] -bor ([long]$bytes[$entryOffset + 13] -shl 8) -bor ([long]$bytes[$entryOffset + 14] -shl 16) -bor ([long]$bytes[$entryOffset + 15] -shl 24)
        $area = $w * $h
        if ($best -eq $null -or $area -gt $best.Area) {
            $best = [PSCustomObject]@{ Area = $area; Size = $size; Offset = $offset }
        }
    }
    if ($best -eq $null) { throw "No icon frames found in $Path" }
    $frameBytes = New-Object byte[] $best.Size
    [Array]::Copy($bytes, $best.Offset, $frameBytes, 0, $best.Size)
    return $frameBytes
}

if (-not (Test-Path $IconPath)) {
    throw "Icon not found: $IconPath"
}
$IconPath = (Resolve-Path $IconPath).Path
$OutputDir = (Resolve-Path $OutputDir).Path

$frameBytes = Get-LargestIconFrameBytes -Path $IconPath
$frameStream = New-Object System.IO.MemoryStream(,$frameBytes)
$sourceBitmap = [System.Drawing.Bitmap]::FromStream($frameStream)
Write-Output "Extracted largest icon frame: $($sourceBitmap.Width) x $($sourceBitmap.Height)"

function New-ResizedPng {
    param(
        [string]$FileName,
        [int]$Width,
        [int]$Height,
        [int]$PaddingPercent = 0
    )
    $bitmap = New-Object System.Drawing.Bitmap($Width, $Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.Clear([System.Drawing.Color]::Transparent)

        $inset = [int]([Math]::Min($Width, $Height) * $PaddingPercent / 100.0)
        $destWidth = $Width - (2 * $inset)
        $destHeight = $Height - (2 * $inset)
        $graphics.DrawImage($sourceBitmap, $inset, $inset, $destWidth, $destHeight)
    }
    finally {
        $graphics.Dispose()
    }

    $outPath = Join-Path $OutputDir $FileName
    $bitmap.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    Write-Output "Wrote $outPath ($Width x $Height)"
}

# Required MSIX tile/logo set (uap:VisualElements + tile target-size variants).
New-ResizedPng -FileName "Square44x44Logo.png"                    -Width 44  -Height 44
New-ResizedPng -FileName "Square44x44Logo.targetsize-16.png"      -Width 16  -Height 16
New-ResizedPng -FileName "Square44x44Logo.targetsize-24.png"      -Width 24  -Height 24
New-ResizedPng -FileName "Square44x44Logo.targetsize-32.png"      -Width 32  -Height 32
New-ResizedPng -FileName "Square44x44Logo.targetsize-48.png"      -Width 48  -Height 48
New-ResizedPng -FileName "Square44x44Logo.targetsize-256.png"     -Width 256 -Height 256
New-ResizedPng -FileName "Square71x71Logo.png"                    -Width 71  -Height 71
New-ResizedPng -FileName "Square150x150Logo.png"                  -Width 150 -Height 150
New-ResizedPng -FileName "Square310x310Logo.png"                  -Width 310 -Height 310
New-ResizedPng -FileName "Wide310x150Logo.png"                    -Width 310 -Height 150 -PaddingPercent 15
New-ResizedPng -FileName "StoreLogo.png"                          -Width 50  -Height 50
New-ResizedPng -FileName "SplashScreen.png"                       -Width 620 -Height 300 -PaddingPercent 20

$sourceBitmap.Dispose()
$frameStream.Dispose()

Write-Output ""
Write-Output "Done. These are PLACEHOLDER assets resampled from app.ico - replace with real"
Write-Output "designed artwork before any public release (see ADR-0010)."
