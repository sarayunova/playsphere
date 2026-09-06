<#
.SYNOPSIS
    Generates the MSIX tile/logo asset set (Phase 10 / ADR-0010) from a source icon image.

.DESCRIPTION
    Resamples a single square master image (PNG/JPEG/BMP, or a legacy .ico) down into the full
    MSIX tile/logo set. Defaults to src/PlaySphere.App/Assets/icon-source.png - the real designed
    artwork that replaced this phase's original app.ico-derived placeholder set (see ADR-0010's
    "Consequences" - this was one of its explicit open follow-ups).

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
if (-not $IconPath) { $IconPath = Join-Path $scriptRoot "..\src\PlaySphere.App\Assets\icon-source.png" }
if (-not $OutputDir) { $OutputDir = Join-Path $scriptRoot "..\src\PlaySphere.App\Assets" }

# System.Drawing.Icon's legacy GDI+ parser throws on the PNG-compressed large frames modern .ico
# files embed, so .ico sources are parsed by hand to pull out the largest frame's raw image bytes
# directly. Any other raster format (PNG/JPEG/BMP) loads directly via Bitmap.FromFile - GDI+
# detects the real format from the file's contents, not its extension.
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

if ([System.IO.Path]::GetExtension($IconPath) -eq '.ico') {
    $frameBytes = Get-LargestIconFrameBytes -Path $IconPath
    $frameStream = New-Object System.IO.MemoryStream(,$frameBytes)
    $sourceBitmap = [System.Drawing.Bitmap]::FromStream($frameStream)
}
else {
    $sourceBitmap = [System.Drawing.Bitmap]::FromFile($IconPath)
    $frameStream = $null
}
Write-Output "Loaded source image: $($sourceBitmap.Width) x $($sourceBitmap.Height)"

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

        # Fit the (square) source into the largest centered square that fits the padded canvas,
        # rather than stretching it to fill Width x Height directly - for non-square canvases
        # (Wide310x150Logo, SplashScreen) that would squash a square source into an oval/distorted
        # shape. The canvas stays transparent outside that square so the tile's manifest-declared
        # BackgroundColor shows through, matching how Windows tile logos are meant to work.
        $inset = [int]([Math]::Min($Width, $Height) * $PaddingPercent / 100.0)
        $availableWidth = $Width - (2 * $inset)
        $availableHeight = $Height - (2 * $inset)
        $destSize = [Math]::Min($availableWidth, $availableHeight)
        $destX = $inset + [int](($availableWidth - $destSize) / 2)
        $destY = $inset + [int](($availableHeight - $destSize) / 2)
        $graphics.DrawImage($sourceBitmap, $destX, $destY, $destSize, $destSize)
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
if ($frameStream) { $frameStream.Dispose() }

Write-Output ""
Write-Output "Done. Resampled from: $IconPath"
