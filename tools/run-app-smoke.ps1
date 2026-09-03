# App launch smoke test: start the built app and confirm it stays alive.
[CmdletBinding()]
param(
    [int]$Seconds = 5
)

$ErrorActionPreference = 'Stop'
Write-Host '== PlaySphere app launch smoke test ==' -ForegroundColor Cyan

$repo = Split-Path -Parent $PSScriptRoot
$exe = Get-ChildItem -Path (Join-Path $repo 'src\PlaySphere.App\bin') -Recurse -Filter 'PlaySphere.App.exe' -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\.store\\.' } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $exe) {
    throw 'PlaySphere.App.exe not found. Run tools\bootstrap.ps1 then tools\build-release.ps1 first.'
}

Write-Host "Launching: $($exe.FullName)"
$proc = Start-Process -FilePath $exe.FullName -PassThru
Start-Sleep -Seconds $Seconds

if ($proc.HasExited) {
    throw "App exited during smoke window (exit code $($proc.ExitCode))."
}

Write-Host "Smoke OK: app stayed alive for ${Seconds}s (PID $($proc.Id))." -ForegroundColor Green
Stop-Process -Id $proc.Id -Force
$proc.WaitForExit()