# Generate the legally-usable media test corpus (PICKLE.md §55).
#
# Fixtures are synthesized with FFmpeg (testsrc2 video, sine audio) so no copyrighted
# material is ever committed. FFmpeg is a DEV-ONLY tool: it is downloaded once into
# tools/ffmpeg/ and is never packaged with the app.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File tools/generate-media-fixtures.ps1 [-Force]

[CmdletBinding()]
param(
    [int]$Seconds = 6,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$ffDir = Join-Path $PSScriptRoot 'ffmpeg'
$ffmpeg = Join-Path $ffDir 'ffmpeg.exe'
$ffprobe = Join-Path $ffDir 'ffprobe.exe'
$out = Join-Path $repo 'samples\legal-media'

New-Item -ItemType Directory -Path $ffDir -Force | Out-Null
New-Item -ItemType Directory -Path $out -Force | Out-Null

# ---- 1. Bootstrap FFmpeg (one-time download) -----------------------------------
if (-not (Test-Path $ffmpeg)) {
    Write-Host 'ffmpeg not found - downloading static build (one-time, ~100 MB)...' -ForegroundColor Yellow
    $zip = Join-Path $env:TEMP 'ffmpeg-release-essentials.zip'
    $extract = Join-Path $env:TEMP 'ffmpeg-extract'
    & curl.exe -sSL -o $zip 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip'
    if ($LASTEXITCODE -ne 0) { throw "curl download of FFmpeg failed (exit $LASTEXITCODE)." }
    if (Test-Path $extract) { Remove-Item -Recurse -Force $extract }
    Expand-Archive -Path $zip -DestinationPath $extract -Force
    $ffmpegExe = Get-ChildItem $extract -Recurse -Filter 'ffmpeg.exe' | Select-Object -First 1
    if (-not $ffmpegExe) { throw 'ffmpeg.exe not found inside downloaded archive.' }
    Copy-Item $ffmpegExe.FullName $ffmpeg
    Copy-Item (Join-Path $ffmpegExe.DirectoryName 'ffprobe.exe') $ffprobe
    Remove-Item -Recurse -Force $extract
    Remove-Item -Force $zip
    Write-Host 'ffmpeg ready in tools/ffmpeg.' -ForegroundColor Green
}

Write-Host '== Generating legal media fixtures ==' -ForegroundColor Cyan

# ---- 2. Fixture builders ----------------------------------------------------------
function Invoke-Ffmpeg {
    param([string[]]$Arguments)
    & $ffmpeg @Arguments 2>$null
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed (exit $LASTEXITCODE): $($Arguments -join ' ')" }
}

function Invoke-Ffprobe {
    param([string]$FilePath)
    $out = & $ffprobe -v error -show_streams -show_format -of json $FilePath 2>$null
    if ($LASTEXITCODE -ne 0) { throw "ffprobe failed on $FilePath" }
    return ($out | Out-String | ConvertFrom-Json)
}

# Audio-only fixtures
Invoke-Ffmpeg @('-y','-f','lavfi','-i',"sine=frequency=997:duration=$Seconds",'-c:a','libmp3lame','-b:a','128k',(Join-Path $out 'sine.mp3'))
Invoke-Ffmpeg @('-y','-f','lavfi','-i',"sine=frequency=523:duration=$Seconds",'-c:a','pcm_s16le',(Join-Path $out 'tone.wav'))
Invoke-Ffmpeg @('-y','-f','lavfi','-i',"sine=frequency=440:duration=$Seconds",'-c:a','flac',(Join-Path $out 'tone.flac'))

# Single video + audio MP4 (H.264 / AAC)
Invoke-Ffmpeg @(
    '-y',
    '-f','lavfi','-i',"testsrc2=size=640x360:rate=30:duration=$Seconds",
    '-f','lavfi','-i',"sine=frequency=440:duration=$Seconds",
    '-map','0:v','-map','1:a',
    '-c:v','libx264','-pix_fmt','yuv420p',
    '-c:a','aac','-b:a','128k',
    '-shortest',
    (Join-Path $out 'testcard.mp4'))

# MKV with two audio tracks (legally distinct tones + language metadata)
Invoke-Ffmpeg @(
    '-y',
    '-f','lavfi','-i',"testsrc2=size=640x360:rate=30:duration=$Seconds",
    '-f','lavfi','-i',"sine=frequency=440:duration=$Seconds",
    '-f','lavfi','-i',"sine=frequency=880:duration=$Seconds",
    '-map','0:v','-map','1:a','-map','2:a',
    '-c:v','libx264','-pix_fmt','yuv420p',
    '-c:a','aac','-b:a','128k',
    '-metadata:s:a:0','language=eng',
    '-metadata:s:a:1','language=hin',
    '-shortest',
    (Join-Path $out 'multi-audio.mkv'))

# MKV with an embedded subtitle track (external SRT kept as an additional fixture)
$srt = Join-Path $out 'subtitle-sample.srt'
Set-Content -Path $srt -Value @(
    '1',
    '00:00:00,500 --> 00:00:02,000',
    'Hello from PlaySphere.',
    '',
    '2',
    '00:00:02,500 --> 00:00:05,000',
    'Subtitles are on screen.',
    '') -Encoding utf8

Invoke-Ffmpeg @(
    '-y',
    '-i',(Join-Path $out 'testcard.mp4'),
    '-i',$srt,
    '-map','0','-map','1:0',
    '-c','copy','-c:s','srt',
    (Join-Path $out 'multi-subtitle.mkv'))

# Malformed / corrupted fixtures (error-path corpus)
Set-Content -Path (Join-Path $out 'malformed.m3u') -Value @(
    '#EXTM3U',
    '/path/that/does/not/exist.mp4',
    'garbage # not a valid entry') -Encoding ascii

$testcard = [System.IO.File]::ReadAllBytes((Join-Path $out 'testcard.mp4'))
[System.IO.File]::WriteAllBytes((Join-Path $out 'truncated.mp4'), $testcard[0..([Math]::Min(1023, $testcard.Length - 1))])

$rng = New-Object 'System.Random' 42
$garbage = New-Object 'byte[]' (2048)
$rng.NextBytes($garbage)
[System.IO.File]::WriteAllBytes((Join-Path $out 'corrupted.mp4'), $garbage)

# ---- 3. Verification ---------------------------------------------------------------
Write-Host ''
Write-Host '== Verification (ffprobe) ==' -ForegroundColor Cyan
$fixtures = 'sine.mp3','tone.wav','tone.flac','testcard.mp4','multi-audio.mkv','multi-subtitle.mkv'
foreach ($name in $fixtures) {
    $path = Join-Path $out $name
    if (-not (Test-Path $path)) { throw "Fixture missing: $path" }
    $json = Invoke-Ffprobe $path
    $streams = @($json.streams) | ForEach-Object { "$($_.codec_type)/$($_.codec_name)" }
    $dur = [Math]::Round([double]$json.format.duration, 2)
    Write-Host ("  {0,-22} {1,7}s  {2}" -f $name, $dur, ($streams -join ', '))
}

Write-Host ''
Write-Host 'Corpus generated and verified.' -ForegroundColor Green
Write-Host "Location: $out"