# Validate native (non-managed) dependencies expected by the modules.
#
# - libVLC (PlayPhase 1): must ship native libvlc.dll / libvlccore.dll + plugins with the app.
# - FFmpeg/FFprobe (Phase 7): dev-only tool in tools/ffmpeg for fixture generation.

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Write-Host '== PlaySphere native dependency validation ==' -ForegroundColor Cyan

$repo = Split-Path -Parent $PSScriptRoot

# ---- libVLC runtime ---------------------------------------------------------
$libVlcFiles = Get-ChildItem -Path (Join-Path $repo 'src\PlaySphere.App\bin') -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -in @('libvlc.dll', 'libvlccore.dll') }

if ($libVlcFiles.Count -ge 2) {
    $sample = $libVlcFiles | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    Write-Host "  libVLC ......... OK ($($libVlcFiles.Count) copies, sample: $($sample.FullName.Replace($repo + '\', '')))"
    Write-Host "  libVLC plugins . checking..."
    $pluginDir = Join-Path $sample.DirectoryName 'plugins'
    $pluginCount = if (Test-Path $pluginDir) { @(Get-ChildItem $pluginDir -Recurse -File -ErrorAction SilentlyContinue).Count } else { 0 }
    Write-Host "  libVLC plugins . $pluginCount files"
    if ($pluginCount -eq 0) {
        Write-Warning 'No libVLC plugins found next to the app output. Video decode may fail.'
    }
}
else {
    Write-Warning 'libVLC native files not found in app output. Did you build PlaySphere.App (Release)?'
}

# ---- FFmpeg dev tool --------------------------------------------------------
$ffmpeg = Join-Path $PSScriptRoot 'ffmpeg\ffmpeg.exe'
$ffprobe = Join-Path $PSScriptRoot 'ffmpeg\ffprobe.exe'
if ((Test-Path $ffmpeg) -and (Test-Path $ffprobe)) {
    Write-Host '  FFmpeg/FFprobe. OK (dev-only, tools/ffmpeg)'
}
else {
    Write-Host '  FFmpeg/FFprobe . dev-only tool; run tools\generate-media-fixtures.ps1 to fetch.'
}

Write-Host 'Native dependency validation complete.' -ForegroundColor Green