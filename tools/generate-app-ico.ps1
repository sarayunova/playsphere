<#
.SYNOPSIS
    Builds src/PlaySphere.App/Assets/app.ico (the Win32 exe/title-bar icon) from the MSIX tile
    PNGs that tools/generate-msix-assets.ps1 already produced.

.DESCRIPTION
    .NET has no built-in "save a multi-resolution .ico" API (System.Drawing.Icon only round-trips
    a single frame), so this constructs the ICO container by hand: a standard ICONDIR header plus
    one ICONDIRENTRY and one PNG-compressed image block per size. Modern Windows (Vista+) reads
    PNG-compressed frames at any declared size, which is what lets this reuse the already-resized
    PNGs directly instead of re-deriving anything.

    Run tools/generate-msix-assets.ps1 first - this depends on its Square44x44Logo.targetsize-*.png
    output.
#>
[CmdletBinding()]
param(
    [string]$AssetsDir,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

$scriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
if (-not $AssetsDir) { $AssetsDir = Join-Path $scriptRoot '..\src\PlaySphere.App\Assets' }
if (-not $OutputPath) { $OutputPath = Join-Path $AssetsDir 'app.ico' }
$AssetsDir = (Resolve-Path $AssetsDir).Path

# (declared size, source PNG). 256 is declared as 0 per the ICO spec (a byte can't hold 256).
$frames = @(
    @{ Size = 16;  File = 'Square44x44Logo.targetsize-16.png' }
    @{ Size = 32;  File = 'Square44x44Logo.targetsize-32.png' }
    @{ Size = 48;  File = 'Square44x44Logo.targetsize-48.png' }
    @{ Size = 256; File = 'Square44x44Logo.targetsize-256.png' }
)

$frameData = foreach ($f in $frames) {
    $path = Join-Path $AssetsDir $f.File
    if (-not (Test-Path $path)) { throw "Missing $path - run tools/generate-msix-assets.ps1 first." }
    [PSCustomObject]@{ Size = $f.Size; Bytes = [System.IO.File]::ReadAllBytes($path) }
}

$stream = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($stream)
try {
    # ICONDIR: reserved(2)=0, type(2)=1, count(2)
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$frameData.Count)

    $headerSize = 6 + (16 * $frameData.Count)
    $offset = $headerSize
    foreach ($f in $frameData) {
        $declaredSize = if ($f.Size -ge 256) { 0 } else { $f.Size }
        $writer.Write([byte]$declaredSize)   # width
        $writer.Write([byte]$declaredSize)   # height
        $writer.Write([byte]0)               # color count (0 = not a palette image)
        $writer.Write([byte]0)               # reserved
        $writer.Write([UInt16]1)             # color planes
        $writer.Write([UInt16]32)            # bits per pixel
        $writer.Write([UInt32]$f.Bytes.Length)
        $writer.Write([UInt32]$offset)
        $offset += $f.Bytes.Length
    }
    foreach ($f in $frameData) {
        $writer.Write($f.Bytes)
    }

    $writer.Flush()
    [System.IO.File]::WriteAllBytes($OutputPath, $stream.ToArray())
}
finally {
    $writer.Dispose()
    $stream.Dispose()
}

Write-Output "Wrote $OutputPath ($($frameData.Count) frames: $(($frameData | ForEach-Object { $_.Size }) -join ', '))"
